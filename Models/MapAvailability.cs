namespace RAPluginManifestEditor.Models;

/// <summary>A plugin's map status relative to RA Control's own local map cache
/// (AvailableMaps.txt) and downloaded maps (Parameter Tables) — see MapAvailabilityService.</summary>
public enum MapAvailability
{
    /// <summary>Not yet checked this session.</summary>
    Unknown,

    /// <summary>A map is available upstream and already downloaded locally.</summary>
    Downloaded,

    /// <summary>A map is available upstream but not downloaded yet.</summary>
    AvailableNotDownloaded,

    /// <summary>No map exists upstream for this plugin's uniqueId.</summary>
    NotAvailable,
}
