namespace LakeCompanion.Domain;

/// <summary>Persistent state belonging to the player.</summary>
public sealed class Character
{
    /// <summary>Initializes a new player profile.</summary>
    public Character(string name)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Angler" : name.Trim();
    }

    /// <summary>Gets the display name selected by the player.</summary>
    public string Name { get; }

    /// <summary>Gets the fishing level, beginning at one.</summary>
    public int FishingLevel { get; private set; } = 1;

    /// <summary>Gets accumulated angling experience.</summary>
    public int Experience { get; private set; }

    /// <summary>Gets fish currently held in the keep-net.</summary>
    public List<CaughtFish> Inventory { get; } = [];

    /// <summary>Creates a persistence-safe snapshot of the character and every retained fish.</summary>
    public CharacterSnapshot ToSnapshot() => new(Name, FishingLevel, Experience, [.. Inventory]);

    /// <summary>Restores a character from a previously validated local save snapshot.</summary>
    public static Character Restore(CharacterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var character = new Character(snapshot.Name)
        {
            FishingLevel = Math.Max(1, snapshot.FishingLevel),
            Experience = Math.Max(0, snapshot.Experience)
        };
        character.Inventory.AddRange(snapshot.Inventory ?? []);
        return character;
    }

    /// <summary>Adds experience and advances exactly one or more levels as thresholds are crossed.</summary>
    public void GainExperience(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Experience += amount;
        while (Experience >= FishingLevel * 100)
        {
            Experience -= FishingLevel * 100;
            FishingLevel++;
        }
    }
}

/// <summary>Describes the local companion and their durable relationship state.</summary>
public sealed class Companion
{
    /// <summary>Initializes the default companion.</summary>
    public Companion(string name = "Rowan") => Name = name;

    /// <summary>Gets the companion's screen name.</summary>
    public string Name { get; }

    /// <summary>Gets the current relationship, bounded to the design range.</summary>
    public Relationship Relationship { get; } = new();

    /// <summary>Gets or sets the current observable companion mood.</summary>
    public CompanionMood Mood { get; set; } = CompanionMood.Content;

    /// <summary>Creates a persistence-safe snapshot of companion relationship state.</summary>
    public CompanionSnapshot ToSnapshot() => new(Name, Relationship.Score, Mood);

    /// <summary>Restores the companion without bypassing relationship score bounds.</summary>
    public static Companion Restore(CompanionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var companion = new Companion(snapshot.Name) { Mood = snapshot.Mood };
        companion.Relationship.SetScore(snapshot.RelationshipScore);
        return companion;
    }
}

/// <summary>Encapsulates the relationship meter and protects its invariant.</summary>
public sealed class Relationship
{
    /// <summary>Gets the signed affinity score in the inclusive range -100 through 100.</summary>
    public int Score { get; private set; }

    /// <summary>Gets whether dialogue is intentionally limited by the silence rule.</summary>
    public bool IsSilent => Score < 0;

    /// <summary>Adjusts affinity without allowing score overflow.</summary>
    public void Adjust(int delta) => Score = Math.Clamp(Score + delta, -100, 100);

    /// <summary>Restores a persisted score while maintaining its inclusive range invariant.</summary>
    internal void SetScore(int score) => Score = Math.Clamp(score, -100, 100);
}

/// <summary>Defines a fish species available to the fishing system.</summary>
public sealed record Fish(string Id, string Name, int BaseWeightGrams, int ExperienceAward, bool IsRare);

/// <summary>Records an individual catch in a character inventory.</summary>
public sealed record CaughtFish(Fish Species, int WeightGrams, DateTimeOffset CaughtAt, CatchQuality Quality);

/// <summary>Outcome quality for a single fishing attempt.</summary>
public enum CatchQuality
{
    /// <summary>No fish was caught.</summary>
    None,
    /// <summary>A minor catch or near-catch occurred.</summary>
    Small,
    /// <summary>The player completed the reaction challenge and landed a prized catch.</summary>
    Big
}

/// <summary>Visible emotional tone used by the companion presentation layer.</summary>
public enum CompanionMood
{
    /// <summary>Relaxed and receptive.</summary>
    Content,
    /// <summary>Attentive to a recent event.</summary>
    Curious,
    /// <summary>Celebrating a notable moment.</summary>
    Excited,
    /// <summary>Gently attentive to player frustration or fatigue.</summary>
    Concerned,
    /// <summary>Using the relationship silence presentation.</summary>
    Quiet
}

/// <summary>Input directions recognized by the fishing reaction challenge.</summary>
public enum FishingDirection
{
    /// <summary>Up arrow input.</summary>
    Up,
    /// <summary>Down arrow input.</summary>
    Down,
    /// <summary>Left arrow input.</summary>
    Left,
    /// <summary>Right arrow input.</summary>
    Right
}
