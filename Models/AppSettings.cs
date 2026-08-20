using System.Collections.Generic;

namespace RAPluginManifestEditor.Models;

public class AppSettings
{
    public string? LastFilePath { get; set; }

    /// <summary>RA Control hardware controller model(s) the user owns (e.g. "Micro 3",
    /// "Mix S") — AvailableMaps.txt lists downloadable maps per controller model, so
    /// this scopes "map available" checks to hardware the user actually has.</summary>
    public List<string> SelectedControllerModels { get; set; } = new();
}
