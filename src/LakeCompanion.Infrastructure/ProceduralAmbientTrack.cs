using System.Buffers.Binary;

namespace LakeCompanion.Infrastructure;

/// <summary>Creates a small local WAV soundscape so the starter can provide ambience without bundling third-party audio.</summary>
public static class ProceduralAmbientTrack
{
    private const int SampleRate = 22_050;
    private const int DurationSeconds = 24;

    /// <summary>Returns a cached generated track, creating it atomically on first use.</summary>
    public static Task<string> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LakeCompanion", "audio");
        Directory.CreateDirectory(directory);
        var trackPath = Path.Combine(directory, "procedural-lake-ambience.wav");
        if (File.Exists(trackPath))
        {
            return Task.FromResult(trackPath);
        }

        var temporaryPath = trackPath + ".tmp";
        WriteWave(temporaryPath, cancellationToken);
        File.Move(temporaryPath, trackPath, overwrite: true);
        return Task.FromResult(trackPath);
    }

    private static void WriteWave(string path, CancellationToken cancellationToken)
    {
        var sampleCount = SampleRate * DurationSeconds;
        const short channels = 1;
        const short bitsPerSample = 16;
        var dataLength = sampleCount * sizeof(short);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);

        var filteredWater = 0d;
        var random = new Random(2_024);
        Span<byte> sampleBuffer = stackalloc byte[sizeof(short)];
        for (var index = 0; index < sampleCount; index++)
        {
            if (index % 1_024 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var time = (double)index / SampleRate;
            var slowSwell = 0.72d + (0.28d * Math.Sin(time * 0.17d));
            var pad = (Math.Sin(2d * Math.PI * 146.83d * time) * 0.025d)
                + (Math.Sin(2d * Math.PI * 220d * time) * 0.017d)
                + (Math.Sin(2d * Math.PI * 293.66d * time) * 0.009d);
            filteredWater = (filteredWater * 0.985d) + (((random.NextDouble() * 2d) - 1d) * 0.015d);
            var value = Math.Clamp((pad * slowSwell) + filteredWater, -0.12d, 0.12d);
            BinaryPrimitives.WriteInt16LittleEndian(sampleBuffer, (short)(value * short.MaxValue));
            stream.Write(sampleBuffer);
        }
    }
}
