using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RAPluginManifestEditor.Models;
using RAPluginManifestEditor.Services;

namespace RAPluginManifestEditor.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ManifestService _manifestService = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly HistoryStore _historyStore = new();
    private readonly MapAvailabilityService _mapAvailabilityService = new();
    private readonly LiveMapAvailabilityService _liveMapAvailabilityService = new();
    private readonly HostMapDownloadService _hostMapDownloadService = new();

    private XDocument? _document;
    private List<PluginEntry> _allPlugins = new();

    /// <summary>Set by the view once a TopLevel is available, so commands here can show native file dialogs.</summary>
    public IStorageProvider? StorageProvider { get; set; }

    /// <summary>Confirmation hook the view wires up (Avalonia has no built-in MessageBox).</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    public MainViewModel()
    {
        RefreshHistoryPanel();
        // Load the saved API token up front so "live" mode (CanLiveCheck) reflects reality
        // from launch - previously this only happened when Settings was opened, so a
        // saved token silently had no effect until the user opened and closed Settings once.
        ApiToken = _settingsStore.Load().ApiToken;
    }

    // ------------------------------------------------------------------
    // Observable state
    // ------------------------------------------------------------------

    [ObservableProperty]
    public partial bool IsFileLoaded { get; set; }

    [ObservableProperty]
    public partial string? CurrentFilePath { get; set; }

    [ObservableProperty]
    public partial string StatusLog { get; set; } = "";

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial string SelectedFormat { get; set; } = AllFormatsLabel;

    [ObservableProperty]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    public partial int ShownCount { get; set; }

    [ObservableProperty]
    public partial int CheckedCount { get; set; }

    /// <summary>Backs the table header's select-all checkbox. Getter: true only if every
    /// currently filtered/visible plugin is checked (no indeterminate/tri-state - this
    /// environment's CheckBox theme doesn't render bool? null as indeterminate, so a plain
    /// two-state checkbox is used instead: unchecked covers both "none" and "some" checked).
    /// Setter ignores the incoming value and is deterministic instead - checks every
    /// filtered plugin unless they're already all checked, in which case it unchecks them -
    /// so a click always checks the rest first, matching standard data-grid header-checkbox
    /// behavior. Scoped to FilteredPlugins, not _allPlugins, so it only ever reflects/affects
    /// what's actually visible under the current search/format filter.</summary>
    public bool HeaderCheckState
    {
        get => FilteredPlugins.Count > 0 && FilteredPlugins.All(p => p.IsChecked);
        set
        {
            var shouldCheck = !(FilteredPlugins.Count > 0 && FilteredPlugins.All(p => p.IsChecked));
            foreach (var p in FilteredPlugins) p.IsChecked = shouldCheck;
        }
    }

    [ObservableProperty]
    public partial int AutoCheckedCount { get; set; }

    [ObservableProperty]
    public partial int RemovedThisSessionCount { get; set; }

    [ObservableProperty]
    public partial bool HasCheckedMaps { get; set; }

    [ObservableProperty]
    public partial int MapsMissingCount { get; set; }

    [ObservableProperty]
    public partial int MapsDownloadedCount { get; set; }

    [ObservableProperty]
    public partial int MapsNotAvailableCount { get; set; }

    [ObservableProperty]
    public partial int SelectedForMapCount { get; set; }

    [ObservableProperty]
    public partial bool IsHistoryOpen { get; set; }

    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    [ObservableProperty]
    public partial bool HasKnownControllerModels { get; set; }

    public ObservableCollection<ControllerModelOption> ControllerModelOptions { get; } = new();

    [ObservableProperty]
    public partial string? ApiToken { get; set; }

    [ObservableProperty]
    public partial bool IsLiveChecking { get; set; }

    [ObservableProperty]
    public partial string? LiveCheckProgressLabel { get; set; }

    [ObservableProperty]
    public partial bool IsDownloadingMaps { get; set; }

    [ObservableProperty]
    public partial string? DownloadMapsProgressLabel { get; set; }

    public bool CanLiveCheck => !string.IsNullOrWhiteSpace(ApiToken);

    partial void OnApiTokenChanged(string? value)
    {
        OnPropertyChanged(nameof(CanLiveCheck));
        OnPropertyChanged(nameof(CheckMapsMenuLabel));
    }

    /// <summary>Label for the toolbar's consolidated "Maps" dropdown button - shows whichever
    /// map operation is currently running (live check or download-all), or just "Maps" when
    /// idle. The individual operations' own progress labels still drive their menu item text.</summary>
    public string MapsButtonLabel =>
        IsDownloadingMaps ? DownloadMapsProgressLabel ?? "Downloading…"
        : IsLiveChecking ? LiveCheckProgressLabel ?? "Checking…"
        : "Maps";

    /// <summary>Label for the single "Check for missing maps" menu item, which runs live
    /// (via <see cref="CheckMapAvailabilityAsync"/>) when an API token is set - since the
    /// live pass already runs the offline pass first, there's nothing an offline-only label
    /// would add once a token exists.</summary>
    public string CheckMapsMenuLabel =>
        IsLiveChecking ? LiveCheckProgressLabel ?? "Checking…"
        : CanLiveCheck ? "Check for missing maps (live)"
        : "Check for missing maps";

    partial void OnIsLiveCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(MapsButtonLabel));
        OnPropertyChanged(nameof(CheckMapsMenuLabel));
    }

    partial void OnLiveCheckProgressLabelChanged(string? value)
    {
        OnPropertyChanged(nameof(MapsButtonLabel));
        OnPropertyChanged(nameof(CheckMapsMenuLabel));
    }

    partial void OnIsDownloadingMapsChanged(bool value) => OnPropertyChanged(nameof(MapsButtonLabel));
    partial void OnDownloadMapsProgressLabelChanged(string? value) => OnPropertyChanged(nameof(MapsButtonLabel));

    [ObservableProperty]
    public partial string? LastKnownFileHint { get; set; }

    public bool HasLastKnownFileHint => !string.IsNullOrEmpty(LastKnownFileHint);

    partial void OnLastKnownFileHintChanged(string? value) => OnPropertyChanged(nameof(HasLastKnownFileHint));

    public const string AllFormatsLabel = "All formats";

    public ObservableCollection<string> Formats { get; } = new() { AllFormatsLabel };
    public ObservableCollection<PluginEntry> FilteredPlugins { get; } = new();
    public ObservableCollection<HistoryEntry> HistoryItems { get; } = new();

    private string _sortKey = "Name";
    private bool _sortAscending = true;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedFormatChanged(string value) => ApplyFilter();

    // ------------------------------------------------------------------
    // Startup / resume
    // ------------------------------------------------------------------

    public async Task TryAutoResumeAsync()
    {
        var settings = _settingsStore.Load();
        if (string.IsNullOrEmpty(settings.LastFilePath)) return;

        if (File.Exists(settings.LastFilePath))
        {
            await LoadFileAsync(settings.LastFilePath);
        }
        else
        {
            LastKnownFileHint = settings.LastFilePath;
        }
    }

    // ------------------------------------------------------------------
    // File open / reload / save
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        if (StorageProvider is null) return;

        IStorageFolder? startFolder = null;
        var guessed = AppPaths.GuessHostModePropsDir();
        if (guessed is not null)
        {
            startFolder = await StorageProvider.TryGetFolderFromPathAsync(guessed);
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open HostMode.props",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("RA Control manifest") { Patterns = new[] { "*.props", "*.xml" } },
                FilePickerFileTypes.All,
            },
        });

        var file = files.FirstOrDefault();
        if (file is null) return;

        var path = file.Path.LocalPath;
        await LoadFileAsync(path);
    }

    [RelayCommand]
    private async Task ReloadFromDiskAsync()
    {
        if (CurrentFilePath is null) return;
        if (!File.Exists(CurrentFilePath))
        {
            StatusLog = $"Can't reload — \"{CurrentFilePath}\" no longer exists.";
            return;
        }
        await LoadFileAsync(CurrentFilePath);
        StatusLog = $"Reloaded \"{Path.GetFileName(CurrentFilePath)}\" from disk.";
    }

    private void WireCountRecompute(PluginEntry p)
    {
        p.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PluginEntry.IsChecked)) RecomputeCounts();
            if (e.PropertyName == nameof(PluginEntry.IsSelectedForMap)) RecomputeCounts();
        };
    }

    private async Task LoadFileAsync(string path)
    {
        try
        {
            var result = await Task.Run(() => _manifestService.Load(path));
            _document = result.Document;

            var historyKeys = _historyStore.Load().Select(h => h.Key).ToHashSet();
            _allPlugins = result.Plugins;

            int autoChecked = 0;
            foreach (var p in _allPlugins)
            {
                var inHistory = historyKeys.Contains(p.Key);
                p.FromHistory = inHistory;
                p.IsChecked = inHistory;
                if (inHistory) autoChecked++;
                WireCountRecompute(p);
            }

            Formats.Clear();
            Formats.Add(AllFormatsLabel);
            foreach (var fmt in _allPlugins.Select(p => p.Format).Distinct().OrderBy(f => f))
                Formats.Add(fmt);
            SelectedFormat = AllFormatsLabel;

            CurrentFilePath = path;
            IsFileLoaded = true;
            AutoCheckedCount = autoChecked;
            RemovedThisSessionCount = 0;
            LastKnownFileHint = null;
            HasCheckedMaps = false;
            MapsMissingCount = 0;
            MapsDownloadedCount = 0;
            MapsNotAvailableCount = 0;

            ApplyFilter();
            var settings = _settingsStore.Load();
            settings.LastFilePath = path;
            _settingsStore.Save(settings);

            StatusLog = $"Loaded \"{Path.GetFileName(path)}\" — {_allPlugins.Count} plugin(s)." +
                        (autoChecked > 0 ? $" {autoChecked} auto-selected from removal history." : "");
        }
        catch (Exception ex)
        {
            StatusLog = $"Failed to load \"{path}\": {ex.Message}";
        }
    }

    /// <summary>If any plugins are checked but the user never clicked "Remove checked",
    /// warn them before a save silently leaves those plugins in the manifest. Returns
    /// false only if the user wants to stop and go back (e.g. to review their selection
    /// some other way) rather than save at all — in every other case the caller should
    /// proceed with saving.</summary>
    private async Task<bool> ConfirmPendingCheckedBeforeSaveAsync()
    {
        var pendingChecked = _allPlugins.Where(p => p.IsChecked).ToList();
        if (pendingChecked.Count == 0) return true;

        var shouldRemove = ConfirmAsync is not null && await ConfirmAsync(
            "Checked plugins not removed",
            $"You've checked {pendingChecked.Count} plugin(s) but haven't clicked \"Remove checked\" yet — " +
            "they'll stay in the manifest as-is if you save now.\n\n" +
            "Remove them before saving?");
        if (shouldRemove) PerformRemoveChecked(pendingChecked);

        return true;
    }

    private void PerformRemoveChecked(List<PluginEntry> toRemove)
    {
        _historyStore.Upsert(toRemove);
        _manifestService.RemoveFromDocument(toRemove);

        var removedSet = toRemove.ToHashSet();
        _allPlugins = _allPlugins.Where(p => !removedSet.Contains(p)).ToList();
        RemovedThisSessionCount += toRemove.Count;

        ApplyFilter();
        RefreshHistoryPanel();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_document is null || CurrentFilePath is null) return;
        if (!await ConfirmPendingCheckedBeforeSaveAsync()) return;

        try
        {
            await Task.Run(() => _manifestService.Save(_document, CurrentFilePath));
            RemovedThisSessionCount = 0;
            StatusLog = $"Saved \"{Path.GetFileName(CurrentFilePath)}\" — {_allPlugins.Count} plugin(s) remain. A timestamped backup was written alongside it.";
        }
        catch (Exception ex)
        {
            StatusLog = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAsCopyAsync()
    {
        if (_document is null || StorageProvider is null) return;
        if (!await ConfirmPendingCheckedBeforeSaveAsync()) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save edited copy as",
            SuggestedFileName = CurrentFilePath is not null ? Path.GetFileName(CurrentFilePath) : "HostMode.props",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("RA Control manifest") { Patterns = new[] { "*.props" } },
            },
        });
        if (file is null) return;

        try
        {
            await Task.Run(() => _manifestService.Save(_document, file.Path.LocalPath));
            StatusLog = $"Saved a copy to \"{file.Path.LocalPath}\".";
        }
        catch (Exception ex)
        {
            StatusLog = $"Save-as failed: {ex.Message}";
        }
    }

    // ------------------------------------------------------------------
    // Selection / removal
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task RemoveCheckedAsync()
    {
        var toRemove = _allPlugins.Where(p => p.IsChecked).ToList();
        if (toRemove.Count == 0) return;

        var confirmed = ConfirmAsync is null || await ConfirmAsync(
            "Remove plugins",
            $"Remove {toRemove.Count} plugin(s) from the manifest?\n\n" +
            "They'll also be remembered in the removal history, so they stay auto-selected if a rescan brings them back.\n\n" +
            "This only affects the in-memory list until you click \"Save changes\".");
        if (!confirmed) return;

        PerformRemoveChecked(toRemove);
    }

    // ------------------------------------------------------------------
    // Map availability (offline check against RA Control's own local cache)
    // ------------------------------------------------------------------

    /// <summary>Checks for missing maps: runs live against RA Control's own API if an API
    /// token is set in Settings (since that pass runs the offline check first anyway - see
    /// <see cref="RunLiveMapCheckAsync"/> - there's no reason to offer the offline-only
    /// result as a separate option once live is available), otherwise falls back to the
    /// offline-only cache check below.</summary>
    [RelayCommand]
    private async Task CheckMapAvailabilityAsync()
    {
        if (_allPlugins.Count == 0) return;

        if (CanLiveCheck)
            await RunLiveMapCheckAsync();
        else
            RunOfflineMapCheck();
    }

    /// <summary>Cross-references every loaded plugin's uniqueId against RA Control's own
    /// local map cache (AvailableMaps.txt + Parameter Tables) — no network calls. Plugins
    /// with a map available but not yet downloaded are auto-selected via IsSelectedForMap,
    /// which is intentionally separate from IsChecked (removal) so the two can't collide.</summary>
    private void RunOfflineMapCheck()
    {
        var selectedModels = _settingsStore.Load().SelectedControllerModels;
        var result = _mapAvailabilityService.Evaluate(_allPlugins, selectedModels);
        HasCheckedMaps = true;
        MapsDownloadedCount = result.Downloaded;
        MapsMissingCount = result.Missing;
        MapsNotAvailableCount = result.NotAvailable;
        RecomputeCounts();

        StatusLog = result.NoControllersSelected
            ? "Select which RA Control device(s) you have in Settings first — map availability is specific to your hardware."
            : result.CacheFound
                ? $"Checked maps — {result.Missing} plugin(s) have a map available but not downloaded" +
                  (result.Missing > 0 ? " (auto-selected)." : ".") +
                  $" {result.Downloaded} already have a map on disk." +
                  $" {result.NotAvailable} have no map upstream" +
                  (result.NotAvailable > 0 ? " (auto-checked for removal)." : ".")
                : "Couldn't find RA Control's map cache (AvailableMaps.txt) — open Host Mode in RA " +
                  "Control at least once so it can build its map index, then try again.";
    }

    /// <summary>Re-verifies, live against RA Control's own API, only the plugins the
    /// offline check above couldn't confidently mark "Downloaded" (no local Parameter
    /// Table) - fixes cases where a stale/incomplete AvailableMaps.txt snapshot would
    /// otherwise misflag a plugin as having no map at all (and auto-check it for removal).
    /// Requires a user-supplied API token in Settings; this app never ships one. Called only
    /// from <see cref="CheckMapAvailabilityAsync"/> once it's confirmed a token is set.</summary>
    private async Task RunLiveMapCheckAsync()
    {
        if (IsLiveChecking || string.IsNullOrWhiteSpace(ApiToken)) return;

        var selectedModels = _settingsStore.Load().SelectedControllerModels;
        if (selectedModels.Count == 0)
        {
            StatusLog = "Select which RA Control device(s) you have in Settings first — map availability is specific to your hardware.";
            return;
        }

        IsLiveChecking = true;
        LiveCheckProgressLabel = "Starting…";
        try
        {
            // Always (re-)run the offline pass first so plugins with a local Parameter
            // Table are marked Downloaded before filtering — otherwise every plugin is
            // still at its default Unknown and gets swept into the live pass, which can
            // only ever produce AvailableNotDownloaded/NotAvailable, never Downloaded.
            _mapAvailabilityService.Evaluate(_allPlugins, selectedModels);

            var progress = new Progress<(int done, int total)>(p => LiveCheckProgressLabel = $"Checking {p.done}/{p.total}…");
            var result = await _liveMapAvailabilityService.EvaluateAsync(
                _allPlugins, selectedModels, ApiToken.Trim(), progress, CancellationToken.None);

            MapsDownloadedCount = _allPlugins.Count(p => p.MapAvailability == MapAvailability.Downloaded);
            MapsMissingCount = _allPlugins.Count(p => p.MapAvailability == MapAvailability.AvailableNotDownloaded);
            MapsNotAvailableCount = _allPlugins.Count(p => p.MapAvailability == MapAvailability.NotAvailable);
            RecomputeCounts();

            StatusLog = result.Failed > 0
                ? $"Live check: {result.Checked} plugin(s) re-verified, {result.Confirmed} confirmed available, " +
                  $"{result.Failed} failed (check your API token and network connection)."
                : $"Live check complete — {result.Checked} plugin(s) re-verified against RA Control's live API, " +
                  $"{result.Confirmed} confirmed available.";
        }
        finally
        {
            IsLiveChecking = false;
            LiveCheckProgressLabel = null;
        }
    }

    /// <summary>Downloads a real Mapping for every plugin currently AvailableNotDownloaded,
    /// writing it to RA Control's own Host Maps cache exactly as if its "Download" button had
    /// been clicked for each one (confirmed by direct test — see the private
    /// ra-control-api-research repo). Requires an API token in Settings; this app never ships
    /// one. Run "Check for missing maps (live)" first for the most accurate list of what
    /// actually needs downloading.</summary>
    [RelayCommand]
    private async Task DownloadAllMapsAsync()
    {
        if (IsDownloadingMaps || IsLiveChecking || _allPlugins.Count == 0 || string.IsNullOrWhiteSpace(ApiToken)) return;

        var selectedModels = _settingsStore.Load().SelectedControllerModels;
        if (selectedModels.Count == 0)
        {
            StatusLog = "Select which RA Control device(s) you have in Settings first — map availability is specific to your hardware.";
            return;
        }

        var toDownload = _allPlugins.Count(p => p.MapAvailability == MapAvailability.AvailableNotDownloaded);
        if (toDownload == 0)
        {
            StatusLog = "Nothing to download — no plugins are currently marked as having a map available but not downloaded.";
            return;
        }

        var confirmed = ConfirmAsync is null || await ConfirmAsync(
            "Download all maps",
            $"Download {toDownload} Mapping(s) from RA Control's own API and write them to your local Host Maps " +
            "folder — the same place RA Control's own \"Download\" button writes to? RA Control will pick these up " +
            "next time it loads each plugin.");
        if (!confirmed) return;

        IsDownloadingMaps = true;
        DownloadMapsProgressLabel = "Starting…";
        try
        {
            var progress = new Progress<(int done, int total)>(p => DownloadMapsProgressLabel = $"Downloading {p.done}/{p.total}…");
            var result = await _hostMapDownloadService.DownloadAllAsync(
                _allPlugins, selectedModels, ApiToken.Trim(), progress, CancellationToken.None);

            MapsDownloadedCount = _allPlugins.Count(p => p.MapAvailability == MapAvailability.Downloaded);
            MapsMissingCount = _allPlugins.Count(p => p.MapAvailability == MapAvailability.AvailableNotDownloaded);
            RecomputeCounts();

            StatusLog = result.Failed > 0
                ? $"Downloaded {result.Downloaded} of {result.Attempted} map(s); {result.Failed} failed " +
                  "(check your API token and network connection)."
                : $"Downloaded {result.Downloaded} map(s) — RA Control will use them next time it loads each plugin.";
        }
        finally
        {
            IsDownloadingMaps = false;
            DownloadMapsProgressLabel = null;
        }
    }

    // ------------------------------------------------------------------
    // Settings (RA Control device selection)
    // ------------------------------------------------------------------

    [RelayCommand]
    private void OpenSettings()
    {
        var settings = _settingsStore.Load();
        var selected = new HashSet<string>(settings.SelectedControllerModels, StringComparer.OrdinalIgnoreCase);
        var known = _mapAvailabilityService.GetKnownControllerModels();
        ApiToken = settings.ApiToken;

        HasKnownControllerModels = known.Count > 0;
        ControllerModelOptions.Clear();
        foreach (var model in known)
        {
            var option = new ControllerModelOption(model, selected.Contains(model));
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ControllerModelOption.IsSelected)) SaveControllerModelSelection();
            };
            ControllerModelOptions.Add(option);
        }

        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        var settings = _settingsStore.Load();
        settings.ApiToken = string.IsNullOrWhiteSpace(ApiToken) ? null : ApiToken.Trim();
        _settingsStore.Save(settings);
        IsSettingsOpen = false;
    }

    private void SaveControllerModelSelection()
    {
        var settings = _settingsStore.Load();
        settings.SelectedControllerModels = ControllerModelOptions.Where(o => o.IsSelected).Select(o => o.Name).ToList();
        _settingsStore.Save(settings);
    }

    // ------------------------------------------------------------------
    // Removal history panel
    // ------------------------------------------------------------------

    [RelayCommand]
    private void OpenHistory()
    {
        RefreshHistoryPanel();
        IsHistoryOpen = true;
    }

    [RelayCommand]
    private void CloseHistory() => IsHistoryOpen = false;

    [RelayCommand]
    private void RestoreHistoryItem(HistoryEntry? entry)
    {
        if (entry is null) return;

        var existing = _allPlugins.FirstOrDefault(p => p.Key == entry.Key);
        if (existing is not null)
        {
            // Already back in the list (e.g. RA's scanner re-added it since removal).
            existing.IsChecked = false;
            existing.FromHistory = false;
        }
        else if (_document is null)
        {
            StatusLog = "Open a HostMode.props file before restoring a plugin.";
            return;
        }
        else
        {
            var restored = _manifestService.TryRestoreToDocument(_document, entry.ElementXml);
            if (restored is null)
            {
                StatusLog = $"Can't restore \"{entry.Name}\" — no saved XML for it " +
                            "(it was removed before this version added restore support).";
                return;
            }

            WireCountRecompute(restored);
            _allPlugins.Add(restored);
            if (RemovedThisSessionCount > 0) RemovedThisSessionCount--;
        }

        _historyStore.Forget(entry.Key);
        ApplyFilter();
        RefreshHistoryPanel();
        StatusLog = $"Restored \"{entry.Name}\". Click \"Save changes\" to write it back to the file.";
    }

    [RelayCommand]
    private void ForgetHistoryItem(HistoryEntry? entry)
    {
        if (entry is null) return;
        _historyStore.Forget(entry.Key);

        var match = _allPlugins.FirstOrDefault(p => p.Key == entry.Key);
        if (match is not null)
        {
            match.IsChecked = false;
            match.FromHistory = false;
        }

        RefreshHistoryPanel();
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var confirmed = ConfirmAsync is null || await ConfirmAsync(
            "Clear removal history",
            "Clear the entire removal history? Previously-removed plugins will no longer be auto-selected.");
        if (!confirmed) return;

        _historyStore.Clear();
        foreach (var p in _allPlugins) p.FromHistory = false;
        RefreshHistoryPanel();
    }

    private void RefreshHistoryPanel()
    {
        var list = _historyStore.Load().OrderByDescending(h => h.RemovedAt).ToList();
        HistoryItems.Clear();
        foreach (var h in list) HistoryItems.Add(h);
    }

    public int HistoryCount => HistoryItems.Count;

    // ------------------------------------------------------------------
    // Sorting / filtering
    // ------------------------------------------------------------------

    [RelayCommand]
    private void SortBy(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (_sortKey == key) _sortAscending = !_sortAscending;
        else { _sortKey = key; _sortAscending = true; }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = SearchText.Trim();
        var fmt = SelectedFormat == AllFormatsLabel ? null : SelectedFormat;

        IEnumerable<PluginEntry> query = _allPlugins;
        if (fmt is not null)
            query = query.Where(p => p.Format == fmt);
        if (!string.IsNullOrEmpty(q))
        {
            query = query.Where(p =>
                p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Manufacturer.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Format.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Category.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        Func<PluginEntry, string> keySelector = _sortKey switch
        {
            "Manufacturer" => p => p.Manufacturer,
            "Format" => p => p.Format,
            "Category" => p => p.Category,
            "Version" => p => p.Version,
            "FilePath" => p => p.FilePath,
            _ => p => p.Name,
        };
        query = _sortAscending
            ? query.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase)
            : query.OrderByDescending(keySelector, StringComparer.OrdinalIgnoreCase);

        var results = query.ToList();
        FilteredPlugins.Clear();
        foreach (var p in results) FilteredPlugins.Add(p);

        RecomputeCounts(results.Count);
    }

    private void RecomputeCounts(int? shown = null)
    {
        TotalCount = _allPlugins.Count;
        ShownCount = shown ?? FilteredPlugins.Count;
        CheckedCount = _allPlugins.Count(p => p.IsChecked);
        SelectedForMapCount = _allPlugins.Count(p => p.IsSelectedForMap);
        OnPropertyChanged(nameof(HeaderCheckState));
    }
}
