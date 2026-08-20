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

    /// <summary>RA Control's own local index of every map it knows is available upstream
    /// (one relative path per line, e.g. "Acqua/AMBER4PRE (da8253ed).json"). RA Control
    /// refreshes this itself; nothing here fetches it from the network.</summary>
    public static string? GuessAvailableMapsPath()
    {
        var hostModeDir = GuessHostModePropsDir();
        if (hostModeDir is null) return null;

        var path = Path.Combine(hostModeDir, "AvailableMaps.txt");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Folder where RA Control stores maps it has already downloaded for this user
    /// (flat *.json files named "PluginName (uniqueId).json").</summary>
    public static string? GuessDownloadedMapsDir()
    {
        var hostModeDir = GuessHostModePropsDir();
        if (hostModeDir is null) return null;

        var dir = Path.Combine(hostModeDir, "Parameter Tables");
        return Directory.Exists(dir) ? dir : null;
    }
}
