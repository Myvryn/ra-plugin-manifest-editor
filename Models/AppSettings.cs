using System.Collections.Generic;

namespace RAPluginManifestEditor.Models;

public class AppSettings
{
    public string? LastFilePath { get; set; }

    /// <summary>RA Control hardware controller model(s) the user owns (e.g. "Micro 3",
    /// "Mix S") — AvailableMaps.txt lists downloadable maps per controller model, so
    /// this scopes "map available" checks to hardware the user actually has.</summary>
    public List<string> SelectedControllerModels { get; set; } = new();

    /// <summary>Optional bring-your-own API token for RA Control's backend
    /// (ra-control-api.rocksolidaudioinfo-326.workers.dev), enabling a live "Check for
    /// missing maps (live)" pass instead of relying only on RA Control's local cache.
    /// Never shipped with the app and never sent anywhere except that one host — see
    /// LiveMapAvailabilityService. Stored locally only, like every other setting here.</summary>
    public string? ApiToken { get; set; }
}
