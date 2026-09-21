using Microsoft.EntityFrameworkCore;

namespace LakeCompanion.Infrastructure;

/// <summary>SQLite context for durable game facts; narrative generation is never persisted here by default.</summary>
public sealed class LakeCompanionDbContext(DbContextOptions<LakeCompanionDbContext> options) : DbContext(options)
{
    /// <summary>Gets the single active-player projection table.</summary>
    public DbSet<PlayerSaveEntity> Players => Set<PlayerSaveEntity>();

    /// <summary>Gets the fish catch journal table.</summary>
    public DbSet<FishCatchEntity> FishCatches => Set<FishCatchEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerSaveEntity>(entity =>
        {
            entity.HasKey(player => player.Id);
            entity.Property(player => player.PlayerName).HasMaxLength(80).IsRequired();
            entity.Property(player => player.CompanionName).HasMaxLength(80).IsRequired();
        });
        modelBuilder.Entity<FishCatchEntity>(entity =>
        {
            entity.HasKey(catchEntry => catchEntry.Id);
            entity.Property(catchEntry => catchEntry.SpeciesId).HasMaxLength(40).IsRequired();
            entity.Property(catchEntry => catchEntry.SpeciesName).HasMaxLength(80).IsRequired();
            entity.HasIndex(catchEntry => catchEntry.CaughtAt);
        });
    }
}

/// <summary>Database projection of the active character and companion state.</summary>
public sealed class PlayerSaveEntity
{
    /// <summary>Gets or sets the stable row identity.</summary>
    public int Id { get; set; } = 1;
    /// <summary>Gets or sets the player display name.</summary>
    public string PlayerName { get; set; } = string.Empty;
    /// <summary>Gets or sets the fishing level.</summary>
    public int FishingLevel { get; set; }
    /// <summary>Gets or sets earned experience within the current level.</summary>
    public int Experience { get; set; }
    /// <summary>Gets or sets the companion display name.</summary>
    public string CompanionName { get; set; } = string.Empty;
    /// <summary>Gets or sets the bounded relationship score.</summary>
    public int RelationshipScore { get; set; }
    /// <summary>Gets or sets the UTC save timestamp.</summary>
    public DateTimeOffset SavedAt { get; set; }
}

/// <summary>Database projection of one caught fish.</summary>
public sealed class FishCatchEntity
{
    /// <summary>Gets or sets the primary key.</summary>
    public long Id { get; set; }
    /// <summary>Gets or sets the fish species identifier.</summary>
    public string SpeciesId { get; set; } = string.Empty;
    /// <summary>Gets or sets the player-facing species name.</summary>
    public string SpeciesName { get; set; } = string.Empty;
    /// <summary>Gets or sets the landed weight in grams.</summary>
    public int WeightGrams { get; set; }
    /// <summary>Gets or sets the local catch time.</summary>
    public DateTimeOffset CaughtAt { get; set; }
    /// <summary>Gets or sets the serialized catch quality.</summary>
    public string Quality { get; set; } = string.Empty;
}
