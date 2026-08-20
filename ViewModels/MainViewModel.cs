using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

    private XDocument? _document;
    private List<PluginEntry> _allPlugins = new();

    /// <summary>Set by the view once a TopLevel is available, so commands here can show native file dialogs.</summary>
    public IStorageProvider? StorageProvider { get; set; }

    /// <summary>Confirmation hook the view wires up (Avalonia has no built-in MessageBox).</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    public MainViewModel()
    {
        RefreshHistoryPanel();
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
    private void CheckAllFiltered()
    {
        foreach (var p in FilteredPlugins) p.IsChecked = true;
    }

    [RelayCommand]
    private void UncheckAll()
    {
        foreach (var p in _allPlugins) p.IsChecked = false;
    }

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

    /// <summary>Cross-references every loaded plugin's uniqueId against RA Control's own
    /// local map cache (AvailableMaps.txt + Parameter Tables) — no network calls. Plugins
    /// with a map available but not yet downloaded are auto-selected via IsSelectedForMap,
    /// which is intentionally separate from IsChecked (removal) so the two can't collide.</summary>
    [RelayCommand]
    private void CheckMapAvailability()
    {
        if (_allPlugins.Count == 0) return;

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

    // ------------------------------------------------------------------
    // Settings (RA Control device selection)
    // ------------------------------------------------------------------

    [RelayCommand]
    private void OpenSettings()
    {
        var selected = new HashSet<string>(_settingsStore.Load().SelectedControllerModels, StringComparer.OrdinalIgnoreCase);
        var known = _mapAvailabilityService.GetKnownControllerModels();

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
    private void CloseSettings() => IsSettingsOpen = false;

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
    }
}
