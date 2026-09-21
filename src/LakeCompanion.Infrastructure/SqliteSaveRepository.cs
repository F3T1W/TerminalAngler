using LakeCompanion.Domain;
using Microsoft.EntityFrameworkCore;

namespace LakeCompanion.Infrastructure;

/// <summary>Writes a normalized local game snapshot to SQLite using a context factory per operation.</summary>
public sealed class SqliteSaveRepository(IDbContextFactory<LakeCompanionDbContext> contextFactory)
{
    /// <summary>Creates the database if absent and persists the active profile plus its catch journal.</summary>
    public async Task SaveAsync(Character character, Companion companion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(companion);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        var profile = await context.Players.SingleOrDefaultAsync(player => player.Id == 1, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            profile = new PlayerSaveEntity();
            context.Players.Add(profile);
        }

        profile.PlayerName = character.Name;
        profile.FishingLevel = character.FishingLevel;
        profile.Experience = character.Experience;
        profile.CompanionName = companion.Name;
        profile.RelationshipScore = companion.Relationship.Score;
        profile.SavedAt = DateTimeOffset.UtcNow;

        // The character inventory is the authoritative snapshot. Replacing its journal avoids duplicate
        // rows after a later load/save cycle; richer multi-profile history belongs in a dedicated aggregate.
        await context.FishCatches.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        foreach (var caught in character.Inventory)
        {
            context.FishCatches.Add(new FishCatchEntity
            {
                SpeciesId = caught.Species.Id,
                SpeciesName = caught.Species.Name,
                WeightGrams = caught.WeightGrams,
                CaughtAt = caught.CaughtAt,
                Quality = caught.Quality.ToString()
            });
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
