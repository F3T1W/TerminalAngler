using LakeCompanion.Domain;

namespace LakeCompanion.Application;

/// <summary>Produces companion reactions from game context without exposing a specific model provider.</summary>
public interface ICompanionService
{
    /// <summary>Creates a safe, concise reaction to the player's latest action.</summary>
    Task<CompanionReaction> ReactAsync(Companion companion, string playerAction, CancellationToken cancellationToken);
}

/// <summary>Generates text with a local language model.</summary>
public interface ILocalLanguageModel
{
    /// <summary>Completes a prompt using only the configured local endpoint.</summary>
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken);
}

/// <summary>Converts an optional local microphone recording to text.</summary>
public interface ISpeechRecognizer
{
    /// <summary>Transcribes a bounded PCM/WAV recording, returning null when no speech is recognized.</summary>
    Task<string?> TranscribeAsync(Stream audio, CancellationToken cancellationToken);
}

/// <summary>Renders optional local speech from a companion response.</summary>
public interface ISpeechSynthesizer
{
    /// <summary>Speaks text through the configured local engine.</summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken);
}

/// <summary>Controls optional local background audio without coupling the game loop to an operating-system player.</summary>
public interface IAmbientMusicService : IAsyncDisposable
{
    /// <summary>Gets whether ambient audio is currently playing.</summary>
    bool IsPlaying { get; }

    /// <summary>Gets whether the active track is paused at its current playback position.</summary>
    bool IsPaused { get; }

    /// <summary>Gets a concise player-facing status message.</summary>
    string Status { get; }

    /// <summary>Begins looping local ambient audio when the current platform supports it.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Pauses the current track without resetting its playback position.</summary>
    Task PauseAsync(CancellationToken cancellationToken);

    /// <summary>Resumes a previously paused track from its preserved playback position.</summary>
    Task ResumeAsync(CancellationToken cancellationToken);

    /// <summary>Stops any active local ambient audio process.</summary>
    Task StopAsync(CancellationToken cancellationToken);
}

/// <summary>Captures one bounded local microphone utterance and returns its offline recognition outcome.</summary>
public interface IVoiceInputService
{
    /// <summary>Raised when capture or local transcription changes stage.</summary>
    event Action<string>? StatusChanged;

    /// <summary>Records and transcribes one push-to-talk utterance, returning null when no speech is recognized.</summary>
    Task<VoiceRecognitionResult> CaptureAndTranscribeAsync(CancellationToken cancellationToken);
}

/// <summary>Outcome of one local microphone capture and speech-recognition attempt.</summary>
/// <param name="Transcript">Recognized speech, when available.</param>
/// <param name="FailureMessage">A user-facing diagnostic when no transcript was produced.</param>
public sealed record VoiceRecognitionResult(string? Transcript, string? FailureMessage);

/// <summary>A model-independent response ready for terminal rendering or speech.</summary>
public sealed record CompanionReaction(string Text, CompanionMood Mood, bool IsSilent);
