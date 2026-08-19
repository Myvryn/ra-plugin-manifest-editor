using System;
using System.IO;

namespace RAPluginManifestEditor.Services;

/// <summary>Resolves an app-data directory for this tool's own settings/history,
/// independent of RA Control's own file locations. Cross-platform (Windows/macOS/Linux).</summary>
public static class AppPaths
{
    private const string FolderName = "RAPluginManifestEditor";

    public static string GetAppDataDir()
    {
        string baseDir;
        if (OperatingSystem.IsWindows())
        {
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            baseDir = Path.Combine(home, "Library", "Application Support");
        }
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            baseDir = !string.IsNullOrEmpty(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        var dir = Path.Combine(baseDir, FolderName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Best-guess default location of RA Control's HostMode.props, if it exists,
    /// so the "Open" dialog can start somewhere useful instead of the user's home folder.</summary>
    public static string? GuessHostModePropsDir()
    {
        string? candidate = null;
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            candidate = Path.Combine(appData, "Rocksolid Audio", "RA Control", "Host Mode");
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidate = Path.Combine(home, "Library", "Application Support", "Rocksolid Audio", "RA Control", "Host Mode");
        }

        return candidate is not null && Directory.Exists(candidate) ? candidate : null;
    }
}
