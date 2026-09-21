using System.Diagnostics;
using System.Runtime.InteropServices;
using LakeCompanion.Application;
using Microsoft.Extensions.Options;

namespace LakeCompanion.Infrastructure;

/// <summary>Configuration for optional local background ambience and a user-owned playlist folder.</summary>
public sealed class AmbientMusicOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "AmbientMusic";

    /// <summary>Gets or sets whether ambience starts with the game.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the folder containing local WAV, M4A, MP3, AAC, or AIFF tracks.</summary>
    public string TrackDirectory { get; set; } = "music";

    /// <summary>Gets or sets the bundled ambient fallback played only when the playlist has no custom tracks.</summary>
    public string AmbientFallbackPath { get; set; } = "music/ambient.mp3";

    /// <summary>Gets or sets an optional single-track fallback when the playlist folder is empty.</summary>
    public string? TrackPath { get; set; }

    /// <summary>Gets or sets the macOS player volume in the inclusive range zero through one.</summary>
    public double Volume { get; set; } = 0.18d;
}

/// <summary>Loops a local playlist through macOS <c>afplay</c>, releasing the audio device while muted and resuming from the saved track position.</summary>
public sealed partial class AmbientMusicService(IOptions<AmbientMusicOptions> options) : IAmbientMusicService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".m4a", ".mp3", ".aac", ".aiff", ".aif"
    };

    private readonly object sync = new();
    private CancellationTokenSource? playbackCancellation;
    private Process? activeProcess;
    private Task? playbackTask;
    private bool isPaused;
    private string currentTrackName = "";
    private string status = "Музыка выключена.";
    private IReadOnlyList<string>? playlist;
    private PausedPlayback? pausedPlayback;
    private int activeTrackIndex;
    private TimeSpan activeTrackOffset;
    private long activeTrackStartedAt;

    /// <inheritdoc />
    public bool IsPlaying
    {
        get
        {
            lock (sync)
            {
                return playbackTask is { IsCompleted: false };
            }
        }
    }

    /// <inheritdoc />
    public bool IsPaused
    {
        get
        {
            lock (sync)
            {
                return isPaused;
            }
        }
    }

    /// <inheritdoc />
    public string Status
    {
        get
        {
            lock (sync)
            {
                return status;
            }
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            SetStatus("Музыка отключена в настройках.");
            return;
        }

        if (!OperatingSystem.IsMacOS())
        {
            SetStatus("Фоновая музыка пока доступна только на macOS.");
            return;
        }

        lock (sync)
        {
            if (playbackTask is { IsCompleted: false })
            {
                return;
            }
        }

        var tracks = await ResolveTracksAsync(cancellationToken).ConfigureAwait(false);
        lock (sync)
        {
            isPaused = false;
            status = $"Музыкальный плейлист запущен: {tracks.Count} трек(а). M — поставить на паузу.";
            playlist = tracks;
            pausedPlayback = null;
            StartPlaybackLocked(tracks, 0, TimeSpan.Zero, cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task PauseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process? process;
        CancellationTokenSource? source;
        lock (sync)
        {
            if (isPaused || activeProcess is null || playlist is null)
            {
                return Task.CompletedTask;
            }

            process = activeProcess;
            var elapsed = activeTrackStartedAt == 0
                ? TimeSpan.Zero
                : Stopwatch.GetElapsedTime(activeTrackStartedAt);
            pausedPlayback = new PausedPlayback(playlist, activeTrackIndex, activeTrackOffset + elapsed);
            source = playbackCancellation;
            isPaused = true;
            status = $"Музыка на паузе: {currentTrackName}. M — продолжить.";
        }

        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Playback completed just as mute was requested.
        }
        TryStop(process);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? previousTask;
        PausedPlayback? paused;
        lock (sync)
        {
            if (!isPaused || pausedPlayback is null)
            {
                return;
            }

            paused = pausedPlayback;
            previousTask = playbackTask;
        }

        if (previousTask is not null)
        {
            await previousTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        lock (sync)
        {
            if (!isPaused || !ReferenceEquals(pausedPlayback, paused))
            {
                return;
            }

            isPaused = false;
            pausedPlayback = null;
            status = $"Музыка: {currentTrackName}. M — поставить на паузу.";
            StartPlaybackLocked(paused.Tracks, paused.TrackIndex, paused.Offset, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? task;
        Process? process;
        CancellationTokenSource? source;
        lock (sync)
        {
            task = playbackTask;
            process = activeProcess;
            source = playbackCancellation;
            isPaused = false;
            pausedPlayback = null;
            playlist = null;
            status = "Музыка выключена.";
        }

        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A concurrent shutdown already released this playback source.
        }
        TryStop(process);
        if (task is not null)
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> ResolveTracksAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = ResolvePath(options.Value.TrackDirectory, isDirectory: true);
        var ambientFallback = ResolvePath(options.Value.AmbientFallbackPath, isDirectory: false);
        if (directory is not null && Directory.Exists(directory))
        {
            var tracks = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                .Where(path => !PathsEqual(path, ambientFallback))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (tracks.Length > 0)
            {
                return tracks;
            }
        }

        if (ambientFallback is not null && File.Exists(ambientFallback) && SupportedExtensions.Contains(Path.GetExtension(ambientFallback)))
        {
            return [ambientFallback];
        }

        var singleTrack = ResolvePath(options.Value.TrackPath, isDirectory: false);
        if (singleTrack is not null && File.Exists(singleTrack) && SupportedExtensions.Contains(Path.GetExtension(singleTrack)))
        {
            return [singleTrack];
        }

        return [await ProceduralAmbientTrack.GetOrCreateAsync(cancellationToken).ConfigureAwait(false)];
    }

    private async Task PlayLoopAsync(IReadOnlyList<string> tracks, int startingTrackIndex, TimeSpan startingOffset, CancellationToken cancellationToken)
    {
        try
        {
            var index = startingTrackIndex;
            var offset = startingOffset;
            while (!cancellationToken.IsCancellationRequested)
            {
                var trackPath = tracks[index];
                var trackName = $"{Path.GetFileName(trackPath)} ({index + 1}/{tracks.Count})";
                lock (sync)
                {
                    currentTrackName = trackName;
                    status = $"Музыка: {trackName}. M — поставить на паузу.";
                }

                var resumeSegment = await CreateResumeSegmentAsync(trackPath, offset, cancellationToken).ConfigureAwait(false);
                using var process = CreatePlayer(resumeSegment ?? trackPath);
                cancellationToken.ThrowIfCancellationRequested();
                process.Start();
                using var registration = cancellationToken.Register(() => TryStop(process));
                lock (sync)
                {
                    activeProcess = process;
                    activeTrackIndex = index;
                    activeTrackOffset = offset;
                    activeTrackStartedAt = Stopwatch.GetTimestamp();
                }

                try
                {
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    lock (sync)
                    {
                        if (ReferenceEquals(activeProcess, process))
                        {
                            activeProcess = null;
                            activeTrackStartedAt = 0;
                        }
                    }
                    TryDelete(resumeSegment);
                }

                offset = TimeSpan.Zero;
                index = (index + 1) % tracks.Count;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the player mutes music or exits the game.
        }
        catch (Exception)
        {
            SetStatus("Музыку не удалось запустить. Можно продолжать без неё.");
        }
        finally
        {
            lock (sync)
            {
                activeProcess = null;
                activeTrackStartedAt = 0;
                playbackCancellation?.Dispose();
                playbackCancellation = null;
                playbackTask = null;
                if (!isPaused)
                {
                    currentTrackName = "";
                    playlist = null;
                }
            }
        }
    }

    private void StartPlaybackLocked(IReadOnlyList<string> tracks, int trackIndex, TimeSpan offset, CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        playbackCancellation = source;
        playbackTask = Task.Run(() => PlayLoopAsync(tracks, trackIndex, offset, source.Token), CancellationToken.None);
    }

    private static async Task<string?> CreateResumeSegmentAsync(string trackPath, TimeSpan offset, CancellationToken cancellationToken)
    {
        if (offset < TimeSpan.FromMilliseconds(100))
        {
            return null;
        }

        var segmentPath = Path.Combine(Path.GetTempPath(), $"lake-companion-resume-{Guid.NewGuid():N}{Path.GetExtension(trackPath)}");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("ffmpeg")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("-nostdin");
        process.StartInfo.ArgumentList.Add("-loglevel");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-ss");
        process.StartInfo.ArgumentList.Add(offset.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(trackPath);
        process.StartInfo.ArgumentList.Add("-map");
        process.StartInfo.ArgumentList.Add("0:a:0");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("copy");
        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add(segmentPath);
        process.Start();
        using var registration = cancellationToken.Register(() => TryStop(process));
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode == 0 && File.Exists(segmentPath))
        {
            return segmentPath;
        }

        TryDelete(segmentPath);
        return null;
    }

    private Process CreatePlayer(string trackPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("afplay")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-v");
        process.StartInfo.ArgumentList.Add(Math.Clamp(options.Value.Volume, 0d, 1d).ToString(System.Globalization.CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add(trackPath);
        return process;
    }

    private static string? ResolvePath(string? configuredPath, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        if (Path.IsPathFullyQualified(configuredPath))
        {
            return configuredPath;
        }

        var besideExecutable = Path.Combine(AppContext.BaseDirectory, configuredPath);
        if (isDirectory ? Directory.Exists(besideExecutable) : File.Exists(besideExecutable))
        {
            return besideExecutable;
        }

        return Path.GetFullPath(configuredPath);
    }

    private static bool PathsEqual(string left, string? right) => right is not null
        && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void TryStop(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The player exited between the check and the kill request.
        }
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A player or converter may still be releasing the temporary segment.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup only; the operating system temp folder will recover it later.
        }
    }

    private void SetStatus(string value)
    {
        lock (sync)
        {
            status = value;
        }
    }

    private sealed record PausedPlayback(IReadOnlyList<string> Tracks, int TrackIndex, TimeSpan Offset);
}
