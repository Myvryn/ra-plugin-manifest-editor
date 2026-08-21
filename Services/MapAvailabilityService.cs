using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using RAPluginManifestEditor.Models;

namespace RAPluginManifestEditor.Services;

public class MapCheckResult
{
    /// <summary>False if RA Control's map cache (AvailableMaps.txt) couldn't be found —
    /// e.g. RA Control has never been run, or Host Mode has never been opened in it.</summary>
    public required bool CacheFound { get; init; }

    /// <summary>True if the cache was found but no controller model is selected in
    /// Settings, so "available" couldn't be scoped to the user's actual hardware.</summary>
    public required bool NoControllersSelected { get; init; }

    public required int Available { get; init; }
    public required int Downloaded { get; init; }
    public required int Missing { get; init; }
    public required int NotAvailable { get; init; }
}

/// <summary>Cross-references the loaded manifest's plugins against RA Control's own
/// locally-cached map index (AvailableMaps.txt) and its Host Maps folder (actually-downloaded,
/// plugin-specific control Mappings — see AppPaths.GuessHostMapsDir), purely by reading files
/// RA Control itself already maintains on disk. Makes no network calls of its own.
///
/// AvailableMaps.txt is organized per controller hardware model (one top-level folder per
/// model, e.g. "Micro 3/Waves/..."), so "available" is scoped to whichever model(s) the
/// user selected in Settings — a map listed only under hardware they don't own isn't
/// actually available to them.
///
/// Plugins with no map available for the selected controller(s) are auto-checked for
/// removal (IsChecked), same as history-based auto-selection. Plugins with a map available
/// but not yet downloaded are auto-selected via IsSelectedForMap instead — a separate flag
/// so "flagged to fetch a map" and "flagged for removal" can never collide.</summary>
public class MapAvailabilityService
{
    private static readonly Regex UniqueIdInParens = new(@"\(([0-9a-fA-F]{4,16})\)\.json$", RegexOptions.Compiled);

    /// <summary>Every controller model name AvailableMaps.txt has entries for (its
    /// top-level folder names), for populating the Settings dialog. Empty if the cache
    /// hasn't been found yet.</summary>
    public List<string> GetKnownControllerModels()
    {
        var path = AppPaths.GuessAvailableMapsPath();
        if (path is null) return new List<string>();

        var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path))
        {
            var model = TopLevelFolder(line);
            if (model is not null) models.Add(model);
        }
        return models.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public MapCheckResult Evaluate(IEnumerable<PluginEntry> plugins, IReadOnlyCollection<string> selectedControllerModels)
    {
        var availablePath = AppPaths.GuessAvailableMapsPath();
        var hostMapsDir = AppPaths.GuessHostMapsDir();

        if (availablePath is null)
        {
            foreach (var p in plugins) p.MapAvailability = MapAvailability.Unknown;
            return new MapCheckResult
            {
                CacheFound = false, NoControllersSelected = false,
                Available = 0, Downloaded = 0, Missing = 0, NotAvailable = 0,
            };
        }

        if (selectedControllerModels.Count == 0)
        {
            foreach (var p in plugins) p.MapAvailability = MapAvailability.Unknown;
            return new MapCheckResult
            {
                CacheFound = true, NoControllersSelected = true,
                Available = 0, Downloaded = 0, Missing = 0, NotAvailable = 0,
            };
        }

        var selected = new HashSet<string>(selectedControllerModels, StringComparer.OrdinalIgnoreCase);
        var availableIds = LoadAvailableUniqueIds(File.ReadLines(availablePath), selected);
        var downloadedIds = hostMapsDir is null
            ? new HashSet<string>()
            : LoadDownloadedUniqueIds(hostMapsDir, selected);

        int available = 0, downloaded = 0, missing = 0, notAvailable = 0;
        foreach (var p in plugins)
        {
            var id = NormalizeId(p.UniqueId);
            if (id is null || !availableIds.Contains(id))
            {
                p.MapAvailability = MapAvailability.NotAvailable;
                p.IsChecked = true;
                notAvailable++;
                continue;
            }

            available++;
            if (downloadedIds.Contains(id))
            {
                p.MapAvailability = MapAvailability.Downloaded;
                downloaded++;
            }
            else
            {
                p.MapAvailability = MapAvailability.AvailableNotDownloaded;
                p.IsSelectedForMap = true;
                missing++;
            }
        }

        return new MapCheckResult
        {
            CacheFound = true,
            NoControllersSelected = false,
            Available = available,
            Downloaded = downloaded,
            Missing = missing,
            NotAvailable = notAvailable,
        };
    }

    private static string? NormalizeId(string uniqueId)
    {
        if (string.IsNullOrWhiteSpace(uniqueId) || uniqueId == "0") return null;
        return uniqueId.Trim().ToLowerInvariant();
    }

    private static string? TopLevelFolder(string relativePath)
    {
        var slash = relativePath.IndexOf('/');
        return slash > 0 ? relativePath[..slash] : null;
    }

    private static HashSet<string> LoadAvailableUniqueIds(IEnumerable<string> lines, HashSet<string> selectedControllerModels)
    {
        var ids = new HashSet<string>();
        foreach (var line in lines)
        {
            var model = TopLevelFolder(line);
            if (model is null || !selectedControllerModels.Contains(model)) continue;

            var match = UniqueIdInParens.Match(line);
            if (match.Success) ids.Add(match.Groups[1].Value.ToLowerInvariant());
        }
        return ids;
    }

    /// <summary>Scans Host Maps/&lt;model&gt;/ for each selected controller model and returns
    /// the uniqueIds of plugins with a real, downloaded control Mapping — filtering out stub
    /// files RA Control sometimes writes (e.g. just {"Host Preset": [{"Parameter Count": N}]}
    /// with no actual "parameter" entries) which aren't a usable Mapping.</summary>
    private static HashSet<string> LoadDownloadedUniqueIds(string hostMapsDir, HashSet<string> selectedControllerModels)
    {
        var ids = new HashSet<string>();
        foreach (var model in selectedControllerModels)
        {
            var modelDir = Path.Combine(hostMapsDir, model);
            if (!Directory.Exists(modelDir)) continue;

            foreach (var file in Directory.EnumerateFiles(modelDir, "*.json", SearchOption.AllDirectories))
            {
                var match = UniqueIdInParens.Match(file);
                if (match.Success && IsRealHostPreset(file)) ids.Add(match.Groups[1].Value.ToLowerInvariant());
            }
        }
        return ids;
    }

    private static bool IsRealHostPreset(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("Host Preset", out var preset) || preset.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var entry in preset.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("parameter", out _)) return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return false;
        }
    }
}
