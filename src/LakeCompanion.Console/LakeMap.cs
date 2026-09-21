namespace LakeCompanion.ConsoleApp;

using LakeCompanion.Domain;

/// <summary>Maintains the walkable tile map of Lake Alder and draws it for the terminal game loop.</summary>
public sealed class LakeMap
{
    private static readonly string[] Terrain =
    [
        "#################################################",
        "#...................~~~~~~~~~~~.................#",
        "#.................~~~~~~~~~~~~~~~...............#",
        "#...............~~~~~~~~~~~~~~~~~~~.............#",
        "#..............~~~~~~~~~~~~~~~~~~~~~............#",
        "#.............~~~~~~~~~~~~~~~~~~~~~~~...........#",
        "#.............~~~~~~~~~~~~~~~~~~~~~~~...........#",
        "#..............~~~~~~~~~~~~~~~~~~~~~............#",
        "#...............~~~~~~~~~~~~~~~~~~~.............#",
        "#.................~~~~~~~~~~~~~~~...............#",
        "#...................~~~~~~~~~~~.................#",
        "#...............................................#",
        "#################################################"
    ];

    private static readonly Landmark[] Landmarks =
    [
        new('R', "Камышовый берег", new Point(6, 2)),
        new('S', "Каменные ступени", new Point(29, 1)),
        new('B', "Старый эллинг", new Point(42, 2)),
        new('D', "Рассветный причал", new Point(5, 10)),
        new('W', "Ивовый изгиб", new Point(17, 10)),
        new('Q', "Тихая заводь", new Point(41, 10))
    ];

    private Point playerPosition = new(5, 10);
    private Point companionPosition = new(6, 10);

    /// <summary>Gets the location inferred from the player’s current shore tile.</summary>
    public LocationInfo CurrentLocation => Locate(playerPosition);

    /// <summary>Creates a save snapshot for the exact map tiles occupied by both characters.</summary>
    public WorldSnapshot ToSnapshot() => new(playerPosition.X, playerPosition.Y, companionPosition.X, companionPosition.Y);

    /// <summary>Restores both characters to valid shore tiles; returns false for a damaged or incompatible save.</summary>
    public bool TryRestore(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var savedPlayer = new Point(snapshot.PlayerX, snapshot.PlayerY);
        var savedCompanion = new Point(snapshot.CompanionX, snapshot.CompanionY);
        if (!IsWalkable(savedPlayer) || !IsWalkable(savedCompanion) || savedPlayer == savedCompanion)
        {
            return false;
        }

        playerPosition = savedPlayer;
        companionPosition = savedCompanion;
        return true;
    }

    /// <summary>Attempts cardinal movement, refusing water and the outer boundary.</summary>
    public bool TryMove(ConsoleKey key, out string result)
    {
        var delta = key switch
        {
            ConsoleKey.UpArrow or ConsoleKey.W => new Point(0, -1),
            ConsoleKey.DownArrow or ConsoleKey.S => new Point(0, 1),
            ConsoleKey.LeftArrow or ConsoleKey.A => new Point(-1, 0),
            ConsoleKey.RightArrow or ConsoleKey.D => new Point(1, 0),
            _ => new Point(0, 0)
        };

        if (delta == default)
        {
            result = string.Empty;
            return false;
        }

        var next = new Point(playerPosition.X + delta.X, playerPosition.Y + delta.Y);
        if (!IsWalkable(next))
        {
            result = "Берег здесь заканчивается. Вода прекрасна, но в неё не зайти.";
            return true;
        }

        var previousPlayerPosition = playerPosition;
        playerPosition = next;
        companionPosition = Follow(companionPosition, playerPosition, previousPlayerPosition);
        result = $"Ты идёшь по берегу. {CurrentLocation.Name}.";
        return true;
    }

    /// <summary>Draws the complete lake, land, landmarks, and both characters on the active screen.</summary>
    public void Render()
    {
        System.Console.WriteLine("\u001b[36m╭──────────────────────── ОЗЕРО ОЛЬХОВОЕ ──────────────────────╮\u001b[0m");
        System.Console.WriteLine("\u001b[36m│  @ ты  & Рован  . берег  ~ вода  # граница                    │\u001b[0m");
        for (var y = 0; y < Terrain.Length; y++)
        {
            var line = new char[Terrain[y].Length];
            for (var x = 0; x < line.Length; x++)
            {
                var point = new Point(x, y);
                line[x] = point == playerPosition ? '@'
                    : point == companionPosition ? '&'
                    : LandmarkSymbolAt(point) ?? Terrain[y][x];
            }

            System.Console.WriteLine($"\u001b[36m│\u001b[0m {new string(line)} \u001b[36m│\u001b[0m");
        }
        System.Console.WriteLine("\u001b[36m╰──────────────────────────────────────────────────────────────╯\u001b[0m");
        System.Console.WriteLine("  R Камыши · S Ступени · B Эллинг · D Причал · W Ивы · Q Заводь");
    }

    private static Point Follow(Point companion, Point player, Point previousPlayer)
    {
        // When the player walks into Rowan's tile, he gives way and takes the space just vacated.
        // This keeps both sprites visible and reads as a companion walking alongside rather than overlap.
        if (companion == player && IsWalkable(previousPlayer))
        {
            return previousPlayer;
        }

        var horizontal = Math.Sign(player.X - companion.X);
        var vertical = Math.Sign(player.Y - companion.Y);
        var next = Math.Abs(player.X - companion.X) >= Math.Abs(player.Y - companion.Y)
            ? new Point(companion.X + horizontal, companion.Y)
            : new Point(companion.X, companion.Y + vertical);
        return IsWalkable(next) && next != player ? next : companion;
    }

    private static bool IsWalkable(Point point) => point.Y >= 0
        && point.Y < Terrain.Length
        && point.X >= 0
        && point.X < Terrain[point.Y].Length
        && Terrain[point.Y][point.X] is not ('#' or '~');

    private static char? LandmarkSymbolAt(Point point)
    {
        foreach (var landmark in Landmarks)
        {
            if (landmark.Position == point)
            {
                return landmark.Symbol;
            }
        }

        return null;
    }

    private static LocationInfo Locate(Point point) => point switch
    {
        { X: <= 12, Y: <= 4 } => new LocationInfo("Камышовый берег", "Высокие камыши образуют тихий коридор у тёмной, терпеливой воды."),
        { X: >= 35, Y: <= 4 } => new LocationInfo("Старый эллинг", "Старый эллинг встречает ветер и беспокойную рябь."),
        { X: <= 12, Y: >= 8 } => new LocationInfo("Рассветный причал", "Старые доски поскрипывают под ногами, а у берега мелькают мальки."),
        { X: >= 34, Y: >= 8 } => new LocationInfo("Тихая заводь", "В дальней заводи последние лучи света лежат серебряными лентами."),
        { X: <= 25, Y: >= 8 } => new LocationInfo("Ивовый изгиб", "Листья ивы касаются воды в укромном изгибе берега."),
        _ => new LocationInfo("Каменные ступени", "Старые плоские ступени уходят в прозрачную воду, где озеро становится глубже.")
    };

    private readonly record struct Point(int X, int Y);
    private readonly record struct Landmark(char Symbol, string Name, Point Position);
}

/// <summary>Player-facing name and prose for the active lake area.</summary>
public sealed record LocationInfo(string Name, string Description);
