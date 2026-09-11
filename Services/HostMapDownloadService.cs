using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using RAPluginManifestEditor.Models;

namespace RAPluginManifestEditor.Services;

/// <summary>Downloads real, plugin-specific control Mappings from RA Control's own backend
/// API and writes them to the same Host Maps cache RA Control itself reads from
/// (Documents\Rocksolid Audio\RA Control\Host Maps\...). Confirmed by direct test (see the
/// private ra-control-api-research repo) that RA Control applies a Mapping written this way
/// exactly as if its own "Download" button had been clicked — no HardwareComms/serial
/// involvement needed. Only ever writes a Mapping the API itself returned with real
/// "parameter" entries; never guesses or fabricates one.</summary>
public class HostMapDownloadService
{
    private const string ApiBase = "https://ra-control-api.rocksolidaudioinfo-326.workers.dev";
    private const string BasePath = "1s4AS0A7x98NbeO1MvLUcj8FpFmlfHG5l";
    private const int MaxConcurrency = 4;

    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions PrettyPrint = new() { WriteIndented = true, IndentSize = 4 };

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        // Matches headers RA Control's own client sends, including requesting compression —
        // /v1/drive/download responses come back gzip-encoded.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,*");
        client.DefaultRequestHeaders.Add("X-RA-Client", "RA Control");
        return client;
    }

    public record DownloadAllResult(int Attempted, int Downloaded, int Failed);

    /// <summary>Downloads a real Mapping for every plugin currently AvailableNotDownloaded
    /// (set by MapAvailabilityService.Evaluate or LiveMapAvailabilityService.EvaluateAsync)
    /// under whichever selected controller model it's found for first, writing it to Host
    /// Maps. Updates MapAvailability to Downloaded in place on success so the UI reflects it
    /// immediately, without needing a fresh offline pass.</summary>
    public async Task<DownloadAllResult> DownloadAllAsync(
        IReadOnlyList<PluginEntry> plugins,
        IReadOnlyCollection<string> selectedControllerModels,
        string apiToken,
        IProgress<(int done, int total)>? progress,
        CancellationToken cancellationToken)
    {
        var toDownload = plugins.Where(p => p.MapAvailability == MapAvailability.AvailableNotDownloaded).ToList();
        var models = new List<string>(selectedControllerModels);
        var hostMapsDir = AppPaths.GetOrCreateHostMapsDir();

        using var semaphore = new SemaphoreSlim(MaxConcurrency);
        var tasks = new List<Task>();
        int done = 0, downloaded = 0, failed = 0;
        var syncRoot = new object();

        foreach (var plugin in toDownload)
        {
            await semaphore.WaitAsync(cancellationToken);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var success = await DownloadForAnyModelAsync(plugin, models, hostMapsDir, apiToken, cancellationToken);
                    lock (syncRoot)
                    {
                        if (success)
                        {
                            downloaded++;
                            plugin.MapAvailability = MapAvailability.Downloaded;
                            plugin.IsSelectedForMap = false;
                        }
                        else
                        {
                            failed++;
                        }

                        done++;
                        progress?.Report((done, toDownload.Count));
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        return new DownloadAllResult(done, downloaded, failed);
    }

    private static async Task<bool> DownloadForAnyModelAsync(
        PluginEntry plugin, List<string> models, string hostMapsDir, string apiToken, CancellationToken cancellationToken)
    {
        foreach (var model in models)
        {
            var fileId = await FindFileIdAsync(model, plugin, apiToken, cancellationToken);
            if (string.IsNullOrEmpty(fileId)) continue;

            var body = await DownloadRealHostPresetAsync(fileId, apiToken, cancellationToken);
            if (body is null) continue;

            if (WriteHostMap(hostMapsDir, model, plugin, body.Value)) return true;
        }

        return false;
    }

    private static async Task<string?> FindFileIdAsync(
        string controllerModel, PluginEntry plugin, string apiToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/v1/drive/findFileInPath")
        {
            Content = JsonContent.Create(new FindFileRequest(
                BasePath,
                $"{controllerModel}/{plugin.Manufacturer}/",
                $"{plugin.Name} ({plugin.UniqueId}).json")),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var result = await response.Content.ReadFromJsonAsync<FindFileResponse>(cancellationToken: cancellationToken);
            return string.IsNullOrEmpty(result?.FileId) ? null : result.FileId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns null if the download failed outright, or if the body doesn't contain
    /// at least one real "parameter" entry (a stub/empty body isn't a usable Mapping).</summary>
    private static async Task<JsonElement?> DownloadRealHostPresetAsync(
        string fileId, string apiToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/v1/drive/download")
        {
            Content = JsonContent.Create(new DownloadRequest(fileId)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

        try
        {
            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("Host Preset", out var preset) || preset.ValueKind != JsonValueKind.Array)
                return null;

            var hasReal = preset.EnumerateArray()
                .Any(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("parameter", out _));
            return hasReal ? doc.RootElement.Clone() : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool WriteHostMap(string hostMapsDir, string model, PluginEntry plugin, JsonElement body)
    {
        try
        {
            var dir = Path.Combine(hostMapsDir, model, plugin.Manufacturer);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{plugin.Name} ({plugin.UniqueId}).json");
            File.WriteAllText(path, JsonSerializer.Serialize(body, PrettyPrint));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private record FindFileRequest(
        [property: JsonPropertyName("basePath")] string BasePath,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("fileName")] string FileName);

    private record FindFileResponse([property: JsonPropertyName("fileId")] string? FileId);

    private record DownloadRequest([property: JsonPropertyName("fileId")] string FileId);
}
