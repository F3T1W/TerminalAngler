using LakeCompanion.Application;
using Microsoft.Extensions.Options;
using Whisper.net;
using Whisper.net.Ggml;

namespace LakeCompanion.Infrastructure;

/// <summary>Local Whisper.NET recognizer with opt-in first-use model download and a no-op local TTS boundary.</summary>
public sealed class SpeechService(
    IOptions<VoiceOptions> options,
    IOptions<CompanionSpeechOptions> companionSpeechOptions) : ISpeechRecognizer, ISpeechSynthesizer, IDisposable
{
    private readonly SemaphoreSlim factoryLock = new(1, 1);
    private WhisperFactory? factory;

    /// <summary>Raised when optional companion narration could not be started or completed.</summary>
    public event Action<string>? NarrationFailed;

    /// <summary>Raised when optional companion narration starts or finishes.</summary>
    public event Action<string>? NarrationStatusChanged;

    /// <inheritdoc />
    public async Task<string?> TranscribeAsync(Stream audio, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        var whisperFactory = await GetFactoryAsync(cancellationToken).ConfigureAwait(false);
        using var processor = whisperFactory.CreateBuilder().WithLanguage(options.Value.Language).Build();
        var fragments = new List<string>();
        await foreach (var segment in processor.ProcessAsync(audio, cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(segment.Text))
            {
                fragments.Add(segment.Text.Trim());
            }
        }

        var result = string.Join(' ', fragments);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    /// <inheritdoc />
    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var settings = companionSpeechOptions.Value;
        try
        {
            using var speechTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            speechTimeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.MaxDurationSeconds, 5, 120)));
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("/usr/bin/say")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("-r");
            process.StartInfo.ArgumentList.Add(Math.Clamp(settings.Rate, 90, 400).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(settings.Voice))
            {
                process.StartInfo.ArgumentList.Add("-v");
                process.StartInfo.ArgumentList.Add(settings.Voice);
            }

            process.StartInfo.ArgumentList.Add(text.Trim());
            process.Start();
            NarrationStatusChanged?.Invoke($"Озвучка Рована началась (процесс {process.Id}).");
            using var registration = speechTimeout.Token.Register(() => TryStop(process));
            await process.WaitForExitAsync(speechTimeout.Token).ConfigureAwait(false);
            NarrationStatusChanged?.Invoke(process.ExitCode == 0
                ? "Озвучка Рована завершена."
                : $"Озвучка Рована завершилась с кодом {process.ExitCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            NarrationFailed?.Invoke("Время озвучки Рована истекло, процесс остановлен.");
            throw;
        }
        catch (Exception exception)
        {
            NarrationFailed?.Invoke($"Не удалось запустить озвучку Рована: {exception.Message}");
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        factory?.Dispose();
        factoryLock.Dispose();
    }

    private async Task<WhisperFactory> GetFactoryAsync(CancellationToken cancellationToken)
    {
        await factoryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (factory is not null)
            {
                return factory;
            }

            var modelPath = ResolveModelPath(options.Value.ModelPath);
            if (File.Exists(modelPath) && new FileInfo(modelPath).Length < 10_000_000)
            {
                File.Delete(modelPath);
            }
            if (!File.Exists(modelPath))
            {
                if (!options.Value.DownloadModelIfMissing)
                {
                    throw new FileNotFoundException("Whisper model is missing.", modelPath);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
                var temporaryPath = modelPath + ".tmp";
                try
                {
                    using var source = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Base, cancellationToken: cancellationToken).ConfigureAwait(false);
                    await using (var destination = File.Create(temporaryPath))
                    {
                        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                    }
                    File.Move(temporaryPath, modelPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }

            factory = WhisperFactory.FromPath(modelPath);
            return factory;
        }
        finally
        {
            factoryLock.Release();
        }
    }

    private static string ResolveModelPath(string? configuredPath) => string.IsNullOrWhiteSpace(configuredPath)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LakeCompanion", "models", "ggml-base.bin")
        : Path.GetFullPath(configuredPath);

    private static void TryStop(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The speech process exited between the check and the kill request.
        }
    }
}

/// <summary>Settings for opt-in local macOS narration of companion replies.</summary>
public sealed class CompanionSpeechOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "CompanionSpeech";
    /// <summary>Gets or sets whether narration starts enabled; the O key can change it for the current session.</summary>
    public bool Enabled { get; set; }
    /// <summary>Gets or sets an optional macOS <c>say</c> voice name; blank uses the system default voice.</summary>
    public string? Voice { get; set; }
    /// <summary>Gets or sets the speaking rate in words per minute.</summary>
    public int Rate { get; set; } = 180;
    /// <summary>Gets or sets the maximum time one companion reply may occupy the local voice engine.</summary>
    public int MaxDurationSeconds { get; set; } = 35;
}

/// <summary>Settings for offline Whisper recognition and bounded AVFoundation microphone capture.</summary>
public sealed class VoiceOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Voice";
    /// <summary>Gets or sets the preferred Whisper language code.</summary>
    public string Language { get; set; } = "ru";
    /// <summary>Gets or sets an optional absolute or working-directory-relative GGML model path.</summary>
    public string? ModelPath { get; set; }
    /// <summary>Gets or sets whether the base model may be downloaded on the first voice request.</summary>
    public bool DownloadModelIfMissing { get; set; } = true;
    /// <summary>Gets or sets the AVFoundation audio device expression passed to ffmpeg.</summary>
    public string AudioDevice { get; set; } = "none:2";
    /// <summary>Gets or sets the maximum duration of one push-to-talk capture.</summary>
    public int MaxCaptureSeconds { get; set; } = 8;
}
