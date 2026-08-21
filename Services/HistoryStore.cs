using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using RAPluginManifestEditor.Models;

namespace RAPluginManifestEditor.Services;

public class HistoryStore
{
    private readonly string _path = Path.Combine(AppPaths.GetAppDataDir(), "removal-history.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<HistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new List<HistoryEntry>();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<HistoryEntry>>(json) ?? new List<HistoryEntry>();
        }
        catch
        {
            return new List<HistoryEntry>();
        }
    }

    public void Save(List<HistoryEntry> entries)
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(entries, JsonOptions));
    }

    public void Upsert(IEnumerable<PluginEntry> removed)
    {
        var list = Load();
        var byKey = list.ToDictionary(h => h.Key);
        foreach (var p in removed)
        {
            byKey[p.Key] = new HistoryEntry
            {
                Key = p.Key,
                Name = p.Name,
                Manufacturer = p.Manufacturer,
                UniqueId = p.UniqueId,
                Format = p.Format,
                Category = p.Category,
                File = p.FilePath,
                RemovedAt = System.DateTimeOffset.Now,
                ElementXml = p.Element.ToString(SaveOptions.DisableFormatting),
            };
        }
        Save(byKey.Values.ToList());
    }

    public void Forget(string key)
    {
        var list = Load();
        list.RemoveAll(h => h.Key == key);
        Save(list);
    }

    public void Clear() => Save(new List<HistoryEntry>());
}
