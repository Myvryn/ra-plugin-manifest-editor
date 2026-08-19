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

    /// <summary>Full original &lt;PLUGIN .../&gt; XML, captured at removal time so the
    /// plugin can be reconstructed exactly if the user chooses to restore it.</summary>
    public string ElementXml { get; set; } = "";
}
