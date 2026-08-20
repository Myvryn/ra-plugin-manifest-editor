using CommunityToolkit.Mvvm.ComponentModel;

namespace RAPluginManifestEditor.Models;

/// <summary>One selectable RA Control hardware controller model in the Settings dialog,
/// sourced live from AvailableMaps.txt's top-level folder names.</summary>
public partial class ControllerModelOption : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public ControllerModelOption(string name, bool isSelected)
    {
        Name = name;
        IsSelected = isSelected;
    }
}
