using System.Buffers.Binary;
using NAudio.Wave;

namespace Nokto.Platform.Windows.Media;

public static class RecognitionAudioLevel
{
    private static readonly Guid FloatFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid PcmFormat = new("00000001-0000-0010-8000-00aa00389b71");
    /// <summary>RMS acotado del buffer existente; sin conservar muestras ni hacer otra captura.</summary>
    public static float Measure(ReadOnlySpan<byte> audio, WaveFormat format)
    {
        bool floating = format.Encoding == WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible ext &&
            ext.SubFormat == FloatFormat;
        if (!floating && format.Encoding != WaveFormatEncoding.Pcm && !(format is WaveFormatExtensible pcm && pcm.SubFormat == PcmFormat)) return 0;
        int bytes = format.BitsPerSample / 8;
        if (bytes is < 1 or > 4 || floating && bytes != 4) return 0;
        int samples = audio.Length / bytes, step = Math.Max(1, (samples + 2047) / 2048), count = 0;
        double sum = 0;
        for (int i = 0; i < samples; i += step)
        {
            var sample = audio.Slice(i * bytes, bytes);
            double value = floating ? BinaryPrimitives.ReadSingleLittleEndian(sample) : bytes switch
            {
                1 => (sample[0] - 128) / 128d,
                2 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768d,
                3 => ((sample[0] | sample[1] << 8 | sample[2] << 16) << 8 >> 8) / 8388608d,
                4 => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648d,
                _ => 0
            };
            if (!double.IsFinite(value)) continue;
            value = Math.Clamp(value, -1, 1); sum += value * value; count++;
        }
        return count > 0 ? (float)Math.Sqrt(sum / count) : 0;
    }
}
