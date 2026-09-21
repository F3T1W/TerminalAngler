using LakeCompanion.Domain;
using System.Diagnostics;

namespace LakeCompanion.ConsoleApp;

/// <summary>Runs the terminal reaction challenge and returns a deterministic domain outcome for a supplied random source.</summary>
public sealed class FishingGame(Random random)
{
    private static readonly Fish[] FishTable =
    [
        new("perch", "окунь", 320, 15, false),
        new("carp", "карп", 860, 30, false),
        new("pike", "щука", 1_400, 55, true)
    ];

    /// <summary>Runs one bite. The player must repeat three random arrows in sequence before the adaptive deadline.</summary>
    public async Task<FishingAttempt> PlayAsync(int fishingLevel, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fishingLevel, 1);
        var directions = Enumerable.Range(0, 3)
            .Select(_ => (FishingDirection)random.Next(Enum.GetValues<FishingDirection>().Length))
            .ToArray();
        var limit = TimeSpan.FromMilliseconds(Math.Max(2_200, 4_000 - ((fishingLevel - 1) * 150)));
        System.Console.WriteLine($"\n  ПОКЛЁВКА! Повтори: {string.Join(' ', directions.Select(Arrow))}");
        System.Console.WriteLine($"  Нажми три стрелки в этом порядке за {limit.TotalSeconds:0.00} с.");

        // Console.ReadKey cannot be cancelled. Polling KeyAvailable prevents a late, orphaned read
        // from stealing the next command after a bite timer expires.
        var stopwatch = Stopwatch.StartNew();
        FishingDirection? received = null;
        for (var step = 0; step < directions.Length; step++)
        {
            ConsoleKeyInfo? key = null;
            while (stopwatch.Elapsed < limit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (System.Console.KeyAvailable)
                {
                    key = System.Console.ReadKey(intercept: true);
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
            }

            var response = stopwatch.Elapsed;
            if (key is null)
            {
                return new FishingAttempt(directions[0], received, response, limit, CatchQuality.None, null);
            }

            received = await ToDirectionAsync(key.Value, stopwatch, limit, cancellationToken).ConfigureAwait(false);
            if (received != directions[step] || response > limit)
            {
                return new FishingAttempt(directions[0], received, response, limit, CatchQuality.Small, null);
            }

            if (step < directions.Length - 1)
            {
                System.Console.WriteLine($"  Верно. Следующая стрелка: {Arrow(directions[step + 1])}");
            }
        }

        var species = FishTable[random.Next(FishTable.Length)];
        var weight = species.BaseWeightGrams + random.Next(-90, 241) + (fishingLevel * 20);
        var caught = new CaughtFish(species, Math.Max(50, weight), DateTimeOffset.Now, CatchQuality.Big);
        return new FishingAttempt(directions[0], received, stopwatch.Elapsed, limit, CatchQuality.Big, caught);
    }

    private static FishingDirection? ToDirection(ConsoleKeyInfo key) => key.Key switch
    {
        ConsoleKey.UpArrow => FishingDirection.Up,
        ConsoleKey.DownArrow => FishingDirection.Down,
        ConsoleKey.LeftArrow => FishingDirection.Left,
        ConsoleKey.RightArrow => FishingDirection.Right,
        _ => null
    };

    private static async Task<FishingDirection?> ToDirectionAsync(
        ConsoleKeyInfo key,
        Stopwatch stopwatch,
        TimeSpan limit,
        CancellationToken cancellationToken)
    {
        var directDirection = ToDirection(key);
        if (directDirection is not null || key.Key != ConsoleKey.Escape)
        {
            return directDirection;
        }

        // Some terminal hosts deliver an ANSI arrow sequence as Escape, '[', and A/B/C/D separately.
        // Consume that sequence here so a completed fishing input cannot spill into movement on the next turn.
        var graceDeadline = stopwatch.Elapsed + TimeSpan.FromMilliseconds(75);
        if (await TryReadKeyAsync(stopwatch, limit, graceDeadline, cancellationToken).ConfigureAwait(false) is not { KeyChar: '[' })
        {
            return null;
        }

        var finalKey = await TryReadKeyAsync(stopwatch, limit, graceDeadline, cancellationToken).ConfigureAwait(false);
        return finalKey?.KeyChar switch
        {
            'A' => FishingDirection.Up,
            'B' => FishingDirection.Down,
            'C' => FishingDirection.Right,
            'D' => FishingDirection.Left,
            _ => null
        };
    }

    private static async Task<ConsoleKeyInfo?> TryReadKeyAsync(
        Stopwatch stopwatch,
        TimeSpan limit,
        TimeSpan graceDeadline,
        CancellationToken cancellationToken)
    {
        while (stopwatch.Elapsed < limit && stopwatch.Elapsed < graceDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (System.Console.KeyAvailable)
            {
                return System.Console.ReadKey(intercept: true);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static string Arrow(FishingDirection direction) => direction switch
    {
        FishingDirection.Up => "↑",
        FishingDirection.Down => "↓",
        FishingDirection.Left => "←",
        FishingDirection.Right => "→",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
    };
}
