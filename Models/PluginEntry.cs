using System;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RAPluginManifestEditor.Models;

/// <summary>Wraps a single &lt;PLUGIN .../&gt; element from HostMode.props.</summary>
public partial class PluginEntry : ObservableObject
{
    public XElement Element { get; }

    public string Name { get; }
    public string Manufacturer { get; }
    public string Format { get; }
    public string Category { get; }
    public string Version { get; }
    public string FilePath { get; }
    public string UniqueId { get; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    [ObservableProperty]
    public partial bool FromHistory { get; set; }

    /// <summary>Map status from the last "Check for missing maps" run — see MapAvailabilityService.</summary>
    [ObservableProperty]
    public partial MapAvailability MapAvailability { get; set; } = MapAvailability.Unknown;

    /// <summary>Checked for the (separate, future) "download maps" action. Distinct from
    /// <see cref="IsChecked"/>, which marks a plugin for removal — the two must never share state.</summary>
    [ObservableProperty]
    public partial bool IsSelectedForMap { get; set; }

    partial void OnMapAvailabilityChanged(MapAvailability value)
    {
        OnPropertyChanged(nameof(MapStatusLabel));
        OnPropertyChanged(nameof(IsMapDownloaded));
        OnPropertyChanged(nameof(IsMapMissing));
        OnPropertyChanged(nameof(IsMapNotAvailable));
    }

    public string MapStatusLabel => MapAvailability switch
    {
        MapAvailability.Downloaded => "Have map",
        MapAvailability.AvailableNotDownloaded => "Missing",
        MapAvailability.NotAvailable => "No map",
        _ => "—",
    };

    public bool IsMapDownloaded => MapAvailability == MapAvailability.Downloaded;
    public bool IsMapMissing => MapAvailability == MapAvailability.AvailableNotDownloaded;
    public bool IsMapNotAvailable => MapAvailability == MapAvailability.NotAvailable;

    public PluginEntry(XElement element)
    {
        Element = element;
        Name = element.Attribute("name")?.Value ?? "";
        Manufacturer = element.Attribute("manufacturer")?.Value ?? "";
        Format = element.Attribute("format")?.Value ?? "";
        Category = element.Attribute("category")?.Value ?? "";
        Version = element.Attribute("version")?.Value ?? "";
        FilePath = element.Attribute("file")?.Value ?? "";
        UniqueId = element.Attribute("uniqueId")?.Value ?? "";
    }

    /// <summary>Stable identity used to remember removals across rescans.</summary>
    public string Key
    {
        get
        {
            var id = string.IsNullOrEmpty(UniqueId) || UniqueId == "0"
                ? $"{Name}@{FilePath}"
                : UniqueId;
            return $"{Format}::{id}";
        }
    }
}
