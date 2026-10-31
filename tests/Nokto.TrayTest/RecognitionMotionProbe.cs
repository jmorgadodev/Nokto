using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nokto.Platform.Windows.Media;
using Nokto.UI.Controls;
using Nokto.UI.Views;
using NAudio.Wave;

internal static class RecognitionMotionProbe
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("[PASS] " + message); }
    public static void Run()
    {
        byte[] pcm = new byte[8192];
        for (int i = 0; i < pcm.Length; i += 2) System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i, 2), 16384);
        Check(Math.Abs(RecognitionAudioLevel.Measure(pcm, new WaveFormat(48000, 16, 2)) - .5) < .001, "La onda recibe el nivel RMS del buffer PCM existente, sin capturar hardware");
        Array.Clear(pcm);
        Check(RecognitionAudioLevel.Measure(pcm, new WaveFormat(48000, 16, 2)) == 0, "Silencio produce nivel cero, no una onda de música inventada");
        byte[] floating = new byte[16];
        BitConverter.GetBytes(float.NaN).CopyTo(floating, 0); BitConverter.GetBytes(float.PositiveInfinity).CopyTo(floating, 4);
        Check(float.IsFinite(RecognitionAudioLevel.Measure(floating, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1))), "El medidor descarta muestras no finitas");

        var view = new SongRecognitionView();
        var signal = (RecognitionSignal)typeof(SongRecognitionView).GetField("_signal", Flags)!.GetValue(view)!;
        var host = new Window { Content = view, Width = 308, Height = 530, Position = new PixelPoint(-5000, -5000), ShowActivated = false, ShowInTaskbar = false };
        host.Show(); Dispatcher.UIThread.RunJobs();
        var frames = (DispatcherTimer)typeof(RecognitionSignal).GetField("_frames", Flags)!.GetValue(signal)!;
        Check(!frames.IsEnabled, "Reconocer no mantiene un temporizador de animación en reposo");
        void Phase(string phase, string message) => typeof(SongRecognitionView).GetMethod("SetPhase", Flags)!.Invoke(view, [phase, message]);
        typeof(SongRecognitionView).GetMethod("SetBusy", Flags)!.Invoke(view, [true]);
        Phase("Listening", "Escuchando la música…");
        Check(frames.IsEnabled, "Escuchar activa la animación solo durante la operación");
        frames.Stop(); // Congelar únicamente la escena sintética para la captura de QA.
        for (int i = 0; i < 25; i++)
        {
            signal.SetLevel((float)(.05 + .45 * Math.Pow(Math.Sin(i * .4), 2)));
            typeof(RecognitionSignal).GetMethod("SampleFrame", Flags)!.Invoke(signal, [1 + i / 30d]);
        }
        Capture(view, "qa-recognition-listening.png");
        Phase("Searching", "Buscando coincidencia…");
        frames.Stop();
        typeof(RecognitionSignal).GetMethod("SampleFrame", Flags)!.Invoke(signal, [.9]);
        Capture(view, "qa-recognition-searching.png");
        frames.Start(); signal.ReducedMotion = true;
        Check(!frames.IsEnabled, "Reducir movimiento detiene la onda y el barrido");
        signal.ReducedMotion = false; Phase("Success", "Canción identificada");
        Check(!frames.IsEnabled, "Resultado detiene la animación continua");
        Phase("Listening", "Escuchando la música…"); view.Dispose();
        Check(!frames.IsEnabled && typeof(SongRecognitionView).GetField("_request", Flags)!.GetValue(view) is null,
            "Cerrar detiene los fotogramas y la prueba nunca inició una captura");
        host.Close();
    }
    private static void Capture(Control view, string name)
    {
        view.Measure(new Size(308, double.PositiveInfinity));
        double height = view.DesiredSize.Height;
        view.Arrange(new Rect(0, 0, 308, height)); Dispatcher.UIThread.RunJobs();
        using var bitmap = new RenderTargetBitmap(new PixelSize(308, (int)Math.Ceiling(height)), new Vector(96, 96));
        bitmap.Render(view); bitmap.Save(Path.Combine(Environment.CurrentDirectory, "artifacts", "Capturas-pruebas", name));
    }
}
