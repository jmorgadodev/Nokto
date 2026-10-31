using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace Nokto.Platform.Windows.Media;

public static class SongRecognitionCapture
{
    /// <summary>Solo se invoca tras pulsación y confirmación. WAV en memoria, sin archivo ni escucha persistente.</summary>
    public static Task<byte[]> CaptureAsync(bool systemOutput, CancellationToken cancellation, Action<float>? onLevel = null) => Task.Run(async () =>
    {
        using WasapiCapture capture = systemOutput ? new WasapiLoopbackCapture() : new WasapiCapture();
        using var memory = new MemoryStream();
        using var writer = new WaveFileWriter(memory, capture.WaveFormat);
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? error = null;
        capture.DataAvailable += (_, args) =>
        {
            if (error is not null) return;
            try
            {
                if (memory.Length + args.BytesRecorded > 8_000_000) throw new InvalidDataException("El formato de audio supera el límite del fragmento.");
                writer.Write(args.Buffer, 0, args.BytesRecorded);
                onLevel?.Invoke(RecognitionAudioLevel.Measure(args.Buffer.AsSpan(0, args.BytesRecorded), capture.WaveFormat));
            }
            catch (Exception ex) { error = ex; }
        };
        capture.RecordingStopped += (_, args) => stopped.TrySetResult(args.Exception);
        capture.StartRecording();
        try { await Task.Delay(TimeSpan.FromSeconds(8), cancellation); }
        finally
        {
            capture.StopRecording();
            var stopError = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            error ??= stopError;
        }
        cancellation.ThrowIfCancellationRequested();
        if (error is not null) throw error;
        writer.Flush();
        if (writer.Length == 0) throw new InvalidOperationException("No se recibió audio. Comprueba la fuente y los permisos de Windows.");
        return memory.ToArray();
    }, cancellation);
}
