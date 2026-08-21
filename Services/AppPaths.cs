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

    /// <summary>Base folder where RA Control caches actually-downloaded, plugin-specific
    /// control Mappings ("Host Preset" JSON per plugin), laid out as
    /// Host Maps/&lt;ControllerModel&gt;/&lt;Manufacturer&gt;/&lt;Plugin&gt; (&lt;uniqueId&gt;).json
    /// — mirroring AvailableMaps.txt's own per-model folder scoping.
    ///
    /// This is NOT "Parameter Tables" (a separate, unrelated cache of a plugin's automatable
    /// parameter *names*, written whenever RA Control loads/scans a plugin regardless of
    /// whether any Mapping was ever downloaded for it — confirmed false-positive by direct
    /// testing: a plugin can have a Parameter Table and still not be controllable until its
    /// Mapping is explicitly downloaded). Confirmed by diffing the filesystem before/after a
    /// real download: only a file under this folder changes.</summary>
    public static string? GuessHostMapsDir()
    {
        string? candidate = null;
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            candidate = Path.Combine(docs, "Rocksolid Audio", "RA Control", "Host Maps");
        }

        return candidate is not null && Directory.Exists(candidate) ? candidate : null;
    }

    /// <summary>Same location as <see cref="GuessHostMapsDir"/>, but creates it if it
    /// doesn't exist yet (e.g. a fresh install that's never downloaded a Mapping through RA
    /// Control's own UI). Used when writing a Mapping ourselves.</summary>
    public static string GetOrCreateHostMapsDir()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dir = Path.Combine(docs, "Rocksolid Audio", "RA Control", "Host Maps");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
