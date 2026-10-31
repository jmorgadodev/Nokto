using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Data;
using Nokto.Core.Services;
using Nokto.Platform.Windows.Media;
using System.Security.Cryptography;
using Nokto.UI.Controls;

namespace Nokto.UI.Views;

/// <summary>Panel de la isla. Mostrarlo nunca inicia una captura ni consulta credenciales.</summary>
public sealed class SongRecognitionView : UserControl, IDisposable
{
    public event Action? DismissRequested;
    public event Action? ContentChanged;
    private readonly RecognitionSignal _signal = new();
    private readonly TextBlock _disclosure = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brush.Parse("#A9C2CF") };
    private readonly IslandButton _microphone = new() { Content = "Micrófono", Appearance = "Selected", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly IslandButton _computer = new() { Content = "Audio del PC", HorizontalAlignment = HorizontalAlignment.Stretch };
    private bool _loopback;
    private readonly IslandButton _recognize = new() { Appearance = "Primary", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly IslandButton _cancel = new() { Content = "Cancelar", IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 12) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#B4CFDA"), FontSize = 12, IsVisible = false };
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#E1FFF4"), FontSize = 17, FontWeight = FontWeight.SemiBold, IsVisible = false };
    private CancellationTokenSource? _request;
    private bool _closed;
    public SongRecognitionView()
    {
        _signal.Bind(RecognitionSignal.ReducedMotionProperty, new Binding("IslandVm.ReducedMotion"));
        _recognize.Padding = new Thickness(10, 12);
        var content = new StackPanel { Spacing = 12 };
        var back = new IslandButton { Content = "‹  Volver al módulo", Appearance = "Quiet", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 6) };
        back.Click += (_, _) => DismissRequested?.Invoke(); content.Children.Add(back);
        content.Children.Add(new TextBlock { Text = "¿Qué está sonando?", Foreground = Brush.Parse("#EDF9F6"), FontSize = 17, FontWeight = FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = "Elige dónde escuchar", Foreground = Brush.Parse("#BFDBE3"), FontSize = 12, Margin = new Thickness(0, 4, 0, -4) });
        var sources = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,*") };
        _microphone.Margin = new Thickness(0, 0, 4, 0); _computer.Margin = new Thickness(4, 0, 0, 0);
        sources.Children.Add(_microphone); Grid.SetColumn(_computer, 1); sources.Children.Add(_computer); content.Children.Add(sources);
        _microphone.Click += (_, _) => SelectSource(false); _computer.Click += (_, _) => SelectSource(true);
        content.Children.Add(_signal);
        content.Children.Add(_status); content.Children.Add(_result); content.Children.Add(_disclosure);
        content.Children.Add(_recognize); content.Children.Add(_cancel);
        Content = new Border { Background = Brush.Parse("#17212C"), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Child = content };
        RefreshSource();
        _recognize.Click += Recognize;
        _cancel.Click += (_, _) => _request?.Cancel();
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape) { DismissRequested?.Invoke(); e.Handled = true; } };
    }

    private void SelectSource(bool loopback)
    {
        _loopback = loopback;
        _microphone.Appearance = loopback ? "Secondary" : "Selected";
        _computer.Appearance = loopback ? "Selected" : "Secondary";
        _signal.Phase = "Idle"; _result.Text = ""; _result.IsVisible = false;
        RefreshSource();
    }

    private void SetStatus(string text)
    {
        _status.Text = text; _status.IsVisible = !string.IsNullOrEmpty(text); ContentChanged?.Invoke();
    }

    public void Dispose()
    {
        _closed = true; _signal.Dispose(); _request?.Cancel();
    }

    private void RefreshSource()
    {
        _recognize.Content = "Reconocer canción";
        _disclosure.Text = "Al pulsar, se escuchan 8 segundos. Solo una huella acústica se envía por Internet para identificar la canción; el audio no se sube ni se guarda.";
        _disclosure.Text += _loopback ? "\n\nSe escucha toda la salida predeterminada del PC." : "\n\nSe utiliza tu micrófono para escuchar el entorno.";
        SetStatus("");
    }

    private void SetPhase(string phase, string message)
    {
        _signal.Phase = phase;
        Avalonia.Automation.AutomationProperties.SetName(_signal, message);
        SetStatus(message);
    }
    private void SetBusy(bool busy)
    {
        _microphone.IsEnabled = _computer.IsEnabled = !busy;
        _recognize.IsVisible = !busy; _cancel.IsVisible = busy; _disclosure.IsVisible = !busy;
        if (busy) { _result.Text = ""; _result.IsVisible = false; }
        ContentChanged?.Invoke();
    }

    private async void Recognize(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (_request is not null) return;
        var request = new CancellationTokenSource(); _request = request;
        SetBusy(true);
        SetPhase("Listening", "Escuchando la música…");
        byte[]? audio = null;
        try
        {
            audio = await SongRecognitionCapture.CaptureAsync(_loopback, request.Token, _signal.SetLevel);
            if (_closed) return;
            SetPhase("Searching", "Analizando la música…");
            var signature = await Task.Run(() => RecognitionAudioSignature.Create(audio, request.Token), request.Token);
            if (_closed) return;
            SetStatus("Buscando coincidencia…");
            using var service = new ShazamRecognitionService();
            var song = await service.RecognizeAsync(signature, request.Token);
            if (_closed) return;
            _result.Text = song is null ? "No se encontró una coincidencia" : $"{song.Title}\n{song.Artist}";
            _result.IsVisible = true;
            SetPhase(song is null ? "NoMatch" : "Success", song is null ? "Prueba con un fragmento más claro." : "Canción identificada");
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { if (!_closed) SetPhase("Idle", "Cancelado. El fragmento se ha descartado."); }
        catch (OperationCanceledException) { if (!_closed) SetPhase("Error", "La consulta tardó demasiado. Comprueba la conexión y vuelve a intentarlo."); }
        catch (Exception ex)
        {
            if (!_closed) SetPhase("Error", ex is HttpRequestException ? "No se pudo consultar el servicio. Comprueba tu conexión o inténtalo más tarde." :
                ex is InvalidOperationException or InvalidDataException ? ex.Message : "No se pudo capturar el audio. Revisa la fuente y los permisos de Windows.");
        }
        finally
        {
            if (audio is not null) CryptographicOperations.ZeroMemory(audio);
            request.Dispose(); _request = null;
            if (!_closed) SetBusy(false);
        }
    }
}
