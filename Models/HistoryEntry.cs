using System;

namespace RAPluginManifestEditor.Models;

/// <summary>A persisted record of a plugin the user removed, so it can be
/// auto-selected again if a rescan brings it back into HostMode.props.</summary>
public class HistoryEntry
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Format { get; set; } = "";
    public string Category { get; set; } = "";
    public string File { get; set; } = "";
    public DateTimeOffset RemovedAt { get; set; }

    /// <summary>RA Control's own plugin identifier, captured at removal time so a later
    /// live re-check can look up whether a map has since been published for this plugin -
    /// see LiveMapAvailabilityService.CheckRemovedForNewMapsAsync. Empty for entries saved
    /// before this field existed (that service falls back to parsing Key for those).</summary>
    public string UniqueId { get; set; } = "";

    /// <summary>Full original &lt;PLUGIN .../&gt; XML, captured at removal time so the
    /// plugin can be reconstructed exactly if the user chooses to restore it.</summary>
    public string ElementXml { get; set; } = "";
}
