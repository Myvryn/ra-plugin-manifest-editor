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
