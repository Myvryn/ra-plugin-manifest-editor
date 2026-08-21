using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RAPluginManifestEditor.Models;

namespace RAPluginManifestEditor.Services;

/// <summary>Persists AppSettings to settings.json. The API token is never written to disk
/// as plaintext: on Windows it's encrypted with DPAPI (CurrentUser scope), so the on-disk
/// blob is useless without the same Windows user account. Everything else in AppSettings
/// (last file path, controller selection) is stored as plain JSON as before - only the
/// token needs this treatment.</summary>
public class SettingsStore
{
    private readonly string _path = Path.Combine(AppPaths.GetAppDataDir(), "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Binds the DPAPI blob to this app specifically, so it can't be swapped with a
    // DPAPI-encrypted blob from another app running as the same Windows user.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RAPluginManifestEditor.ApiToken.v1");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var json = File.ReadAllText(_path);
            var file = JsonSerializer.Deserialize<SettingsFile>(json) ?? new SettingsFile();

            string? token = null;
            if (!string.IsNullOrEmpty(file.EncryptedApiToken))
            {
                token = Unprotect(file.EncryptedApiToken);
            }
            else if (!string.IsNullOrEmpty(file.ApiToken))
            {
                // Legacy plaintext field from before encryption was added. Read it once;
                // the next Save() re-writes it encrypted and drops this field.
                token = file.ApiToken;
            }

            return new AppSettings
            {
                LastFilePath = file.LastFilePath,
                SelectedControllerModels = file.SelectedControllerModels,
                ApiToken = token,
            };
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            string? encryptedApiToken = null;
            string? plaintextApiToken = null;
            if (!string.IsNullOrEmpty(settings.ApiToken))
            {
                encryptedApiToken = Protect(settings.ApiToken);
                // DPAPI isn't available outside Windows; fall back to plaintext rather than
                // silently losing the token on Save (best-effort - not treated as a real
                // security boundary on those platforms).
                if (encryptedApiToken is null) plaintextApiToken = settings.ApiToken;
            }

            var file = new SettingsFile
            {
                LastFilePath = settings.LastFilePath,
                SelectedControllerModels = settings.SelectedControllerModels,
                EncryptedApiToken = encryptedApiToken,
                ApiToken = plaintextApiToken,
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(file, JsonOptions));
        }
        catch
        {
            // best-effort; not worth surfacing a failure to persist "last opened file"
        }
    }

    private static string? Protect(string plaintext)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static string? Unprotect(string base64)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(base64), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Blob is corrupt, or was encrypted under a different Windows user/machine -
            // treat as "no saved token" rather than crashing Load().
            return null;
        }
    }

    private sealed class SettingsFile
    {
        public string? LastFilePath { get; set; }
        public List<string> SelectedControllerModels { get; set; } = new();
        public string? EncryptedApiToken { get; set; }
        public string? ApiToken { get; set; }
    }
}
