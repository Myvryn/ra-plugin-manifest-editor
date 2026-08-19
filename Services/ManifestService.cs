using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RAPluginManifestEditor.Models;

namespace RAPluginManifestEditor.Services;

public class ManifestLoadResult
{
    public required XDocument Document { get; init; }
    public required List<PluginEntry> Plugins { get; init; }
}

/// <summary>Loads, edits and saves RA Control's HostMode.props (a JUCE KnownPluginList XML file).</summary>
public class ManifestService
{
    public ManifestLoadResult Load(string path)
    {
        var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var plugins = doc.Descendants("PLUGIN")
            .Select(el => new PluginEntry(el))
            .ToList();
        return new ManifestLoadResult { Document = doc, Plugins = plugins };
    }

    public void RemoveFromDocument(IEnumerable<PluginEntry> toRemove)
    {
        foreach (var p in toRemove)
        {
            p.Element.Remove();
        }
    }

    /// <summary>Re-inserts a previously-removed plugin's XML back into the document.
    /// Returns the new PluginEntry, or null if the XML is missing/invalid or there's
    /// no &lt;KNOWNPLUGINS&gt; container to add it to.</summary>
    public PluginEntry? TryRestoreToDocument(XDocument document, string elementXml)
    {
        if (string.IsNullOrWhiteSpace(elementXml)) return null;

        var known = document.Descendants("KNOWNPLUGINS").FirstOrDefault();
        if (known is null) return null;

        XElement element;
        try
        {
            element = XElement.Parse(elementXml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        known.Add(element);
        return new PluginEntry(element);
    }

    /// <summary>Writes a timestamped backup of the file next to itself before it's overwritten.</summary>
    public string? BackupExisting(string path)
    {
        if (!File.Exists(path)) return null;
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileName(path);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = Path.Combine(dir, $"{name}.bak-{stamp}");
        File.Copy(path, backupPath, overwrite: false);
        return backupPath;
    }

    public void Save(XDocument document, string path)
    {
        BackupExisting(path);
        using var writer = new StreamWriter(path, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        document.Save(writer);
    }
}
