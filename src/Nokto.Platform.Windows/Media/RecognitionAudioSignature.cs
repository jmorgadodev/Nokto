using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Nokto.Core.Services;

namespace Nokto.Platform.Windows.Media;

public static class RecognitionAudioSignature
{
    public static AcousticSignature Create(byte[] wav, CancellationToken cancellation = default)
    {
        if (wav.Length is <= 44 or > 8_000_000) throw new InvalidDataException("Fragmento vacío o demasiado grande.");
        using var memory = new MemoryStream(wav, writable: false);
        using var reader = new WaveFileReader(memory);
        ISampleProvider source = reader.ToSampleProvider();
        if (source.WaveFormat.Channels > 1) source = new Mono(source);
        if (source.WaveFormat.SampleRate != 16000) source = new WdlResamplingSampleProvider(source, 16000);
        var samples = new List<float>(16000 * 8); var block = new float[4096]; int count;
        try
        {
            while ((count = source.Read(block, 0, block.Length)) > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                if (samples.Count + count > 16000 * 16) throw new InvalidDataException("El fragmento excede 16 segundos.");
                for (int i = 0; i < count; i++) samples.Add(block[i]);
            }
            var data = samples.ToArray();
            try { return AcousticSignatureGenerator.Create(data, cancellation); }
            finally { Array.Clear(data); }
        }
        finally { Array.Clear(block); System.Runtime.InteropServices.CollectionsMarshal.AsSpan(samples).Clear(); }
    }
    private sealed class Mono : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly float[] _buffer;
        private readonly int _channels;
        public WaveFormat WaveFormat { get; }
        public Mono(ISampleProvider source)
        {
            _source = source; _channels = source.WaveFormat.Channels;
            if (_channels is < 1 or > 32) throw new InvalidDataException("Formato de canales no compatible.");
            _buffer = new float[_channels * 4096];
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }
        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(_buffer, 0, Math.Min(count, 4096) * _channels);
            for (int frame = 0; frame < read / _channels; frame++)
            {
                float sum = 0; for (int channel = 0; channel < _channels; channel++) sum += _buffer[frame * _channels + channel];
                buffer[offset + frame] = sum / _channels;
            }
            return read / _channels;
        }
    }
}
