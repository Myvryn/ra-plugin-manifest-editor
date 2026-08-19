using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RAPluginManifestEditor.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string title, string message) : this()
    {
        Title = title;
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("MessageText")!.Text = message;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    public static async Task<bool> ShowAsync(Window owner, string title, string message)
    {
        var dialog = new ConfirmDialog(title, message);
        return await dialog.ShowDialog<bool>(owner);
    }
}
