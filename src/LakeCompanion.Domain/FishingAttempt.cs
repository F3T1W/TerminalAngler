namespace LakeCompanion.Domain;

/// <summary>Immutable result of resolving one fishing reaction challenge.</summary>
public sealed record FishingAttempt(
    FishingDirection ExpectedDirection,
    FishingDirection? ReceivedDirection,
    TimeSpan ResponseTime,
    TimeSpan TimeLimit,
    CatchQuality Quality,
    CaughtFish? Catch)
{
    /// <summary>Gets whether the player completed the prompt before it expired.</summary>
    public bool IsSuccess => Quality is CatchQuality.Big;
}
