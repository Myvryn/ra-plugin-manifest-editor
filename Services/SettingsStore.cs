using System;
using System.IO;
using System.Text.Json;
using RAPluginManifestEditor.Models;

namespace RAPluginManifestEditor.Services;

public class SettingsStore
{
    private readonly string _path = Path.Combine(AppPaths.GetAppDataDir(), "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
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
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // best-effort; not worth surfacing a failure to persist "last opened file"
        }
    }
}
