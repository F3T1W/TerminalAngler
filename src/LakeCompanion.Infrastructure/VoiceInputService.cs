using System.Buffers.Binary;
using System.Diagnostics;
using LakeCompanion.Application;
using Microsoft.Extensions.Options;

namespace LakeCompanion.Infrastructure;

/// <summary>Uses local ffmpeg AVFoundation capture to produce a temporary WAV for offline Whisper recognition.</summary>
public sealed class VoiceInputService(IOptions<VoiceOptions> options, ISpeechRecognizer recognizer) : IVoiceInputService
{
    /// <inheritdoc />
    public event Action<string>? StatusChanged;

    /// <inheritdoc />
    public async Task<VoiceRecognitionResult> CaptureAndTranscribeAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return new VoiceRecognitionResult(null, "Голосовой ввод сейчас доступен только на macOS.");
        }

        var path = Path.Combine(Path.GetTempPath(), $"lake-companion-{Guid.NewGuid():N}.wav");
        using var captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            StatusChanged?.Invoke("Идёт запись с выбранного микрофона...");
            var captureTask = Task.Run(() => CaptureWave(path, captureCancellation.Token), CancellationToken.None);
            var deadline = TimeSpan.FromSeconds(Math.Clamp(options.Value.MaxCaptureSeconds, 1, 20) + 5);
            if (await Task.WhenAny(captureTask, Task.Delay(deadline, cancellationToken)).ConfigureAwait(false) != captureTask)
            {
                captureCancellation.Cancel();
                _ = captureTask.ContinueWith(
                    _ => TryDelete(path),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
                return new VoiceRecognitionResult(null, $"Запись с микрофона не завершилась за {deadline.TotalSeconds:0} с. Игра продолжит работу; проверь выбранное устройство и разрешение на микрофон в macOS.");
            }

            var completedCapture = await captureTask.ConfigureAwait(false);
            if (!completedCapture.Succeeded || !File.Exists(path))
            {
                return new VoiceRecognitionResult(null, $"Не удалось записать звук с микрофона. {completedCapture.Error}");
            }

            var signal = MeasureSignal(path);
            if (signal.SampleCount == 0)
            {
                return new VoiceRecognitionResult(null, "Микрофон создал пустой аудиофайл. Проверь выбранное устройство и его разрешение в macOS.");
            }

            StatusChanged?.Invoke($"Запись завершена. Сигнал: пик {signal.PeakDecibels:0.0} dBFS, средний {signal.RmsDecibels:0.0} dBFS.");
            if (signal.PeakDecibels < -55)
            {
                return new VoiceRecognitionResult(null, $"Полезный сигнал с микрофона не обнаружен (пик {signal.PeakDecibels:0.0} dBFS). Выбери микрофон в настройках звука macOS и разреши доступ Терминалу или Rider.");
            }

            StatusChanged?.Invoke("Запись завершена. Идёт локальное распознавание; при первом запуске может загрузиться модель Whisper...");
            await using var recording = File.OpenRead(path);
            using var transcriptionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            transcriptionTimeout.CancelAfter(TimeSpan.FromMinutes(2));
            var transcript = await recognizer.TranscribeAsync(recording, transcriptionTimeout.Token).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(transcript)
                ? new VoiceRecognitionResult(null, $"Звук записан (пик {signal.PeakDecibels:0.0} dBFS), но речь не распознана. Говори ближе к микрофону или выбери другое Voice:AudioDevice.")
                : new VoiceRecognitionResult(transcript, null);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private CaptureResult CaptureWave(string path, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CreateCaptureStartInfo(path) };
        if (!process.Start())
        {
            return new CaptureResult(false, "Не удалось запустить ffmpeg.");
        }

        using var registration = cancellationToken.Register(() => TryStop(process));
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        process.WaitForExit();
        var error = errorTask.GetAwaiter().GetResult().Trim();
        return process.ExitCode == 0
            ? new CaptureResult(true, string.Empty)
            : new CaptureResult(false, string.IsNullOrWhiteSpace(error) ? $"ffmpeg завершился с кодом {process.ExitCode}." : $"ffmpeg: {error}");
    }

    private ProcessStartInfo CreateCaptureStartInfo(string path)
    {
        var info = new ProcessStartInfo("ffmpeg")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("-nostdin"); info.ArgumentList.Add("-loglevel"); info.ArgumentList.Add("error");
        info.ArgumentList.Add("-f"); info.ArgumentList.Add("avfoundation");
        info.ArgumentList.Add("-i"); info.ArgumentList.Add(options.Value.AudioDevice);
        info.ArgumentList.Add("-t"); info.ArgumentList.Add(Math.Clamp(options.Value.MaxCaptureSeconds, 1, 20).ToString(System.Globalization.CultureInfo.InvariantCulture));
        info.ArgumentList.Add("-ar"); info.ArgumentList.Add("16000"); info.ArgumentList.Add("-ac"); info.ArgumentList.Add("1");
        info.ArgumentList.Add("-c:a"); info.ArgumentList.Add("pcm_s16le"); info.ArgumentList.Add("-y"); info.ArgumentList.Add(path);
        return info;
    }

    private static void TryStop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static AudioSignal MeasureSignal(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var dataOffset = FindWaveDataOffset(bytes, out var dataLength);
        if (dataOffset < 0 || dataLength < 2)
        {
            return default;
        }

        var sampleCount = dataLength / 2;
        var peak = 0d;
        var sumSquares = 0d;
        for (var offset = dataOffset; offset < dataOffset + sampleCount * 2; offset += 2)
        {
            var normalized = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset, 2)) / 32768d;
            var absolute = Math.Abs(normalized);
            peak = Math.Max(peak, absolute);
            sumSquares += normalized * normalized;
        }

        return new AudioSignal(sampleCount, ToDecibels(peak), ToDecibels(Math.Sqrt(sumSquares / sampleCount)));
    }

    private static int FindWaveDataOffset(byte[] bytes, out int dataLength)
    {
        dataLength = 0;
        for (var offset = 12; offset + 8 <= bytes.Length;)
        {
            var chunkLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            if (chunkLength < 0 || offset + 8L + chunkLength > bytes.Length)
            {
                return -1;
            }

            if (bytes.AsSpan(offset, 4).SequenceEqual("data"u8))
            {
                dataLength = chunkLength;
                return offset + 8;
            }

            offset += 8 + chunkLength + (chunkLength & 1);
        }

        return -1;
    }

    private static double ToDecibels(double value) => 20 * Math.Log10(Math.Max(value, 0.0000001));

    private readonly record struct CaptureResult(bool Succeeded, string Error);
    private readonly record struct AudioSignal(int SampleCount, double PeakDecibels, double RmsDecibels);
}
