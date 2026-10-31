using System.Numerics;

namespace Nokto.Core.Services;

public sealed record AcousticSignature(byte[] Data, int Milliseconds, int PeakCount);

/// <summary>
/// Huella local de PCM mono de 16 kHz. Análisis de picos adaptado de shazam-for-real
/// de Aleksey Martynov (ISC); ver ThirdPartyNotices/shazam-for-real.txt.
/// No contiene audio PCM ni ejecuta llamadas de red.
/// </summary>
public static class AcousticSignatureGenerator
{
    private const int Rate = 16000, Hop = 128, Window = 2048, Bins = Window / 2 + 1;
    private static readonly double[] Hann = Enumerable.Range(0, Window).Select(i => .5 - .5 * Math.Cos(2 * Math.PI * i / (Window - 1))).ToArray();
    private sealed record Peak(int Frame, float Magnitude, float Bin);

    public static AcousticSignature Create(float[] samples, CancellationToken cancellation = default)
    {
        if (samples.Length < Rate * 2 || samples.Length > Rate * 16) throw new InvalidDataException("Se necesitan entre 2 y 16 segundos de audio.");
        int count = samples.Length / Hop * Hop;
        var spectra = new List<float[]>();
        var fft = new Complex[Window];
        for (int end = Window; end <= count; end += Hop)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int i = 0; i < Window; i++)
            {
                float value = samples[end - Window + i];
                if (!float.IsFinite(value)) throw new InvalidDataException("El audio contiene muestras no válidas.");
                fft[i] = new Complex(value * Hann[i], 0);
            }
            Transform(fft);
            var stripe = new float[Bins];
            for (int bin = 0; bin < Bins; bin++) stripe[bin] = (float)(2 * (fft[bin].Real * fft[bin].Real + fft[bin].Imaginary * fft[bin].Imaginary));
            spectra.Add(stripe);
        }
        var bands = Enumerable.Range(0, 4).Select(_ => new List<Peak>()).ToArray();
        const float minPower = 1f / 512 / 512;
        float logMinimum = MathF.Log(minPower);
        float Log(int frame, int bin) => 18 * 1024 * (1 - MathF.Log(spectra[frame][bin]) / logMinimum);
        bool Maximum(int frame, int bin, int frameRadius, int binRadius)
        {
            float center = spectra[frame][bin];
            for (int f = frame - frameRadius; f <= frame + frameRadius; f++)
                for (int b = bin - binRadius; b <= bin + binRadius; b++)
                    if ((f != frame || b != bin) && spectra[f][b] >= center) return false;
            return true;
        }
        // Margen de 45 ventanas: evita comparar picos contra audio inexistente.
        for (int frame = 45; frame < spectra.Count - 45; frame++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int bin = 32; bin < 704; bin++)
            {
                if (spectra[frame][bin] < minPower || !Maximum(frame, bin, 45, 1) || !Maximum(frame, bin, 3, 10)) continue;
                float a = Log(frame, bin - 1), b = Log(frame, bin), c = Log(frame, bin + 1);
                float divisor = a - 2 * b + c;
                float interpolated = bin + (Math.Abs(divisor) < 1e-10 ? 0 : (a - c) / divisor / 2);
                float hz = interpolated * Rate / Window;
                int band = hz < 250 ? -1 : hz < 520 ? 0 : hz < 1450 ? 1 : hz < 3500 ? 2 : hz < 5500 ? 3 : -1;
                if (band >= 0 && float.IsFinite(interpolated) && float.IsFinite(b)) bands[band].Add(new(frame, b, interpolated));
            }
        }
        int cap = 12 + spectra.Count / 12;
        for (int i = 0; i < bands.Length; i++) bands[i] = bands[i].OrderByDescending(p => p.Magnitude).Take(cap).OrderBy(p => p.Frame).ToList();
        int peaks = bands.Sum(b => b.Count);
        return new(Encode(count, bands), count * 1000 / Rate, peaks);
    }

    // FFT radix 2, sin normalizar; solo se usa durante un reconocimiento explícito.
    private static void Transform(Complex[] values)
    {
        for (int i = 1, j = 0; i < values.Length; i++)
        {
            int bit = values.Length >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (values[i], values[j]) = (values[j], values[i]);
        }
        for (int width = 2; width <= values.Length; width <<= 1)
        {
            var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / width);
            for (int block = 0; block < values.Length; block += width)
            {
                var phase = Complex.One;
                for (int offset = 0; offset < width / 2; offset++)
                {
                    var even = values[block + offset]; var odd = phase * values[block + offset + width / 2];
                    values[block + offset] = even + odd; values[block + offset + width / 2] = even - odd;
                    phase *= step;
                }
            }
        }
    }

    private static byte[] Encode(int count, List<Peak>[] bands)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        // Formato de firma acústica (campos little endian y CRC32 IEEE).
        uint[] header = [0xCAFE2580, 0, 0, 0x94119C00, 0, 0, 0, 3u << 27, 0, 0, (uint)count, 0x007C0000, 0x40000000, 0];
        foreach (uint word in header) writer.Write(word);
        for (int index = 0; index < bands.Length; index++)
        {
            using var payload = new MemoryStream(); using var bandWriter = new BinaryWriter(payload);
            int previous = 0;
            foreach (var peak in bands[index])
            {
                if (peak.Frame - previous >= 100) { bandWriter.Write((byte)255); bandWriter.Write(peak.Frame); previous = peak.Frame; }
                bandWriter.Write((byte)(peak.Frame - previous));
                bandWriter.Write((ushort)Math.Clamp((int)Math.Round(peak.Magnitude), 0, ushort.MaxValue));
                bandWriter.Write((ushort)Math.Clamp((int)Math.Round(peak.Bin * 64), 0, ushort.MaxValue));
                previous = peak.Frame;
            }
            while (payload.Length % 4 != 0) bandWriter.Write((byte)0);
            writer.Write(0x60030040 + index); writer.Write((int)payload.Length); writer.Write(payload.ToArray());
        }
        int length = (int)stream.Length - 48;
        stream.Position = 8; writer.Write(length); stream.Position = 52; writer.Write(length);
        byte[] data = stream.ToArray();
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data.AsSpan(8))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), ~crc);
        return data;
    }
}
