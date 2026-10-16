namespace AVAGunSmoke.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). The guns only fire three ways (up and to the left,
/// straight up, up and to the right), so to shoot something it gets below it: in the same column, or on a diagonal,
/// and pulls that trigger. Outlaws below it it can't shoot, so it backs away from them to get them above it. It
/// shoots open barrels in its way and picks up what comes out; with nothing to shoot it walks on up the trail round
/// what's in the way. Before it does anything it looks at where the bullets, dynamite and riders will be, and if
/// that would get it killed, it takes whichever way of moving keeps it clearest (still firing).
/// </summary>
public sealed class SlingerPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    private const double Lined = 12, Close = 90, Engage = 200;

    private int _tick, _stuck, _wander, _lining, _giveUp, _dodging;
    private Controls _dodge;
    private double _lastX, _lastY;
    private (int Dx, int Dy) _wanderWay;

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var p = g.Player;
        if (g.Phase is not (Phase.Play or Phase.Start) || !p.Alive) { Doing = "waiting"; return default; }
        var want = Plan(g);
        if ((want.Dx != 0 || want.Dy != 0) && Field.Distance(p.X, p.Y, _lastX, _lastY) < 0.5) { if (++_stuck > 25) { _stuck = 0; _wander = 20; _wanderWay = Field.Ways[(_tick / 7) % 8]; } }
        else _stuck = 0;
        _lastX = p.X;
        _lastY = p.Y;
        if (_wander > 0) { _wander--; want = want with { Dx = _wanderWay.Dx, Dy = _wanderWay.Dy }; Doing = "unsticking"; }
        return Safe(g, want);
    }

    private Controls Plan(GameEngine g)
    {
        var p = g.Player;
        var targets = Targets(g).ToList();
        // Anything lined up with a gun: fire that way (and keep moving as planned).
        Controls fire = default;
        foreach (var (x, y, _, _) in targets)
            if (Trigger(g, p, x, y) is { } t) { fire = new Controls(Left: t == 0, Up: t == 1, Right: t == 2); break; }
        var move = Move(g, targets);
        return move with { Left = fire.Left, Up = fire.Up, Right = fire.Right };
    }

    private Controls Move(GameEngine g, List<(double X, double Y, double Cost, bool Barrel)> targets)
    {
        var p = g.Player;
        var near = _giveUp > 0 ? [] : targets.Where(t => !t.Barrel && (g.Showdown || Field.Distance(p.X, p.Y, t.X, t.Y) < Engage)).ToList();
        if (_giveUp > 0) _giveUp--;
        if (near.Count > 0 && ++_lining > 200) { _lining = 0; _giveUp = 90; }
        if (near.Count == 0) _lining = 0;
        if (near.Count > 0 && _giveUp == 0)
        {
            var (x, y, _, _) = near[0];
            double dx = x - p.X, dy = y - p.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < Close || dy < 40)
            {
                // Too close, or not above it: back down the screen (and away to the side) to get it in front.
                Doing = "backing off";
                return new Controls(Math.Abs(dx) < 60 ? -Math.Sign(dx == 0 ? 1 : dx) : 0, -1);
            }
            // Under it, or on a diagonal from it: whichever is the shorter step across; and closer, if it's out of range.
            double col = Math.Abs(dx), diag = Math.Abs(Math.Abs(dx) - dy);
            int up = dy > g.Range * 0.8 ? 1 : 0;
            Doing = "lining up";
            if (col <= diag) return new Controls(col > 4 ? Math.Sign(dx) : 0, up);
            return Math.Abs(dx) > dy ? new Controls(Math.Sign(dx), up) : new Controls(-Math.Sign(dx == 0 ? 1 : dx), up);
        }
        if (g.Items.Where(i => i.Y > g.Camera + 30 && i.Y < g.ViewTop - 30).OrderBy(i => Field.Distance(p.X, p.Y, i.X, i.Y)).FirstOrDefault() is { } item)
        {
            Doing = "to an item";
            return Towards(g, item.X, item.Y);
        }
        // A barrel above, in its column: shoot it open on the way.
        if (targets.FirstOrDefault(t => t.Barrel && t.Y > p.Y + 40 && t.Y < p.Y + g.Range * 0.9) is { Cost: < 1e9 } keg && Math.Abs(keg.X - p.X) < 120)
        {
            Doing = "at a barrel";
            return new Controls(Math.Abs(keg.X - p.X) > 6 ? Math.Sign(keg.X - p.X) : 0, 0);
        }
        if (g.Showdown)
        {
            Doing = "holding";
            double by = g.Boss is { } boss ? boss.Y - g.Range * 0.7 : g.Camera + 170;
            return Towards(g, Field.Width / 2 + Math.Sin(_tick / 80.0) * 160, by);
        }
        Doing = "advancing";
        return Towards(g, Field.Width / 2, g.ViewTop - 40);
    }

    /// <summary>Everything there is to shoot, nearest (and most dangerous) first; barrels last.</summary>
    private static IEnumerable<(double X, double Y, double Cost, bool Barrel)> Targets(GameEngine g)
    {
        var p = g.Player;
        var all = new List<(double X, double Y, double Cost, bool Barrel)>();
        foreach (var s in g.Outlaws)
        {
            if (s.Dead || !s.Hittable || s.Y > g.ViewTop - 8 || s.Y < g.Camera + 4 || s.X < 4 || s.X > Field.Width - 4) continue;
            double d = Field.Distance(p.X, p.Y, s.X, s.Y);
            all.Add((s.X, s.Y, d * (s.Kind == OutlawKind.Rider ? 0.5 : s.Kind == OutlawKind.Dynamiter ? 0.8 : 1), false));
        }
        if (g.Boss is { Dead: false } b) all.Add((b.X, b.Y, Field.Distance(p.X, p.Y, b.X, b.Y) * 0.7, false));
        foreach (var k in g.Kegs)
            if (k.Y > g.Camera && k.Y < g.ViewTop) all.Add((k.X, k.Y, 1e6 + Field.Distance(p.X, p.Y, k.X, k.Y), true));
        return all.OrderBy(t => t.Cost);
    }

    /// <summary>Which trigger would hit something at (x, y) now, if any: near enough on the line, in range, nothing in the way.</summary>
    private static int? Trigger(GameEngine g, Gunslinger p, double x, double y)
    {
        double dx = x - p.X, dy = y - p.Y;
        for (int t = 0; t < 3; t++)
        {
            var (ux, uy) = GameEngine.Aim(t);
            double along = dx * ux + dy * uy, off = Math.Abs(dx * uy - dy * ux);
            if (along > 10 && along < g.Range * 0.95 && off < Lined && g.Stage.Clear(p.X, p.Y, x - ux * 14, y - uy * 14)) return t;
        }
        return null;
    }

    /// <summary>The way to walk to get towards a point, round what's in the way, over the open ground on the screen.</summary>
    private static Controls Towards(GameEngine g, double tx, double ty)
    {
        var p = g.Player;
        var stage = g.Stage;
        int r0 = Field.RowOf(g.Camera) + 1, r1 = Field.RowOf(g.ViewTop) - 1;
        int sc = Field.ColOf(p.X), sr = Field.RowOf(p.Y), gc = Math.Clamp(Field.ColOf(tx), 0, Field.Cols - 1), gr = Math.Clamp(Field.RowOf(ty), r0, r1);
        var from = new Dictionary<(int, int), (int, int)>();
        var queue = new Queue<(int, int)>();
        from[(sc, sr)] = (sc, sr);
        queue.Enqueue((sc, sr));
        (int C, int R) best = (sc, sr);
        double bestD = double.MaxValue;
        while (queue.Count > 0)
        {
            var (c, r) = queue.Dequeue();
            double d = Math.Abs(c - gc) + Math.Abs(r - gr);
            if (d < bestD) { bestD = d; best = (c, r); }
            foreach (var (dc, dr) in Field.Ways)
            {
                int nc = c + dc, nr = r + dr;
                if (nc < 0 || nc >= Field.Cols || nr < r0 || nr > r1 || from.ContainsKey((nc, nr)) || stage.Blocks(nc, nr)) continue;
                if (dc != 0 && dr != 0 && (stage.Blocks(c + dc, r) || stage.Blocks(c, r + dr))) continue;
                from[(nc, nr)] = (c, r);
                queue.Enqueue((nc, nr));
            }
        }
        var path = new List<(int C, int R)>();
        for (var at = best; at != (sc, sr); at = from[at]) path.Add(at);
        path.Reverse();
        double wx = best == (sc, sr) ? tx : Field.CellX(path[0].C), wy = best == (sc, sr) ? ty : Field.CellY(path[0].R);
        foreach (var (c, r) in path.Take(5))
        {
            double x = Field.CellX(c), y = Field.CellY(r);
            if (!Open(stage, p.X, p.Y, x, y)) break;
            (wx, wy) = (x, y);
        }
        if (best == (sc, sr) && Field.Distance(p.X, p.Y, tx, ty) < 6) return default;
        double vx = wx - p.X, vy = wy - p.Y, len = Math.Max(1, Math.Sqrt(vx * vx + vy * vy));
        return new Controls(Math.Abs(vx) > len * 0.38 ? Math.Sign(vx) : 0, Math.Abs(vy) > len * 0.38 ? Math.Sign(vy) : 0);
    }

    private static bool Open(Stage stage, double ax, double ay, double bx, double by)
    {
        double d = Field.Distance(ax, ay, bx, by);
        int n = Math.Max(1, (int)(d / 6));
        double a = Math.Atan2(by - ay, bx - ax), px = -Math.Sin(a) * (Gunslinger.Radius + 1), py = Math.Cos(a) * (Gunslinger.Radius + 1);
        for (int i = 1; i <= n; i++)
        {
            double t = (double)i / n, x = ax + (bx - ax) * t, y = ay + (by - ay) * t;
            if (Ground.Blocks(stage.AtPoint(x + px, y + py)) || Ground.Blocks(stage.AtPoint(x - px, y - py))) return false;
        }
        return true;
    }

    /// <summary>What it wanted, unless that walks into something: then of the nine ways to move, the clearest (still firing).</summary>
    private Controls Safe(GameEngine g, Controls want)
    {
        // It reacts like a person: it only notices trouble every few frames, and then sticks with its dodge a moment.
        if (_dodging > 0) { _dodging--; Doing = "dodging"; return _dodge with { Left = want.Left, Up = want.Up, Right = want.Right }; }
        if (_tick % 4 != 0) return want;
        double here = Clearance(g, want);
        if (here > 14) return want;
        var best = want;
        double most = here;
        foreach (var (dx, dy) in Field.Ways.Append((0, 0)))
        {
            var c = want with { Dx = dx, Dy = dy };
            double clear = Clearance(g, c);
            if (clear > most + 2) { most = clear; best = c; }
        }
        if (best != want) { Doing = "dodging"; _dodge = best; _dodging = 6; }
        return best;
    }

    /// <summary>How close the nearest danger comes to it over the next half second or so, moving like this.</summary>
    private static double Clearance(GameEngine g, Controls c)
    {
        var p = g.Player;
        var me = new Gunslinger { X = p.X, Y = p.Y };
        double least = double.MaxValue, speed = g.Speed;
        var shots = g.Bullets.Where(b => b.Hostile && Field.Distance(b.X, b.Y, p.X, p.Y) < 320).Select(b => (b.X, b.Y, b.Vx, b.Vy)).ToArray();
        var sticks = g.Sticks.ToList();
        var men = g.Outlaws.Where(s => !s.Dead && s.Kind != OutlawKind.Window && Field.Distance(s.X, s.Y, p.X, p.Y) < 160).ToList();
        if (shots.Length == 0 && sticks.Count == 0 && men.Count == 0) return double.MaxValue;
        for (int t = 1; t <= 36; t++)
        {
            double dt = t * Field.Step;
            GameEngine.Step(g.Stage, me, c, g.Camera, speed, Field.Step);
            for (int i = 0; i < shots.Length; i++)
            {
                var (x, y, vx, vy) = shots[i];
                if (double.IsNaN(x)) continue;
                double sx = x + vx * dt, sy = y + vy * dt;
                if (Ground.Stops(g.Stage.AtPoint(sx, sy))) { shots[i] = (double.NaN, 0, 0, 0); continue; }
                least = Math.Min(least, Field.Distance(sx, sy, me.X, me.Y) - Gunslinger.Radius);
            }
            foreach (var n in sticks)
                if (Math.Abs(n.Time - n.T - dt) < Field.Step * 1.5) least = Math.Min(least, Field.Distance(n.ToX, n.ToY, me.X, me.Y) - GameEngine.DynamiteBlast);
            foreach (var s in men)
            {
                double sy = s.Kind == OutlawKind.Rider ? s.Y - 200 * dt : s.Y;
                least = Math.Min(least, Field.Distance(s.X, sy, me.X, me.Y) - Outlaw.Radius - Gunslinger.Radius - (s.Kind == OutlawKind.Rider ? 8 : 0));
            }
        }
        return least;
    }
}
