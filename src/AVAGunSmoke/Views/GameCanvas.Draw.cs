using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAGunSmoke.Engine;
using Effect = AVAGunSmoke.Engine.Effect;

namespace AVAGunSmoke.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAGunSmoke/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);



    /// <summary>A line as a vector display draws it: a soft glow under a bright core.</summary>
    private static void Glow(DrawingContext dc, Point a, Point b, uint colour, double alpha = 1, double width = 2)
    {
        if (alpha <= 0.01) return;
        dc.DrawLine(Pen(Alpha(colour, 0.22 * alpha), width * 3.2), a, b);
        dc.DrawLine(Pen(Alpha(colour, alpha), width), a, b);
    }

    /// <summary>A closed outline of points given about (0, 0), turned and placed.</summary>
    private static void Shape(DrawingContext dc, double x, double y, double angle, double k, uint colour, double alpha, params (double X, double Y)[] pts)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        Point At((double X, double Y) p) => new(x + (p.X * c - p.Y * s) * k, y + (p.X * s + p.Y * c) * k);
        for (int i = 0; i < pts.Length; i++) Glow(dc, At(pts[i]), At(pts[(i + 1) % pts.Length]), colour, alpha);
    }

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private const uint Skin = 0xFFE0B080, Dirt = 0xFFC8A070, Plank = 0xFF8A5A30, PlankDark = 0xFF5A3A1A, Gold = 0xFFFFD040;

    private double Sy(double wy) => Field.ScreenY(wy, Engine.Camera);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF100C08), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            DrawGround(dc, g.Stage, g.Camera);
            foreach (var k in g.Kegs) DrawKeg(dc, k.X, Sy(k.Y), k.Hits);
            foreach (var i in g.Items) if (i.Age < GameEngine.ItemLife - 2 || (int)(_clock * 8) % 2 == 0) DrawItem(dc, i.Kind, i.X, Sy(i.Y), 1);
            foreach (var d in g.Sticks) dc.DrawEllipse(Brush(0x50000000), null, new Point(d.X, Sy(d.Y)), 6, 3);
            foreach (var o in g.Outlaws) DrawOutlaw(dc, o);
            if (g.Boss is { Dead: false } wanted) DrawBoss(dc, g.Stage.Theme, wanted.X, Sy(wanted.Y), wanted.T, wanted.Hit > 0, wanted.Burst > 0, 1.6);
            DrawPlayer(dc);
            foreach (var b in g.Bullets) DrawBullet(dc, b);
            foreach (var d in g.Sticks) DrawStick(dc, d.X, Sy(d.Y) - d.Height, d.T);
            foreach (var d in g.Wreckage)
            {
                double a = Math.Clamp(1 - d.Age / d.Life, 0, 1);
                dc.DrawEllipse(Brush(Alpha(d.Colour, a)), null, new Point(d.X, Sy(d.Y)), 2, 2);
            }
            if (g.Boss is { Dead: false } bb && g.Showdown) DrawBossBar(dc, bb);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 16), 26, Yellow, center: true);
    }

    // ───────────────────────────── the trail ─────────────────────────────

    private static readonly (uint Ground, uint Fleck)[] Grounds = [(Dirt, 0xFFB08A5A), (0xFFC8865A, 0xFFB0704A), (0xFFB89A70, 0xFFA08460)];

    /// <summary>The cells on the screen: the road and whatever stands on it.</summary>
    private void DrawGround(DrawingContext dc, Stage stage, double camera)
    {
        var (ground, fleck) = Grounds[stage.Theme];
        dc.FillRectangle(Brush(ground), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        // Wheel ruts down the middle of the road.
        foreach (double x in new[] { Field.Width / 2 - 70, Field.Width / 2 + 70 })
            dc.FillRectangle(Brush(Alpha(fleck, 0.6)), new Rect(x - 5, Field.Hud, 10, Field.Height - Field.Hud));
        int r0 = Field.RowOf(camera) - 1, r1 = Field.RowOf(camera + Field.Height) + 1;
        for (int r = r0; r <= r1; r++)
        for (int c = 0; c < Field.Cols; c++)
        {
            double x = c * Field.Cell, y = Field.ScreenY((r + 1) * Field.Cell, camera);
            var cell = new Rect(x, y, Field.Cell, Field.Cell);
            uint h = Hash(c, r, stage.Theme);
            char at = stage.At(c, r);
            switch (at)
            {
                case Ground.Building:
                {
                    // Roof planks, and at the edge on the street a boardwalk with its porch posts.
                    dc.FillRectangle(Brush(h % 3 == 0 ? 0xFF7A4E2A : 0xFF6A4424), cell);
                    for (int k = 1; k < 4; k++) dc.DrawLine(Pen(0x40000000, 1), new Point(x, y + k * 8), new Point(x + Field.Cell, y + k * 8));
                    bool streetRight = !Ground.Blocks(stage.At(c + 1, r)) && c < Field.Cols / 2, streetLeft = !Ground.Blocks(stage.At(c - 1, r)) && c >= Field.Cols / 2;
                    if (streetRight || streetLeft)
                    {
                        double bx = streetRight ? x + Field.Cell - 12 : x;
                        dc.FillRectangle(Brush(0xFFA07040), new Rect(bx, y, 12, Field.Cell));
                        for (int k = 0; k < 4; k++) dc.DrawLine(Pen(PlankDark, 1), new Point(bx, y + k * 8), new Point(bx + 12, y + k * 8));
                        if (r % 3 == 0) dc.FillRectangle(Brush(PlankDark), new Rect(streetRight ? x + Field.Cell - 3 : x, y + 2, 3, 6));
                        // A window in the shopfront.
                        if (r % 3 == 1)
                        {
                            double wx = streetRight ? x + Field.Cell - 22 : x + 12;
                            dc.DrawRectangle(Brush(0xFF2A2018), Pen(0xFFD0B080, 1.5), new Rect(wx, y + 6, 10, 20));
                        }
                    }
                    break;
                }
                case Ground.Rock:
                    dc.FillRectangle(Brush(0xFF9A5A3A), cell);
                    dc.DrawEllipse(Brush(0xFFAA6A48), Pen(0xFF6A3A22, 1), new Point(x + 8 + h % 16, y + 8 + (h >> 4) % 16), 9, 7);
                    dc.DrawLine(Pen(0x40FFE0C0, 2), new Point(x + 4, y + 6), new Point(x + 14, y + 4));
                    break;
                case Ground.Cactus:
                    dc.DrawEllipse(Brush(0x40000000), null, new Point(x + 20, y + 26), 10, 4);
                    dc.DrawRectangle(Brush(0xFF3A8A3A), Pen(0xFF1A4A1A, 1), new Rect(x + 12, y + 2, 8, 26), 4, 4);
                    dc.DrawRectangle(Brush(0xFF3A8A3A), Pen(0xFF1A4A1A, 1), new Rect(x + 4, y + 8, 6, 12), 3, 3);
                    dc.DrawRectangle(Brush(0xFF3A8A3A), Pen(0xFF1A4A1A, 1), new Rect(x + 22, y + 4, 6, 12), 3, 3);
                    dc.FillRectangle(Brush(0xFF3A8A3A), new Rect(x + 8, y + 16, 6, 4));
                    dc.FillRectangle(Brush(0xFF3A8A3A), new Rect(x + 18, y + 12, 6, 4));
                    break;
                case Ground.Trough:
                    dc.DrawRectangle(Brush(Plank), Pen(PlankDark, 1.5), new Rect(x + 6, y + 1, 20, 30), 2, 2);
                    dc.FillRectangle(Brush(0xFF4A7AAA), new Rect(x + 9, y + 4, 14, 24));
                    break;
                case Ground.Fence:
                    dc.DrawLine(Pen(PlankDark, 4), new Point(x, y + 12), new Point(x + Field.Cell, y + 12));
                    dc.DrawLine(Pen(PlankDark, 4), new Point(x, y + 22), new Point(x + Field.Cell, y + 22));
                    dc.FillRectangle(Brush(0xFF4A2A10), new Rect(x + 2, y + 6, 5, 22));
                    break;
                case Ground.Wagon:
                    if (stage.At(c - 1, r) != Ground.Wagon && stage.At(c, r + 1) != Ground.Wagon) DrawWagon(dc, x, y);
                    break;
                case Ground.Water:
                case Ground.Bridge:
                {
                    dc.FillRectangle(Brush(0xFF3A6A9A), cell);
                    double wave = (_clock * 30 + h % 32) % 32;
                    dc.DrawLine(Pen(0x70A0D0FF, 1.5), new Point(x + 4, y + wave * 0.9), new Point(x + 14, y + wave * 0.9));
                    if (at == Ground.Bridge)
                    {
                        dc.FillRectangle(Brush(Plank), cell);
                        for (int k = 0; k < 4; k++) dc.DrawLine(Pen(PlankDark, 1.5), new Point(x + k * 8 + 4, y), new Point(x + k * 8 + 4, y + Field.Cell));
                    }
                    if (stage.At(c, r + 1) is not (Ground.Water or Ground.Bridge)) dc.FillRectangle(Brush(0xFF8A6A40), new Rect(x, y, Field.Cell, 4));
                    if (stage.At(c, r - 1) is not (Ground.Water or Ground.Bridge)) dc.FillRectangle(Brush(0xFF8A6A40), new Rect(x, y + Field.Cell - 4, Field.Cell, 4));
                    break;
                }
                case Ground.Track:
                    for (int k = 0; k < 4; k++) dc.FillRectangle(Brush(0xFF5A3A20), new Rect(x + k * 8 + 1, y, 5, Field.Cell));
                    dc.DrawLine(Pen(0xFF8A8A8A, 3), new Point(x, y + 8), new Point(x + Field.Cell, y + 8));
                    dc.DrawLine(Pen(0xFF8A8A8A, 3), new Point(x, y + 24), new Point(x + Field.Cell, y + 24));
                    break;
                case Ground.Crate:
                    dc.DrawRectangle(Brush(0xFFA07A40), Pen(PlankDark, 1.5), new Rect(x + 3, y + 3, 26, 26));
                    dc.DrawLine(Pen(PlankDark, 1), new Point(x + 3, y + 3), new Point(x + 29, y + 29));
                    dc.DrawLine(Pen(PlankDark, 1), new Point(x + 29, y + 3), new Point(x + 3, y + 29));
                    break;
                case Ground.Hideout:
                    dc.FillRectangle(Brush(0xFF4A2E18), cell);
                    for (int k = 0; k < 4; k++) dc.DrawLine(Pen(0xFF2A1A0C, 1), new Point(x + k * 8, y), new Point(x + k * 8, y + Field.Cell));
                    break;
                case Ground.Barrel:
                    break;
                default:
                    for (int k = 0; k < 2; k++)
                    {
                        uint f = Hash(c, r, k + 11);
                        dc.FillRectangle(Brush(fleck), new Rect(x + f % 28, y + (f >> 8) % 28, 3, 2));
                    }
                    if (stage.Theme == 1 && h % 23 == 0) dc.DrawEllipse(Brush(0xFF8A9A4A), null, new Point(x + 16, y + 16), 5, 3);
                    break;
            }
        }
        // The signs on the shopfronts.
        foreach (var (row, left, sign) in stage.Signs)
        {
            double y = Field.ScreenY((row + 1) * Field.Cell, camera);
            if (y < -40 || y > Field.Height + 40) continue;
            int edge = left ? Enumerable.Range(0, Field.Cols / 2).LastOrDefault(c => stage.At(c, row) == Ground.Building) : Enumerable.Range(Field.Cols / 2, Field.Cols / 2).FirstOrDefault(c => stage.At(c, row) == Ground.Building);
            double x = left ? (edge + 1) * Field.Cell - 70 : edge * Field.Cell + 70;
            dc.DrawRectangle(Brush(0xFFE8D8B0), Pen(PlankDark, 2), new Rect(x - 62, y - 2, 124, 24), 2, 2);
            Text(dc, sign, new Point(x, y + 10), sign.Length > 10 ? 15 : 19, 0xFF3A2010, center: true, shadow: false);
        }
        // The hideout's front, with its door and a wanted poster.
        double hy = Field.ScreenY(stage.HideoutY, camera);
        if (hy > -40 && hy < Field.Height)
        {
            dc.FillRectangle(Brush(0xFF6A4424), new Rect(0, hy - 18, Field.Width, 18));
            dc.DrawRectangle(Brush(0xFF1A100A), Pen(0xFF2A1A0C, 2), new Rect(Field.Width / 2 - 40, hy - 60, 80, 60));
            foreach (double px in new[] { 220.0, Field.Width - 220 })
            {
                dc.DrawRectangle(Brush(0xFFE8D8A8), Pen(0xFF6A5030, 1.5), new Rect(px - 24, hy - 70, 48, 56));
                Text(dc, "WANTED", new Point(px, hy - 60), 13, 0xFF3A2010, center: true, shadow: false);
                Text(dc, "$" + stage.Boss.Bounty / 1000 + "K", new Point(px, hy - 26), 15, 0xFFA02010, center: true, shadow: false);
                DrawPerson(dc, px, hy - 40, Math.PI / 2, 0, BossLook(stage.Theme), 0.5, seated: true);
            }
        }
    }

    private static void DrawWagon(DrawingContext dc, double x, double y)
    {
        dc.DrawEllipse(Brush(0x40000000), null, new Point(x + 52, y + 40), 50, 16);
        foreach (var (wx, wy) in new[] { (6.0, 6.0), (6.0, 58.0), (90.0, 6.0), (90.0, 58.0) })
            dc.DrawRectangle(Brush(0xFF2A1A0C), null, new Rect(x + wx - 4, y + wy - 8, 8, 16), 2, 2);
        dc.DrawRectangle(Brush(0xFFEDE4CC), Pen(0xFF8A7A5A, 1.5), new Rect(x + 4, y + 6, 88, 52), 18, 18);
        for (int k = 1; k < 5; k++) dc.DrawLine(Pen(0x60806040, 1.5), new Point(x + 4 + k * 17.6, y + 8), new Point(x + 4 + k * 17.6, y + 56));
    }

    private static void DrawKeg(DrawingContext dc, double x, double y, int hits)
    {
        dc.DrawEllipse(Brush(0x40000000), null, new Point(x + 3, y + 5), 13, 12);
        dc.DrawEllipse(Brush(0xFF9A6A3A), Pen(PlankDark, 1.5), new Point(x, y), 13, 13);
        dc.DrawEllipse(null, Pen(0xFF4A4A4A, 2), new Point(x, y), 10, 10);
        dc.DrawEllipse(Brush(0xFF7A4A22), null, new Point(x, y), 6, 6);
        if (hits < 3) dc.DrawLine(Pen(0xFF2A1A0C, 1.5), new Point(x - 6, y - 8), new Point(x + 2, y + 2));
        if (hits < 2) dc.DrawLine(Pen(0xFF2A1A0C, 1.5), new Point(x + 7, y - 5), new Point(x - 3, y + 8));
    }

    /// <summary>What a barrel gave up: boots, a rifle, a box of bullets, a horseshoe for a horse, or a bag of gold.</summary>
    private static void DrawItem(DrawingContext dc, ItemKind kind, double x, double y, double k)
    {
        dc.DrawEllipse(Brush(0x60FFF0A0), null, new Point(x, y), 15 * k, 15 * k);
        switch (kind)
        {
            case ItemKind.Boots:
                Poly(dc, 0xFF7A4A22, 0xFF2A1A0C, 1.2, new Point(x - 6 * k, y - 10 * k), new Point(x + 1 * k, y - 10 * k), new Point(x + 1 * k, y + 2 * k), new Point(x + 9 * k, y + 4 * k), new Point(x + 9 * k, y + 9 * k), new Point(x - 6 * k, y + 9 * k));
                break;
            case ItemKind.Rifle:
                dc.DrawLine(Pen(0xFF7A4A22, 4 * k), new Point(x - 11 * k, y + 8 * k), new Point(x - 3 * k, y + 2 * k));
                dc.DrawLine(Pen(0xFF2A2A2A, 2.5 * k), new Point(x - 3 * k, y + 2 * k), new Point(x + 11 * k, y - 9 * k));
                break;
            case ItemKind.Rapid:
                dc.DrawRectangle(Brush(0xFF6A7A3A), Pen(0xFF2A2A1A, 1), new Rect(x - 10 * k, y - 3 * k, 20 * k, 10 * k));
                for (int i = 0; i < 4; i++) dc.DrawRectangle(Brush(Gold), null, new Rect(x - 8 * k + i * 5 * k, y - 10 * k, 3 * k, 8 * k), 1, 1);
                break;
            case ItemKind.Horse:
            {
                var pen = Pen(0xFF8A8A8A, 3.5 * k);
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x - 8 * k, y + 8 * k), false);
                    c.ArcTo(new Point(x + 8 * k, y + 8 * k), new Size(8 * k, 9 * k), 0, true, SweepDirection.Clockwise);
                    c.EndFigure(false);
                }
                dc.DrawGeometry(null, pen, g);
                break;
            }
            default:
                dc.DrawEllipse(Brush(0xFFC0A060), Pen(0xFF6A5030, 1.2), new Point(x, y + 2 * k), 10 * k, 9 * k);
                dc.DrawLine(Pen(0xFF6A5030, 2 * k), new Point(x - 4 * k, y - 7 * k), new Point(x + 4 * k, y - 7 * k));
                Text(dc, "$", new Point(x, y + 3 * k), 15 * k, 0xFF3A2A10, center: true, shadow: false);
                break;
        }
    }

    // ───────────────────────────── the people ─────────────────────────────

    /// <summary>Who a figure is, for how he's dressed and what he carries.</summary>
    private enum Look { Slinger, Outlaw, Dynamiter, Henchman, Knife, Dolan, Colonel }

    private static Look BossLook(int theme) => theme switch { 0 => Look.Knife, 1 => Look.Dolan, _ => Look.Colonel };

    private void DrawPlayer(DrawingContext dc)
    {
        var p = Engine.Player;
        if (!p.Alive) return;
        if (p.Safe > 0 && (int)(_clock * 10) % 2 == 0) return;
        double y = Sy(p.Y);
        if (p.Horse > 0)
        {
            DrawHorse(dc, p.X, y + 4, -Math.PI / 2, p.Walk, 0xFF8A5A30, 1.2);
            DrawPerson(dc, p.X, y - 6, -Math.PI / 2, 0, Look.Slinger, 1.25, seated: true, rifle: p.Rifle);
        }
        else DrawPerson(dc, p.X, y, -Math.PI / 2, p.Walk, Look.Slinger, 1.3, rifle: p.Rifle);
    }

    private void DrawOutlaw(DrawingContext dc, Outlaw o)
    {
        double x = o.X, y = Sy(o.Y), f = -o.Facing;
        switch (o.Kind)
        {
            case OutlawKind.Window:
            {
                // A window frame; open with him leaning out, or shut.
                bool left = x < Field.Width / 2;
                double wx = left ? x - 20 : x + 8;
                if (!o.Showing)
                {
                    dc.DrawRectangle(Brush(0xFF5A3A1A), Pen(0xFFD0B080, 1.5), new Rect(wx, y - 14, 12, 28));
                    dc.DrawLine(Pen(0xFF2A1A0C, 1), new Point(wx + 6, y - 14), new Point(wx + 6, y + 14));
                    break;
                }
                dc.DrawRectangle(Brush(0xFF1A100A), Pen(0xFFD0B080, 1.5), new Rect(wx, y - 14, 12, 28));
                DrawPerson(dc, x, y, left ? 0.6 : Math.PI - 0.6, 0, Look.Outlaw, 1.2, seated: true);
                break;
            }
            case OutlawKind.Rider:
                DrawHorse(dc, x, y + 4, Math.PI / 2, o.Walk, 0xFF3A2A1A, 1.2);
                DrawPerson(dc, x, y - 6, Math.PI / 2, 0, Look.Outlaw, 1.2, seated: true);
                break;
            case OutlawKind.Dynamiter:
                DrawPerson(dc, x, y, f, o.Walk, Look.Dynamiter, 1.3, aiming: o.Aiming);
                break;
            case OutlawKind.Henchman:
                DrawPerson(dc, x, y, f, o.Walk, Look.Henchman, 1.3, aiming: o.Aiming);
                break;
            default:
                DrawPerson(dc, x, y, f, o.Walk, Look.Outlaw, 1.3, aiming: o.Aiming);
                break;
        }
    }

    /// <summary>The wanted man, bigger than his gang, flashing when hit; the Colonel behind his hand-cranked gun.</summary>
    private void DrawBoss(DrawingContext dc, int theme, double x, double y, double t, bool hit, bool cranking, double k)
    {
        var look = BossLook(theme);
        if (look == Look.Colonel)
        {
            // The gun on its wheels, in front of him.
            dc.DrawEllipse(Brush(0x40000000), null, new Point(x, y + 30 * k / 1.6), 30, 8);
            foreach (int side in new[] { -1, 1 }) dc.DrawEllipse(Brush(0xFF3A2A1A), Pen(0xFF1A1A1A, 1.5), new Point(x + side * 18, y + 18), 8, 8);
            dc.DrawRectangle(Brush(0xFF5A5A5A), Pen(0xFF2A2A2A, 1), new Rect(x - 12, y + 8, 24, 14), 3, 3);
            for (int i = -1; i <= 1; i++) dc.DrawLine(Pen(0xFF2A2A2A, 3), new Point(x + i * 4, y + 20), new Point(x + i * 4, y + 36));
            if (cranking && (int)(_clock * 20) % 2 == 0) dc.DrawEllipse(Brush(0xC0FFE080), null, new Point(x, y + 40), 8, 6);
        }
        if (hit) using (dc.PushOpacity(0.55)) DrawPerson(dc, x, y, Math.PI / 2, t, look, k);
        else DrawPerson(dc, x, y, Math.PI / 2, t, look, k);
    }

    private void DrawBossBar(DrawingContext dc, Boss b)
    {
        double w = 300, frac = (double)b.Hits / b.MaxHits;
        dc.DrawRectangle(Brush(0xC0100800), Pen(Gold, 1.5), new Rect(Field.Width / 2 - w / 2, Field.Hud + 8, w, 14), 3, 3);
        dc.FillRectangle(Brush(0xFFD03A2A), new Rect(Field.Width / 2 - w / 2 + 2, Field.Hud + 10, (w - 4) * frac, 10));
        Text(dc, Engine.Stage.Boss.Name, new Point(Field.Width / 2, Field.Hud + 34), 20, Gold, center: true);
    }

    /// <summary>A horse from a little above: body, legs going, neck and head the way it faces, and a tail.</summary>
    private static void DrawHorse(DrawingContext dc, double x, double y, double facing, double walk, uint coat, double k)
    {
        double fx = Math.Cos(facing), fy = Math.Sin(facing), step = Math.Sin(walk * 16) * 3 * k;
        dc.DrawEllipse(Brush(0x45000000), null, new Point(x + 2, y + 12 * k), 12 * k, 14 * k);
        foreach (var (lx, ly, ph) in new[] { (-5.0, -7.0, 1.0), (5.0, -7.0, -1.0), (-5.0, 8.0, -1.0), (5.0, 8.0, 1.0) })
            dc.DrawLine(Pen(0xFF2A1A0C, 2.6 * k), new Point(x + lx * k, y + ly * k * Math.Abs(fy) + ly * 0.2 * k), new Point(x + lx * k, y + (ly + 7) * k * Math.Abs(fy) + ph * step));
        dc.DrawEllipse(Brush(coat), Pen(0xFF1A100A, 1), new Point(x, y), 8 * k, 13 * k);
        // Tail behind, head and neck in front.
        dc.DrawLine(Pen(0xFF1A100A, 3 * k), new Point(x - fx * 3 * k, y - fy * 12 * k), new Point(x - fx * 5 * k + Math.Sin(walk * 9) * 2, y - fy * 19 * k));
        var head = new Point(x + fx * 4 * k, y + fy * 17 * k);
        dc.DrawLine(Pen(coat, 6 * k), new Point(x + fx * 2 * k, y + fy * 9 * k), head);
        dc.DrawEllipse(Brush(coat), Pen(0xFF1A100A, 1), head, 4 * k, 5.5 * k);
        dc.DrawLine(Pen(0xFF1A100A, 2 * k), new Point(x, y + fy * 8 * k), new Point(head.X, head.Y - fy * 2 * k));
    }

    /// <summary>
    /// A man as an arcade sprite draws him, seen from a little above: boots and legs striding as he walks, a shirt
    /// or coat with a gun belt, arms holding his guns out the way he faces, and his head under his hat (a face when
    /// he's turned our way, the back of his head when he's turned away). <paramref name="facing"/> is an angle on
    /// the screen, so up the screen is -π/2.
    /// </summary>
    private static void DrawPerson(DrawingContext dc, double x, double y, double facing, double walk, Look look, double k, bool seated = false, bool aiming = false, bool rifle = false)
    {
        double fx = Math.Cos(facing), fy = Math.Sin(facing);
        bool away = fy < -0.35, side = Math.Abs(fx) > 0.5;
        var (shirt, trousers, vest, hat) = look switch
        {
            Look.Slinger => (0xFF4A6AA8u, 0xFF3A3A5Au, 0xFF8A5A30u, 0xFFC8A060u),
            Look.Dynamiter => (0xFFB0A080u, 0xFF4A3A2Au, 0xFF8A2A2Au, 0xFF2A2A2Au),
            Look.Henchman => (0xFF7A6A5Au, 0xFF3A2A1Au, 0xFF4A3A2Au, 0xFF5A4A3Au),
            Look.Knife => (0xFF4A6A3Au, 0xFF2A2A1Au, 0xFF2A3A1Au, 0xFF3A2A1Au),
            Look.Dolan => (0xFFE0D8C0u, 0xFF3A3A3Au, 0xFFB02A2Au, 0xFF1A1A1Au),
            Look.Colonel => (0xFF7A7A8Au, 0xFF4A4A5Au, 0xFF5A5A6Au, 0xFF5A5A6Au),
            _ => (0xFF8A6A4Au, 0xFF3A2A1Au, 0xFF2A2A2Au, 0xFF1A1A1Au),
        };
        var dark = Pen(0xC0101010, 1);
        if (!seated) dc.DrawEllipse(Brush(0x45000000), null, new Point(x + 1, y + 12 * k), 8.5 * k, 3.4 * k);
        if (!seated)
        {
            double phase = Math.Sin(walk * 13);
            foreach (int leg in new[] { -1, 1 })
            {
                double hx = x + leg * 2.6 * k, hy = y + 3 * k, footX, footY;
                if (side)
                {
                    footX = hx + Math.Sign(fx) * leg * phase * 4 * k;
                    footY = y + 11 * k - (leg * phase > 0 ? 1.2 * k : 0);
                }
                else
                {
                    footX = hx;
                    footY = y + 11 * k - Math.Max(0, leg * phase) * 2.6 * k;
                }
                dc.DrawLine(Pen(trousers, 3.4 * k), new Point(hx, hy), new Point(footX, footY));
                dc.DrawEllipse(Brush(0xFF3A2010), null, new Point(footX + (side ? fx * 1 * k : 0), footY + 0.6 * k), 2.4 * k, 1.5 * k);
            }
        }
        // His guns: a six-gun in each hand out in front, or a rifle; dynamite or knives for some.
        double gx = x + fx * 5 * k, gy = y - 1 * k + fy * 3.5 * k;
        bool pistols = look is Look.Slinger or Look.Outlaw or Look.Henchman;
        void Guns()
        {
            if (look == Look.Slinger && rifle)
            {
                dc.DrawLine(Pen(0xFF7A4A22, 3 * k), new Point(gx - fx * 5 * k, gy - fy * 5 * k + 1.5 * k), new Point(gx, gy));
                dc.DrawLine(Pen(0xFF202020, 2 * k), new Point(gx, gy), new Point(gx + fx * 13 * k, gy + fy * 13 * k));
                return;
            }
            if (!pistols) return;
            foreach (int hand in look == Look.Slinger ? new[] { -1, 1 } : new[] { 1 })
            {
                double hx = gx - fy * hand * 4 * k, hy = gy + fx * hand * 4 * k;
                dc.DrawLine(Pen(0xFF303030, 2.4 * k), new Point(hx, hy), new Point(hx + fx * (aiming ? 7 : 5) * k, hy + fy * (aiming ? 7 : 5) * k));
            }
        }
        if (away) Guns();
        // The body: shirt, an open vest or coat over it, and the gun belt.
        var body = new Rect(x - 5 * k, y - 6.5 * k, 10 * k, 10.5 * k);
        dc.DrawRectangle(Brush(shirt), dark, body, 3 * k, 3 * k);
        if (!away)
        {
            dc.FillRectangle(Brush(vest), new Rect(x - 5 * k, y - 6 * k, 3 * k, 8 * k));
            dc.FillRectangle(Brush(vest), new Rect(x + 2 * k, y - 6 * k, 3 * k, 8 * k));
        }
        else dc.FillRectangle(Brush(vest), new Rect(x - 4.5 * k, y - 6 * k, 9 * k, 8 * k));
        dc.FillRectangle(Brush(0xFF3A2410), new Rect(x - 5 * k, y + 1.4 * k, 10 * k, 1.8 * k));
        dc.FillRectangle(Brush(Gold), new Rect(x - 1 * k, y + 1.4 * k, 2 * k, 1.8 * k));
        // The arms.
        var left = new Point(x - 4.6 * k, y - 4.5 * k);
        var right = new Point(x + 4.6 * k, y - 4.5 * k);
        if (pistols || rifle)
        {
            dc.DrawLine(Pen(shirt, 2.6 * k), left, new Point(gx + fy * 4 * k, gy - fx * 4 * k));
            dc.DrawLine(Pen(shirt, 2.6 * k), right, new Point(gx - fy * 4 * k, gy + fx * 4 * k));
        }
        else if (look == Look.Dynamiter || look == Look.Dolan)
        {
            // A lit stick held up, ready to throw.
            dc.DrawLine(Pen(shirt, 2.6 * k), left, new Point(x - 5.5 * k, y + 1 * k));
            var up = new Point(x + 6 * k, y - (aiming || look == Look.Dolan ? 12 : 4) * k);
            dc.DrawLine(Pen(shirt, 2.6 * k), right, up);
            dc.DrawRectangle(Brush(0xFFC02A2A), null, new Rect(up.X - 1.2 * k, up.Y - 4 * k, 2.4 * k, 6 * k));
            dc.DrawEllipse(Brush(Gold), null, new Point(up.X, up.Y - 5 * k), 1.2 * k, 1.2 * k);
        }
        else if (look == Look.Knife)
        {
            dc.DrawLine(Pen(shirt, 2.6 * k), left, new Point(x - 7 * k, y - 1 * k));
            dc.DrawLine(Pen(shirt, 2.6 * k), right, new Point(x + 7 * k, y - 1 * k));
            foreach (int s in new[] { -1, 1 }) dc.DrawLine(Pen(0xFFD8D8E0, 1.4 * k), new Point(x + s * 7 * k, y - 1 * k), new Point(x + s * 9 * k, y + 5 * k));
        }
        else
        {
            dc.DrawLine(Pen(shirt, 2.6 * k), left, new Point(x - 5.6 * k, y + 1 * k));
            dc.DrawLine(Pen(shirt, 2.6 * k), right, new Point(x + 5.6 * k, y + 1 * k));
        }
        if (!away) Guns();
        // The head, and on it the hat.
        var head = new Point(x + fx * 0.6 * k, y - 10 * k);
        dc.DrawEllipse(Brush(Skin), dark, head, 4.2 * k, 4.2 * k);
        if (!away)
        {
            if (side) dc.DrawEllipse(Brush(0xFF101010), null, new Point(head.X + fx * 2.6 * k, head.Y + 0.4 * k), 0.8 * k, 0.8 * k);
            else foreach (int e in new[] { -1, 1 }) dc.DrawEllipse(Brush(0xFF101010), null, new Point(head.X + e * 1.6 * k + fx * k, head.Y + 0.4 * k), 0.75 * k, 0.75 * k);
            // An outlaw's bandana over his face; the Colonel's moustache.
            if (look is Look.Outlaw or Look.Henchman) Poly(dc, 0xFFB02A2A, null, 0, new Point(head.X - 4 * k, head.Y + 1.2 * k), new Point(head.X + 4 * k, head.Y + 1.2 * k), new Point(head.X, head.Y + 5 * k));
            if (look == Look.Colonel) dc.DrawLine(Pen(0xFFE0E0E0, 1.6 * k), new Point(head.X - 3 * k, head.Y + 2 * k), new Point(head.X + 3 * k, head.Y + 2 * k));
        }
        switch (look)
        {
            case Look.Dynamiter or Look.Dolan:
                // A bowler.
                dc.DrawEllipse(Brush(hat), dark, new Point(head.X, head.Y - 2.6 * k), 5.6 * k, 2.2 * k);
                dc.DrawEllipse(Brush(hat), null, new Point(head.X, head.Y - 4 * k), 3.8 * k, 3 * k);
                break;
            case Look.Colonel:
                // A cavalry hat with a plume.
                dc.DrawEllipse(Brush(hat), dark, new Point(head.X, head.Y - 2.6 * k), 7 * k, 2.4 * k);
                dc.DrawEllipse(Brush(hat), null, new Point(head.X, head.Y - 4.4 * k), 4 * k, 2.8 * k);
                dc.DrawLine(Pen(0xFFE8E8F0, 2 * k), new Point(head.X + 3 * k, head.Y - 5 * k), new Point(head.X + 7 * k, head.Y - 9 * k));
                break;
            default:
                // A wide-brimmed hat, its crown dented.
                dc.DrawEllipse(Brush(hat), dark, new Point(head.X, head.Y - 2.4 * k), 7.4 * k, 2.6 * k);
                dc.DrawEllipse(Brush(hat), null, new Point(head.X, head.Y - 4.2 * k), 4.2 * k, 3 * k);
                dc.DrawLine(Pen(Alpha(0xFF000000, 0.35), 1 * k), new Point(head.X - 3.6 * k, head.Y - 3 * k), new Point(head.X + 3.6 * k, head.Y - 3 * k));
                break;
        }
    }

    private void DrawBullet(DrawingContext dc, Bullet b)
    {
        var at = new Point(b.X, Sy(b.Y));
        if (b.Knife)
        {
            double l = Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy), ux = b.Vx / l, uy = -b.Vy / l;
            dc.DrawLine(Pen(0xFFE0E0F0, 2.4), new Point(at.X - ux * 8, at.Y - uy * 8), new Point(at.X + ux * 4, at.Y + uy * 4));
            dc.DrawLine(Pen(0xFF5A3A1A, 3), new Point(at.X - ux * 8, at.Y - uy * 8), new Point(at.X - ux * 12, at.Y - uy * 12));
            return;
        }
        dc.DrawEllipse(Brush(b.Hostile ? 0x60FF6A3Au : 0x60FFF0A0u), null, at, 5, 5);
        dc.DrawEllipse(Brush(b.Hostile ? 0xFFFF8A4Au : 0xFFFFFFC0u), null, at, 2.4, 2.4);
    }

    private static void DrawStick(DrawingContext dc, double x, double y, double t)
    {
        dc.DrawRectangle(Brush(0xFFC02A2A), Pen(0xFF5A1010, 1), new Rect(x - 2.5, y - 7, 5, 14));
        if ((int)(t * 20) % 2 == 0) dc.DrawEllipse(Brush(Gold), null, new Point(x, y - 9), 2.6, 2.6);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, Sy(e.Y));
        if (e.Kind != EffectKind.Score && e.Kind != EffectKind.Word && !InGame) return;
        switch (e.Kind)
        {
            case EffectKind.Blast:
            {
                double r = (14 + 40 * t) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFF5A4A3A, 0.5 * (1 - t))), null, new Point(c.X, c.Y - t * 20), r * 1.1, r);
                dc.DrawEllipse(Brush(Alpha(0xFFFF8A2A, 1 - t)), null, c, r * 0.8, r * 0.7);
                dc.DrawEllipse(Brush(Alpha(0xFFFFF0A0, 1 - t)), null, c, r * 0.4, r * 0.35);
                break;
            }
            case EffectKind.Puff:
                dc.DrawEllipse(Brush(Alpha(0xFFE0D0B0, 0.7 * (1 - t))), null, c, 3 + 6 * t, 3 + 6 * t);
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 22, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 30, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xE81A120A), new Rect(0, 0, Field.Width, Field.Hud));
        dc.FillRectangle(Brush(0xFF8A5A30), new Rect(0, Field.Hud - 2, Field.Width, 2));
        Text(dc, "SCORE", new Point(16, 14), 20, 0xFFC0A070);
        Text(dc, g.Score.ToString("D6"), new Point(90, 14), 30, Yellow);
        Text(dc, "HI", new Point(Field.Width - 140, 14), 20, 0xFFC0A070, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 14), 28, 0xFFC0A070, right: true);
        if (!InGame) return;
        Text(dc, $"STAGE {g.Wave}  {g.Stage.Name}", new Point(512, 14), 24, 0xFFE8C080, center: true);
        int spare = Math.Max(0, g.Lives - (g.Player.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawPerson(dc, 24 + i * 22, 38, Math.PI / 2, 0, Look.Slinger, 0.6);
        // What he's carrying.
        var p = g.Player;
        double ix = Field.Width - 30;
        void Show(ItemKind kind) { DrawItem(dc, kind, ix, 40, 0.7); ix -= 30; }
        for (int i = 0; i < p.Horse; i++) Show(ItemKind.Horse);
        if (p.Rapid) Show(ItemKind.Rapid);
        if (p.Rifle) Show(ItemKind.Rifle);
        for (int i = 0; i < p.Boots; i++) Show(ItemKind.Boots);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 260;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.DrawRectangle(Brush(0xF0E8D8B0), Pen(0xFF6A4424, 3), new Rect(cx - w / 2, cy - 46, w, h), 4, 4);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? 0xFFA01A10 : 0xFF3A2010, center: true, shadow: false);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 24, 0xFF8A2A10, center: true, shadow: false);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF3A2416), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        // A desert sunset behind the title: bands, a sun, mesas.
        uint[] bands = [0xFF5A2A20, 0xFF8A3A24, 0xFFB85A2A, 0xFFD8843A];
        for (int i = 0; i < bands.Length; i++) dc.FillRectangle(Brush(bands[i]), new Rect(0, Field.Hud + i * 40, Field.Width, 40));
        dc.DrawEllipse(Brush(0xFFFFD070), null, new Point(cx, 232), 42, 42);
        Poly(dc, 0xFF4A2418, null, 0, new Point(0, 230), new Point(120, 230), new Point(150, 180), new Point(290, 180), new Point(320, 230), new Point(700, 230), new Point(730, 170), new Point(900, 170), new Point(930, 230), new Point(1024, 230), new Point(1024, 300), new Point(0, 300));
        string title = "AVA GUNSMOKE";
        for (int i = 0; i < title.Length; i++)
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 26 + 26 + i * 52, 112), 104, i % 2 == 0 ? 0xFFFFE8B0 : 0xFFFFC870, center: true);
        Text(dc, "AFTER THE 1985 ARCADE CLASSIC", new Point(cx, 176), 28, 0xFFFFE0C0, center: true);
        // The gunslinger, walking up the trail with both guns up.
        DrawPerson(dc, cx + Math.Sin(_clock * 0.8) * 120, 272, -Math.PI / 2, _clock, Look.Slinger, 2.6);
        Text(dc, "WALK THE TRAIL TO THE WANTED MAN'S HIDEOUT AND COLLECT THE BOUNTY", new Point(cx, 330), 24, 0xFFFFE8C8, center: true);
        (string Name, string Points, Action<double> Draw)[] legend =
        [
            ("OUTLAW", "100", x => DrawPerson(dc, x, 396, Math.PI / 2, _clock, Look.Outlaw, 2.2)),
            ("AT A WINDOW", "200", x => { dc.DrawRectangle(Brush(0xFF1A100A), Pen(0xFFD0B080, 2), new Rect(x - 30, 368, 22, 52)); DrawPerson(dc, x, 400, 0.6, 0, Look.Outlaw, 2, seated: true); }),
            ("DYNAMITER", "300", x => DrawPerson(dc, x, 396, Math.PI / 2, _clock, Look.Dynamiter, 2.2, aiming: true)),
            ("RIDER", "500", x => { DrawHorse(dc, x, 404, Math.PI / 2, _clock, 0xFF3A2A1A, 1.6); DrawPerson(dc, x, 392, Math.PI / 2, 0, Look.Outlaw, 1.6, seated: true); }),
            ("BARREL", "50", x => DrawKeg(dc, x, 398, 3)),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double lx = cx - 360 + i * 180;
            legend[i].Draw(lx);
            Text(dc, legend[i].Name, new Point(lx, 450), 20, 0xFFE0C090, center: true);
            Text(dc, legend[i].Points, new Point(lx, 474), 22, Yellow, center: true);
        }
        // What's in the barrels.
        var items = new[] { (ItemKind.Boots, "BOOTS"), (ItemKind.Rifle, "RIFLE"), (ItemKind.Rapid, "QUICK DRAW"), (ItemKind.Horse, "HORSE"), (ItemKind.Bag, "GOLD") };
        for (int i = 0; i < items.Length; i++)
        {
            double lx = cx - 300 + i * 150;
            DrawItem(dc, items[i].Item1, lx, 516, 1.1);
            Text(dc, items[i].Item2, new Point(lx, 546), 18, 0xFFE0C090, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER OR 1 TO PLAY", new Point(cx, 598), 46, Yellow, center: true);
        Text(dc, "ARROWS: WALK    Z / X / C: FIRE UP-LEFT / UP / UP-RIGHT (SPACE FIRES UP TOO)", new Point(cx, 656), 22, 0xFFE0C090, center: true);
        Text(dc, $"P: PAUSE    M: SOUND    EXTRA LIFE EVERY {GameEngine.ExtraLifeEvery}    GAMEPAD: STICK, X / Y / B", new Point(cx, 686), 22, 0xFFE0C090, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF2A1A10), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "FASTEST GUNS", new Point(cx, 140), 68, 0xFFFFD070, center: true);
        Text(dc, "RANK NAME    SCORE   STAGE", new Point(cx, 204), 30, 0xFFC0A070, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF2A1A10), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, 0xFFC0A070, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : 0xFFC0A070), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
