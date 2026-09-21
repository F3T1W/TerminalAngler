namespace LakeCompanion.Domain;

/// <summary>Versioned complete state of one local Lake Companion session.</summary>
public sealed record GameSaveState(
    int SchemaVersion,
    DateTimeOffset SavedAt,
    CharacterSnapshot Character,
    CompanionSnapshot Companion,
    WorldSnapshot World);

/// <summary>Serializable player progression and keep-net state.</summary>
public sealed record CharacterSnapshot(string Name, int FishingLevel, int Experience, List<CaughtFish> Inventory);

/// <summary>Serializable companion relationship and presentation state.</summary>
public sealed record CompanionSnapshot(string Name, int RelationshipScore, CompanionMood Mood);

/// <summary>Serializable tile positions for player and companion.</summary>
public sealed record WorldSnapshot(int PlayerX, int PlayerY, int CompanionX, int CompanionY);
