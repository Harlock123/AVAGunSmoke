namespace AVAGunSmoke.Engine;

/// <summary>The controls: walking (each -1, 0 or 1; up is +1), and the three triggers: fire up-left, up, up-right.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Left = false, bool Up = false, bool Right = false)
{
    public bool Firing => Left || Up || Right;
}

/// <summary>
/// The screen and the world. The world runs up the map from 0 at the start of a stage, so y grows upwards; the
/// camera is the world y at the bottom of the screen, and only ever moves up.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Cell = 32;
    public const int Cols = 32;
    /// <summary>The score bar across the top of the screen.</summary>
    public const double Hud = 56;

    public static double ScreenY(double wy, double camera) => Height - (wy - camera);
    public static int ColOf(double x) => (int)Math.Floor(x / Cell);
    public static int RowOf(double y) => (int)Math.Floor(y / Cell);
    public static double CellX(int col) => (col + 0.5) * Cell;
    public static double CellY(int row) => (row + 0.5) * Cell;
    public static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));

    /// <summary>The eight ways to walk, from up (0) round clockwise.</summary>
    public static readonly (int Dx, int Dy)[] Ways = [(0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1)];
}

/// <summary>What stands in a cell of the map.</summary>
public static class Ground
{
    public const char Open = '.', Building = 'H', Rock = 'R', Trough = 'T', Wagon = 'W', Rail = '=', Fence = 'F', Cactus = 'C',
        Water = '~', Bridge = 'b', Barrel = 'B', Track = '#', Crate = 'X', Hideout = 'D';

    /// <summary>Whether someone on foot can't go there.</summary>
    public static bool Blocks(char c) => c is Building or Rock or Trough or Wagon or Fence or Cactus or Water or Barrel or Crate or Hideout or Rail;

    /// <summary>Whether a bullet stops there.</summary>
    public static bool Stops(char c) => c is Building or Rock or Wagon or Barrel or Crate or Hideout;
}

public enum SpawnKind { Outlaw, Dynamiter, Window, Rider, Bag }

/// <summary>Something waiting in the map, that comes on when the top of the screen reaches it.</summary>
public sealed record Spawn(SpawnKind Kind, double X, double Y);

/// <summary>A wanted man: the boss at the end of a stage.</summary>
public sealed record Wanted(string Name, string Crime, int Bounty, int Hits);

/// <summary>
/// One stage of the trail: a map of cells, bottom row first, built from blocks of a town street (shopfronts down
/// both sides, barrels, troughs and wagons in the road), canyon walls, a creek with a bridge, a railroad crossing;
/// ending in the open ground before the wanted man's hideout. Where the outlaws wait in it, and its barrels.
/// </summary>
public sealed class Stage
{
    public string Name { get; }
    public int Theme { get; }
    public Wanted Boss { get; }
    public List<char[]> Rows { get; } = [];
    /// <summary>The words on the shopfronts, by the row their sign is on and which side of the street.</summary>
    public List<(int Row, bool Left, string Sign)> Signs { get; } = [];
    public List<Spawn> Spawns { get; } = [];
    public List<(int Col, int Row)> Barrels { get; } = [];
    /// <summary>The row the hideout starts at, across the top of the showdown.</summary>
    public int HideoutRow { get; private set; }

    public double HideoutY => HideoutRow * Field.Cell;
    /// <summary>The camera stops here, with the hideout across the top of the screen.</summary>
    public double CameraEnd => HideoutY + 2 * Field.Cell - (Field.Height - Field.Hud);

    public char At(int col, int row) => col < 0 || col >= Field.Cols ? Ground.Building : row < 0 ? Ground.Open : row >= Rows.Count ? Ground.Hideout : Rows[row][col];
    public char AtPoint(double x, double y) => At(Field.ColOf(x), Field.RowOf(y));
    public bool Blocks(int col, int row) => Ground.Blocks(At(col, row));

    /// <summary>Whether a bullet could fly straight from one point to the other.</summary>
    public bool Clear(double ax, double ay, double bx, double by)
    {
        double d = Field.Distance(ax, ay, bx, by);
        int n = Math.Max(1, (int)(d / 8));
        for (int i = 1; i < n; i++)
        {
            double t = (double)i / n;
            if (Ground.Stops(AtPoint(ax + (bx - ax) * t, ay + (by - ay) * t))) return false;
        }
        return true;
    }

    /// <summary>Pushes someone (a circle) out of anything they can't stand in; true if it had to.</summary>
    public bool Push(ref double x, ref double y, double r)
    {
        bool pushed = false;
        int c0 = Field.ColOf(x - r), c1 = Field.ColOf(x + r), r0 = Field.RowOf(y - r), r1 = Field.RowOf(y + r);
        for (int row = r0; row <= r1; row++)
        for (int col = c0; col <= c1; col++)
        {
            if (!Blocks(col, row)) continue;
            double left = col * Field.Cell, bottom = row * Field.Cell;
            double nx = Math.Clamp(x, left, left + Field.Cell), ny = Math.Clamp(y, bottom, bottom + Field.Cell);
            double dx = x - nx, dy = y - ny, d = Math.Sqrt(dx * dx + dy * dy);
            if (d >= r) continue;
            if (d < 1e-6)
            {
                double l = x - left, rt = left + Field.Cell - x, b = y - bottom, t = bottom + Field.Cell - y, m = Math.Min(Math.Min(l, rt), Math.Min(b, t));
                if (m == l) x = left - r; else if (m == rt) x = left + Field.Cell + r; else if (m == b) y = bottom - r; else y = bottom + Field.Cell + r;
            }
            else
            {
                x = nx + dx / d * r;
                y = ny + dy / d * r;
            }
            pushed = true;
        }
        return pushed;
    }

    private static readonly string[] Names = ["DUST CREEK", "RATTLESNAKE CANYON", "IRON HORSE JUNCTION"];

    private static readonly Wanted[] Bosses =
    [
        new("KNIFE McGRAW", "FOR CATTLE RUSTLING", 10000, 10),
        new("DYNAMITE DOLAN", "FOR ROBBING THE BANK", 20000, 14),
        new("COLONEL CRANE", "FOR HOLDING UP THE MAIL TRAIN", 30000, 18),
    ];

    private static readonly string[] Shops = ["SALOON", "BANK", "HOTEL", "GENERAL STORE", "SHERIFF", "BARBER", "LIVERY", "ASSAY OFFICE", "GUNSMITH", "UNDERTAKER", "TELEGRAPH", "DRY GOODS"];

    private Stage(string name, int theme, Wanted boss)
    {
        Name = name;
        Theme = theme;
        Boss = boss;
    }

    public static int Count => Names.Length;

    /// <summary>
    /// Stage <paramref name="number"/> (1 for the first): the same ground every time for the same number, the three
    /// kinds of country in turn, longer and busier as the numbers go up. Past the third, the wanted men come back
    /// with bigger bounties and thicker hides.
    /// </summary>
    public static Stage Build(int number)
    {
        int theme = (number - 1) % 3, round = (number - 1) / 3;
        var w = Bosses[theme];
        var s = new Stage(Names[theme], theme, w with { Bounty = w.Bounty * (round + 1), Hits = w.Hits + 6 * round });
        var rng = new Random(1985 + number * 7919);
        s.Edges(8, rng, 0);
        int blocks = 9 + Math.Min(5, number);
        for (int i = 0; i < blocks; i++)
        {
            string kind = theme switch
            {
                0 => i % 4 == 2 ? "creek" : "street",
                1 => i % 3 == 1 ? "creek" : "canyon",
                _ => i % 3 == 1 ? "railroad" : "street",
            };
            switch (kind)
            {
                case "street": s.Street(rng); break;
                case "canyon": s.Canyon(rng); break;
                case "creek": s.Creek(rng); break;
                case "railroad": s.Railroad(rng); break;
            }
            if (i % 3 == 2) s.Spawns.Add(new Spawn(SpawnKind.Rider, 0, (s.Rows.Count - 2) * Field.Cell));
        }
        s.Showdown(rng);
        return s;
    }

    private char[] Row(int left = 0, int right = 0, char side = Ground.Building)
    {
        var row = Enumerable.Repeat(Ground.Open, Field.Cols).ToArray();
        for (int c = 0; c < left; c++) row[c] = side;
        for (int c = 0; c < right; c++) row[Field.Cols - 1 - c] = side;
        return row;
    }

    /// <summary>A free spot in a row of the road.</summary>
    private int FreeCol(Random rng, int row)
    {
        for (int i = 0; i < 40; i++)
        {
            int c = 6 + rng.Next(Field.Cols - 12);
            if (!Ground.Blocks(Rows[row][c])) return c;
        }
        return Field.Cols / 2;
    }

    private void Enemy(Random rng, int row, SpawnKind kind = SpawnKind.Outlaw) =>
        Spawns.Add(new Spawn(kind, Field.CellX(FreeCol(rng, row)), Field.CellY(row)));

    /// <summary>Open road with the sides built up (or walled with rock in the canyon).</summary>
    private void Edges(int rows, Random rng, int sides, char side = Ground.Building)
    {
        for (int r = 0; r < rows; r++) Rows.Add(Row(sides, sides, side));
    }

    /// <summary>A barrel in the road, that breaks after a few shots and sometimes has something in it.</summary>
    private void PutBarrel(int col, int row)
    {
        Rows[row][col] = Ground.Barrel;
        Barrels.Add((col, row));
    }

    /// <summary>
    /// A block of the town: a shopfront down each side with a sign and windows a gunman can lean out of, an alley
    /// between blocks, and in the road troughs, hitching rails, barrels and the odd wagon.
    /// </summary>
    private void Street(Random rng)
    {
        int start = Rows.Count, rows = 12;
        int left = 5 + rng.Next(2), right = 5 + rng.Next(2);
        for (int r = 0; r < rows; r++)
        {
            // An alley at the end of the block, between this one and the next.
            bool alley = r >= rows - 2;
            Rows.Add(alley ? Row(1, 1) : Row(left, right));
        }
        Signs.Add((start + rows / 2, true, Shops[rng.Next(Shops.Length)]));
        Signs.Add((start + rows / 2 - 2, false, Shops[rng.Next(Shops.Length)]));
        // Windows: a gunman in one on each side.
        Spawns.Add(new Spawn(SpawnKind.Window, left * Field.Cell, Field.CellY(start + 3 + rng.Next(5))));
        Spawns.Add(new Spawn(SpawnKind.Window, Field.Width - right * Field.Cell, Field.CellY(start + 3 + rng.Next(5))));
        // The road.
        int r1 = start + 2 + rng.Next(3);
        if (rng.Next(2) == 0) { Rows[r1][left] = Rows[r1 + 1][left] = Ground.Trough; }
        else for (int k = 0; k < 3; k++) Rows[r1][Field.Cols - right - 1 - k] = Ground.Fence;
        if (rng.Next(3) == 0)
        {
            int c = 10 + rng.Next(Field.Cols - 23), r2 = start + 5 + rng.Next(3);
            for (int dc = 0; dc < 3; dc++) for (int dr = 0; dr < 2; dr++) Rows[r2 + dr][c + dc] = Ground.Wagon;
        }
        for (int k = 0; k < 2 + rng.Next(2); k++)
        {
            int r = start + 1 + rng.Next(rows - 3), c = FreeCol(rng, r);
            if (Rows[r][c] == Ground.Open) PutBarrel(c, r);
        }
        Enemy(rng, start + 6, rng.Next(3) == 0 ? SpawnKind.Dynamiter : SpawnKind.Outlaw);
        Enemy(rng, start + 9);
    }

    /// <summary>A stretch of canyon: rock walls on both sides, cacti and boulders in the way.</summary>
    private void Canyon(Random rng)
    {
        int start = Rows.Count, rows = 13;
        for (int r = 0; r < rows; r++)
        {
            int l = 3 + (int)(2 * Math.Sin((start + r) * 0.4)), rt = 3 + (int)(2 * Math.Cos((start + r) * 0.35));
            var row = Row(l, rt, Ground.Rock);
            for (int c = l; c < Field.Cols - rt; c++)
                if (rng.NextDouble() < 0.035) row[c] = rng.Next(2) == 0 ? Ground.Cactus : Ground.Rock;
            Rows.Add(row);
        }
        for (int k = 0; k < 2; k++)
        {
            int r = start + 2 + rng.Next(rows - 4), c = FreeCol(rng, r);
            if (Rows[r][c] == Ground.Open) PutBarrel(c, r);
        }
        Enemy(rng, start + 5, SpawnKind.Dynamiter);
        Enemy(rng, start + 8);
        Enemy(rng, start + 11);
    }

    /// <summary>A creek across the trail, with one bridge, and an outlaw on the far bank.</summary>
    private void Creek(Random rng)
    {
        int sides = Theme == 1 ? 3 : 1;
        Edges(3, rng, sides, Theme == 1 ? Ground.Rock : Ground.Building);
        int b = 9 + rng.Next(12);
        for (int r = 0; r < 3; r++)
        {
            var row = Row(sides, sides, Theme == 1 ? Ground.Rock : Ground.Building);
            for (int c = sides; c < Field.Cols - sides; c++) row[c] = Ground.Water;
            for (int k = 0; k < 4; k++) row[b + k] = Ground.Bridge;
            Rows.Add(row);
        }
        Edges(5, rng, sides, Theme == 1 ? Ground.Rock : Ground.Building);
        Enemy(rng, Rows.Count - 3);
        if (rng.Next(2) == 0) Spawns.Add(new Spawn(SpawnKind.Bag, Field.CellX(FreeCol(rng, Rows.Count - 2)), Field.CellY(Rows.Count - 2)));
    }

    /// <summary>A railroad across the trail: the rails to step over, crates on the platform, a water tower.</summary>
    private void Railroad(Random rng)
    {
        Edges(3, rng, 4);
        int r = Rows.Count;
        Rows.Add(Row(1, 1));
        var rail = Row(1, 1);
        for (int c = 1; c < Field.Cols - 1; c++) rail[c] = Ground.Track;
        Rows.Add(rail);
        Rows.Add((char[])rail.Clone());
        Rows.Add(Row(1, 1));
        for (int k = 0; k < 3; k++)
        {
            int c = 3 + rng.Next(Field.Cols - 6);
            Rows[r][c] = Ground.Crate;
        }
        Edges(5, rng, 4);
        Enemy(rng, Rows.Count - 2, SpawnKind.Dynamiter);
        Enemy(rng, Rows.Count - 4);
    }

    /// <summary>The open ground in front of the wanted man's hideout, where the showdown is.</summary>
    private void Showdown(Random rng)
    {
        Edges(12, rng, 2, Theme == 1 ? Ground.Rock : Ground.Building);
        HideoutRow = Rows.Count;
        for (int r = 0; r < 12; r++) Rows.Add(Enumerable.Repeat(Ground.Hideout, Field.Cols).ToArray());
    }
}

/// <summary>The player: a gunslinger with two six-guns, and whatever he's picked up.</summary>
public sealed class Gunslinger
{
    public double X, Y, Walk;
    public bool Alive = true;
    public double Safe, FireIn;
    /// <summary>Pairs of boots (each quicker), whether he has a rifle (longer shots), the rapid-fire bullets, and a horse (which takes a hit for him).</summary>
    public int Boots;
    public bool Rifle, Rapid;
    public int Horse;
    /// <summary>Which way he last moved across (for the drawing): -1 left, 1 right.</summary>
    public int Lean;
    public const double Radius = 10;
}

public enum OutlawKind
{
    /// <summary>Walks to a spot near you, stops, draws and fires.</summary>
    Outlaw,
    /// <summary>Keeps further off and throws lit dynamite.</summary>
    Dynamiter,
    /// <summary>Leans out of a window to fire, and ducks back in.</summary>
    Window,
    /// <summary>Gallops down the street at you, firing; it takes two hits.</summary>
    Rider,
    /// <summary>The wanted man's gang, coming out to help him.</summary>
    Henchman,
}

public sealed class Outlaw
{
    public OutlawKind Kind;
    public double X, Y, ToX, ToY, Facing, T, Wait, FireIn, Walk, Stuck;
    public int Hits = 1, LastVolley;
    /// <summary>Stood still, about to fire.</summary>
    public bool Aiming;
    /// <summary>In a window: how long until he shows (or, showing, until he ducks back).</summary>
    public bool Showing;
    public bool Dead;
    public bool Hittable => Kind != OutlawKind.Window || Showing;
    public const double Radius = 10;
}

/// <summary>The wanted man, at the end of a stage.</summary>
public sealed class Boss
{
    public double X, Y, T, FireIn = 2, Hit, Dir = 1, Still;
    public int Hits, MaxHits, Burst, LastVolley;
    public bool Dead;
    public const double Radius = 18;
}

public enum ItemKind { Boots, Rifle, Rapid, Horse, Bag }

/// <summary>Something out of a broken barrel, or lying in the road: walk over it to take it.</summary>
public sealed class Item
{
    public ItemKind Kind;
    public double X, Y, Age;
}

/// <summary>A barrel in the road: three shots break it.</summary>
public sealed class Keg
{
    public int Col, Row, Hits = 3, LastVolley;
    public double X => Field.CellX(Col);
    public double Y => Field.CellY(Row);
}

public sealed class Bullet
{
    public double X, Y, Vx, Vy, Life;
    public bool Hostile;
    /// <summary>Which pull of the trigger fired it: the two bullets of one pair hit a target only once between them.</summary>
    public int Volley;
    /// <summary>A thrown knife (the boss's), drawn as one.</summary>
    public bool Knife;
}

/// <summary>A stick of dynamite in the air: from where it was thrown to where it will land and go off.</summary>
public sealed class Dynamite
{
    public double FromX, FromY, ToX, ToY, T, Time;
    public double X => FromX + (ToX - FromX) * Math.Min(1, T / Time);
    public double Y => FromY + (ToY - FromY) * Math.Min(1, T / Time);
    public double Height => Math.Sin(Math.PI * Math.Min(1, T / Time)) * 60;
}

/// <summary>A fleck of something, flying out.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Blast, Word, Puff }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
