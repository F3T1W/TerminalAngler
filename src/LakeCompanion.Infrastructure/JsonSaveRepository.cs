using System.Text.Json;
using LakeCompanion.Domain;

namespace LakeCompanion.Infrastructure;

/// <summary>Atomic, versioned JSON store for the complete local game session.</summary>
public sealed class JsonSaveRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string savePath;

    /// <summary>Creates a repository beneath an application-owned local save directory.</summary>
    public JsonSaveRepository(string? directory = null)
    {
        var baseDirectory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LakeCompanion");
        Directory.CreateDirectory(baseDirectory);
        savePath = Path.Combine(baseDirectory, "save.json");
    }

    /// <summary>Gets the complete JSON savegame path for diagnostics or backup.</summary>
    public string SavePath => savePath;

    /// <summary>Loads the last complete save or returns null when the player has not saved yet.</summary>
    /// <exception cref="InvalidDataException">Thrown if an existing save cannot be decoded safely.</exception>
    public async Task<GameSaveState?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(savePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(savePath, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("PlayerName", out _))
            {
                return MigrateLegacyState(document.RootElement);
            }

            var state = JsonSerializer.Deserialize<GameSaveState>(json, SerializerOptions);
            if (state is null || state.SchemaVersion != 1 || state.Character is null || state.Companion is null || state.World is null)
            {
                throw new InvalidDataException("The save file has an unsupported schema.");
            }

            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The save file is malformed.", exception);
        }
    }

    /// <summary>Writes a full snapshot through a temporary file so an interrupted save cannot corrupt the previous session.</summary>
    public async Task SaveAsync(GameSaveState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var temporaryPath = savePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, savePath, overwrite: true);
    }

    private static GameSaveState MigrateLegacyState(JsonElement root)
    {
        var playerName = root.GetProperty("PlayerName").GetString() ?? "Mira";
        var fishingLevel = root.TryGetProperty("FishingLevel", out var level) ? level.GetInt32() : 1;
        var experience = root.TryGetProperty("Experience", out var xp) ? xp.GetInt32() : 0;
        var companionName = root.TryGetProperty("CompanionName", out var companion) ? companion.GetString() ?? "Rowan" : "Rowan";
        var relationship = root.TryGetProperty("RelationshipScore", out var score) ? score.GetInt32() : 0;
        return new GameSaveState(
            1,
            DateTimeOffset.UtcNow,
            new CharacterSnapshot(playerName, fishingLevel, experience, []),
            new CompanionSnapshot(companionName, relationship, CompanionMood.Content),
            new WorldSnapshot(5, 10, 6, 10));
    }
}
