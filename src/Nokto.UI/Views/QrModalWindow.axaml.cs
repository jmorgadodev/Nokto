using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Nokto.UI.Views;

public partial class QrModalWindow : Window
{
    public QrModalWindow()
    {
        InitializeComponent();
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (Clipboard != null)
        {
            await Clipboard.SetTextAsync(TxtUrl.Text ?? "http://localhost:4884");
            BtnCopy.Content = "✓ ¡Enlace Copiado!";
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
