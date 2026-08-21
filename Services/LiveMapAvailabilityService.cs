using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RAPluginManifestEditor.Models;

namespace RAPluginManifestEditor.Services;

/// <summary>Live equivalent of MapAvailabilityService.Evaluate, using RA Control's own
/// backend API (findFileInPath) instead of the local AvailableMaps.txt snapshot, which can
/// be stale or (per investigation) doesn't fully distinguish a locally-generated Parameter
/// Table from a real, currently-downloadable physical control Mapping.
///
/// Requires a user-supplied API token (Settings) — this app ships with none embedded, and
/// never will; see the private ra-control-api-research repo for what that token is and how
/// to obtain your own copy from your own licensed RA Control install. Read-only: only calls
/// findFileInPath, never download.</summary>
public class LiveMapAvailabilityService
{
    private const string ApiBase = "https://ra-control-api.rocksolidaudioinfo-326.workers.dev";

    // The Google-Drive-style root folder RA Control's own map index lives under. Not a
    // secret by itself (useless without a valid token) - the same constant for everyone,
    // observed identically across multiple different controller/manufacturer lookups.
    private const string BasePath = "1s4AS0A7x98NbeO1MvLUcj8FpFmlfHG5l";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Cap on concurrent in-flight requests - stays a well-behaved client of RA
    /// Control's own API rather than firing hundreds of simultaneous requests.</summary>
    private const int MaxConcurrency = 4;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // Matches headers RA Control's own client sends. Its Qt network stack doesn't set
        // a User-Agent by default, which trips Cloudflare's bot-management heuristics.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,*");
        client.DefaultRequestHeaders.Add("X-RA-Client", "RA Control");
        return client;
    }

    public record LiveCheckResult(int Checked, int Confirmed, int Failed);

    /// <summary>Re-verifies, against RA Control's live API, only the plugins the offline
    /// pass (MapAvailabilityService.Evaluate) couldn't confidently resolve as "Downloaded"
    /// (i.e. no local Parameter Table) - those are exactly the ones where a stale or
    /// incomplete local AvailableMaps.txt can misclassify a plugin as NotAvailable (a false
    /// positive for removal) or AvailableNotDownloaded. Plugins already marked Downloaded
    /// are left untouched: a local Parameter Table is a reasonably strong signal on its own,
    /// and skipping them keeps the live pass fast and considerate of RA Control's API.
    ///
    /// Checks each plugin's own Manufacturer/Name/UniqueId against the selected controller
    /// model(s) directly - no dependency on the local AvailableMaps.txt cache at all, so a
    /// stale cache can't hide a real mapping. Sets MapAvailability directly. Progress is
    /// reported after each plugin completes.</summary>
    public async Task<LiveCheckResult> EvaluateAsync(
        IReadOnlyList<PluginEntry> plugins,
        IReadOnlyCollection<string> selectedControllerModels,
        string apiToken,
        IProgress<(int done, int total)>? progress,
        CancellationToken cancellationToken)
    {
        var toCheck = plugins.Where(p => p.MapAvailability != MapAvailability.Downloaded).ToList();
        var models = new List<string>(selectedControllerModels);
        var semaphore = new SemaphoreSlim(MaxConcurrency);
        var tasks = new List<Task>();
        int done = 0, confirmed = 0, failed = 0;
        var syncRoot = new object();

        foreach (var plugin in toCheck)
        {
            await semaphore.WaitAsync(cancellationToken);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var found = await IsAvailableForAnyModelAsync(plugin.Name, plugin.Manufacturer, plugin.UniqueId, models, apiToken, cancellationToken);
                    lock (syncRoot)
                    {
                        if (found is null)
                        {
                            failed++;
                        }
                        else
                        {
                            plugin.MapAvailability = found.Value ? MapAvailability.AvailableNotDownloaded : MapAvailability.NotAvailable;
                            plugin.IsSelectedForMap = found.Value;
                            plugin.IsChecked = !found.Value;
                            if (found.Value) confirmed++;
                        }

                        done++;
                        progress?.Report((done, toCheck.Count));
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        return new LiveCheckResult(done, confirmed, failed);
    }

    /// <summary>Null means the check itself failed (network error, bad token, etc.) -
    /// distinct from a confirmed "false".</summary>
    private static async Task<bool?> IsAvailableForAnyModelAsync(
        string name, string manufacturer, string uniqueId, List<string> models, string apiToken, CancellationToken cancellationToken)
    {
        foreach (var model in models)
        {
            var result = await FindFileInPathAsync(model, name, manufacturer, uniqueId, apiToken, cancellationToken);
            if (result is null) return null; // request itself failed - report as failed, don't guess
            if (result.Value) return true;
        }

        return false;
    }

    private static async Task<bool?> FindFileInPathAsync(
        string controllerModel, string name, string manufacturer, string uniqueId, string apiToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/v1/drive/findFileInPath")
        {
            Content = JsonContent.Create(new FindFileRequest(
                BasePath,
                $"{controllerModel}/{manufacturer}/",
                $"{name} ({uniqueId}).json")),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadFromJsonAsync<FindFileResponse>(cancellationToken: cancellationToken);
            return !string.IsNullOrEmpty(body?.FileId);
        }
        catch
        {
            return null;
        }
    }

    public record RemovedPluginCheckResult(int Checked, List<HistoryEntry> NowAvailable, int Failed);

    // Matches HistoryEntry.Key's fallback-free form ("<Format>::<UniqueId>") so older
    // history entries (saved before HistoryEntry.UniqueId existed) can still be checked,
    // by recovering the id from the same string that was already being persisted. The
    // Name@FilePath fallback form (used when a plugin never had a real uniqueId) doesn't
    // match this pattern and is correctly excluded - there's nothing to look up for those.
    private static readonly Regex KeyUniqueId = new(@"^[^:]+::([0-9a-fA-F]{4,16})$", RegexOptions.Compiled);

    private static string? ResolveUniqueId(HistoryEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.UniqueId) && entry.UniqueId != "0") return entry.UniqueId;
        var match = KeyUniqueId.Match(entry.Key);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Re-checks previously-removed plugins (removal history) against RA Control's
    /// live API, to catch the case where RA Control has since published a map for something
    /// that was removed for lacking one - "Have map" is time-varying (RA Control periodically
    /// releases new maps), but removal history is a one-time decision, so nothing else in
    /// this app ever revisits a removed plugin's map status after the fact. Only entries a
    /// uniqueId can be recovered for (see <see cref="ResolveUniqueId"/>) can be checked.
    /// Doesn't mutate anything - the caller decides what to do with the hits (e.g. offer to
    /// restore them).</summary>
    public async Task<RemovedPluginCheckResult> CheckRemovedForNewMapsAsync(
        IReadOnlyList<HistoryEntry> removedPlugins,
        IReadOnlyCollection<string> selectedControllerModels,
        string apiToken,
        IProgress<(int done, int total)>? progress,
        CancellationToken cancellationToken)
    {
        var toCheck = removedPlugins
            .Select(h => (Entry: h, UniqueId: ResolveUniqueId(h)))
            .Where(x => x.UniqueId is not null)
            .ToList();
        var models = new List<string>(selectedControllerModels);
        var semaphore = new SemaphoreSlim(MaxConcurrency);
        var tasks = new List<Task>();
        int done = 0, failed = 0;
        var nowAvailable = new List<HistoryEntry>();
        var syncRoot = new object();

        foreach (var (entry, uniqueId) in toCheck)
        {
            await semaphore.WaitAsync(cancellationToken);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var found = await IsAvailableForAnyModelAsync(entry.Name, entry.Manufacturer, uniqueId!, models, apiToken, cancellationToken);
                    lock (syncRoot)
                    {
                        if (found is null) failed++;
                        else if (found.Value) nowAvailable.Add(entry);

                        done++;
                        progress?.Report((done, toCheck.Count));
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        return new RemovedPluginCheckResult(done, nowAvailable, failed);
    }

    private record FindFileRequest(
        [property: JsonPropertyName("basePath")] string BasePath,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("fileName")] string FileName);

    private record FindFileResponse([property: JsonPropertyName("fileId")] string? FileId);
}
