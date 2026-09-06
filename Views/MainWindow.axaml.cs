using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RAPluginManifestEditor.ViewModels;

namespace RAPluginManifestEditor.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnSixWallsClick(object? sender, RoutedEventArgs e)
    {
        // The maker's mark in the footer opens the Six Walls site.
        _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new Uri("https://www.sixwalls.net"));
    }

    private async void OnOpened(object? sender, EventArgs? e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.StorageProvider = StorageProvider;
        vm.ConfirmAsync = (title, message) => ConfirmDialog.ShowAsync(this, title, message);

        await vm.TryAutoResumeAsync();
    }
}
