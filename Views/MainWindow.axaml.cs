using System;
using Avalonia.Controls;
using RAPluginManifestEditor.ViewModels;

namespace RAPluginManifestEditor.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs? e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.StorageProvider = StorageProvider;
        vm.ConfirmAsync = (title, message) => ConfirmDialog.ShowAsync(this, title, message);

        await vm.TryAutoResumeAsync();
    }
}
