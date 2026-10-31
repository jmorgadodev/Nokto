using NAudio.Wave;
using Nokto.Core.Services;
using Nokto.Platform.Windows.Media;

internal static class RecognitionAudioProbe
{
    public static void Run()
    {
        foreach (bool floating in new[] { false, true })
        {
            using var memory = new MemoryStream();
            using var writer = new WaveFileWriter(memory, floating ? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2) : new WaveFormat(48000, 16, 2));
            var data = new float[48000 * 8 * 2];
            for (int i = 0; i < data.Length / 2; i++)
            {
                double position = i / 48000d;
                double phase = 2 * Math.PI * (400 * position + 80 * position * position);
                data[i * 2] = (float)(.4 * Math.Sin(phase));
                data[i * 2 + 1] = (float)(.3 * Math.Sin(phase * 1.73));
            }
            writer.WriteSamples(data, 0, data.Length); writer.Flush();
            var signature = RecognitionAudioSignature.Create(memory.ToArray());
            if (Math.Abs(signature.Milliseconds - 8000) > 20 || signature.PeakCount < 10) throw new Exception("Normalización WAV incorrecta");
            Console.WriteLine($"[PASS] WAV {(floating ? "float32" : "PCM16")} estéreo 48 kHz → huella mono 16 kHz, sin hardware");
        }
    }
    public static async Task ReferenceAsync(string directory, bool online)
    {
        // Solo el fichero público indicado explícitamente por el desarrollador.
        using var reader = new MediaFoundationReader(Path.Combine(directory, "test.mp3"));
        using var memory = new MemoryStream();
        WaveFileWriter.WriteWavFileToStream(memory, reader);
        var signature = RecognitionAudioSignature.Create(memory.ToArray());
        var expected = Parse(File.ReadAllBytes(Path.Combine(directory, "test-sigx-10.1.3.bin")));
        var actual = Parse(signature.Data);
        double best = 0, nearby = 0; int alignment = 0;
        for (int shift = -32; shift <= 32; shift++)
        {
            int hits = actual.Count(p => expected.Any(q => q.Band == p.Band && Math.Abs(p.Bin - q.Bin) < 1 && q.Frame == p.Frame + shift));
            double ratio = (double)hits / Math.Max(1, actual.Count);
            if (ratio > best) { best = ratio; alignment = shift; }
            double close = (double)actual.Count(p => expected.Any(q => q.Band == p.Band && Math.Abs(p.Bin - q.Bin) < 1 && Math.Abs(q.Frame - p.Frame - shift) <= 1)) / Math.Max(1, actual.Count);
            nearby = Math.Max(nearby, close);
        }
        Console.WriteLine($"Referencia pública: {signature.Milliseconds} ms, {actual.Count} picos, coincidencia de picos {best:P1}, alineación {alignment} ventanas");
        Console.WriteLine($"Diagnóstico: decodificador {reader.WaveFormat}; referencia {expected.Count} picos; tolerancia de una ventana de 8 ms: {nearby:P1}");
        // La referencia usa NLayer con padding; Media Foundation recorta el retardo MP3.
        // Comparar picos con ±8 ms absorbe la cuantización a ventanas de esa diferencia.
        if (nearby < .9) throw new Exception("La huella no coincide suficientemente con la referencia independiente.");
        if (!online) return;
        reader.Position = 0;
        using var eightSeconds = new MemoryStream();
        var excerpt = new NAudio.Wave.SampleProviders.OffsetSampleProvider(reader.ToSampleProvider()) { Take = TimeSpan.FromSeconds(8) };
        WaveFileWriter.WriteWavFileToStream(eightSeconds, excerpt.ToWaveProvider16());
        var shortSignature = RecognitionAudioSignature.Create(eightSeconds.ToArray());
        using var service = new ShazamRecognitionService();
        var song = await service.RecognizeAsync(shortSignature);
        if (song is null) throw new Exception("La muestra pública no produjo coincidencia en el catálogo.");
        Console.WriteLine($"[PASS] Muestra pública de {shortSignature.Milliseconds} ms identificada sin clave: {song.Title} · {song.Artist}");
    }
    private sealed record Point(int Band, int Frame, float Bin);
    private static List<Point> Parse(byte[] data)
    {
        var points = new List<Point>(); using var stream = new MemoryStream(data); using var reader = new BinaryReader(stream);
        stream.Position = 56;
        while (stream.Position < stream.Length)
        {
            int band = reader.ReadInt32() - 0x60030040; int length = reader.ReadInt32(); long end = stream.Position + length; int frame = 0;
            while (end - stream.Position >= 5)
            {
                int delta = reader.ReadByte();
                if (delta == 255) { frame = reader.ReadInt32(); delta = reader.ReadByte(); }
                frame += delta; reader.ReadUInt16(); float bin = reader.ReadUInt16() / 64f;
                points.Add(new(band, frame, bin));
            }
            stream.Position = end + ((4 - length % 4) % 4);
        }
        return points;
    }
}
