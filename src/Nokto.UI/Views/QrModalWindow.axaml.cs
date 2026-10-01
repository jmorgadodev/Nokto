using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Nokto.LanServer;

namespace Nokto.UI.Views;

public partial class QrModalWindow : Window
{
    private string _currentUrl = "http://localhost:4884";

    public QrModalWindow()
    {
        InitializeComponent();
    }

    public void SetConnectionDetails(string url)
    {
        _currentUrl = url;
        TxtUrl.Text = url;

        try
        {
            byte[] pngBytes = QrCodeService.GeneratePngBytes(url, pixelsPerModule: 8);
            using var ms = new MemoryStream(pngBytes);
            ImgQr.Source = new Bitmap(ms);
        }
        catch
        {
            // Silently handle QR rendering fallback
        }
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (Clipboard != null)
        {
            await Clipboard.SetTextAsync(_currentUrl);
            BtnCopy.Content = "✓ ¡Enlace Copiado al Portapapeles!";
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
