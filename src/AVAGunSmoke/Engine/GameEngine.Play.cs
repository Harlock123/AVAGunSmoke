namespace AVAGunSmoke.Engine;

public sealed partial class GameEngine
{
    public const double WalkSpeed = 140, BootsSpeed = 25, HorseSpeed = 20, BulletSpeed = 520, ShotLife = 0.5, RifleLife = 0.8;
    public const double FireGap = 0.22, RapidGap = 0.13, PairSpread = 6;
    public const int MaxPairs = 3, RapidPairs = 4, MaxBoots = 2, HorseHits = 3;
    public const double DynamiteBlast = 40, StartSeconds = 2.6, DyingSeconds = 2.4, SafeSeconds = 2.5, ClearSeconds = 4, ItemLife = 12;
    public const int ExtraLifeEvery = 30000, BarrelPoints = 50, BagPoints = 1000;

    /// <summary>The stage: 1 for the first.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Stage Stage { get; private set; } = Stage.Build(1);
    /// <summary>The world y at the bottom of the screen.</summary>
    public double Camera { get; private set; }
    public Gunslinger Player { get; } = new();
    public List<Outlaw> Outlaws { get; } = [];
    public List<Keg> Kegs { get; } = [];
    public List<Item> Items { get; } = [];
    public List<Bullet> Bullets { get; } = [];
    public List<Dynamite> Sticks { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public Boss? Boss { get; private set; }
    public SlingerPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public int Bonus { get; private set; }
    /// <summary>At the hideout: the showdown with the wanted man has begun.</summary>
    public bool Showdown { get; private set; }
    /// <summary>For tests: the outlaws stand still and hold their fire, and no more come.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra, _spawnIndex, _fireTurn, _volley;
    private double _ambientIn, _scrolled, _henchIn, _clearIn;
    private List<Spawn> _spawns = [];

    public double AutoScroll => 26 + 2 * (Wave - 1);
    public double EnemyShotSpeed => Math.Min(380, 260 + 20 * (Wave - 1));
    public double OutlawSpeed => Math.Min(125, 80 + 8 * (Wave - 1));
    public double OutlawFireGap => Math.Max(0.8, 1.6 - 0.15 * (Wave - 1));
    public double AimError => Math.Max(0.03, 0.14 - 0.025 * (Wave - 1));
    public double SpawnSpacing => Math.Max(80, 140 - 10 * (Wave - 1));
    public double AmbientEvery => Math.Max(1.8, 3.4 - 0.25 * (Wave - 1));
    public int MaxOutlaws => Math.Min(11, 6 + Wave);
    public double ViewTop => Camera + Field.Height - Field.Hud;
    public double Speed => WalkSpeed + Player.Boots * BootsSpeed + (Player.Horse > 0 ? HorseSpeed : 0);
    public double Range => BulletSpeed * (Player.Rifle ? RifleLife : ShotLife);

    public static int PointsFor(OutlawKind kind) => kind switch
    {
        OutlawKind.Window => 200, OutlawKind.Dynamiter => 300, OutlawKind.Rider => 500, _ => 100,
    };

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = ExtraLifeEvery;
        Wreckage.Clear();
        Stage = Stage.Build(1);
        Camera = 0;
        Strip();
        PlacePlayer(Field.Width / 2, 120);
        SetPhase(Phase.Start);
    }

    /// <summary>Back to just the two guns, as after losing a life.</summary>
    private void Strip()
    {
        var p = Player;
        p.Boots = 0;
        p.Rifle = p.Rapid = false;
        p.Horse = 0;
    }

    private void PlacePlayer(double x, double y)
    {
        var p = Player;
        p.X = x;
        p.Y = y;
        Stage.Push(ref p.X, ref p.Y, Gunslinger.Radius);
        p.Alive = true;
        p.Safe = SafeSeconds;
        p.FireIn = 0;
    }

    /// <summary>A stage: its ground built, the player at the bottom, the outlaws waiting in it.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        Stage = Stage.Build(wave);
        Camera = 0;
        Outlaws.Clear();
        Bullets.Clear();
        Sticks.Clear();
        Items.Clear();
        Kegs.Clear();
        foreach (var (c, r) in Stage.Barrels) Kegs.Add(new Keg { Col = c, Row = r });
        _spawns = Stage.Spawns.OrderBy(s => s.Y).ToList();
        _spawnIndex = 0;
        _ambientIn = 3;
        _scrolled = 0;
        Showdown = false;
        Boss = null;
        PlacePlayer(Field.Width / 2, 120);
        SetPhase(Phase.Start);
        var b = Stage.Boss;
        Say($"STAGE {wave} - {Stage.Name}", $"WANTED: {b.Name}  ${b.Bounty}", StartSeconds + 0.3);
        Play(Sfx.StageStart);
        Music(true);
    }

    private void Simulate(double dt, Controls c)
    {
        MoveDebris(dt);
        foreach (var i in Items) i.Age += dt;
        switch (Phase)
        {
            case Phase.Start:
                Walk(dt, c);
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                Walk(dt, c);
                Fire(c);
                Scroll(dt);
                Reinforce(dt);
                MoveOutlaws(dt);
                MoveBoss(dt);
                MoveBullets(dt);
                MoveSticks(dt);
                Contact();
                Tidy();
                if (Boss is { Dead: true } && (_clearIn -= dt) <= 0) Clear();
                break;
            case Phase.Dying:
                MoveOutlaws(dt);
                MoveBoss(dt);
                MoveBullets(dt);
                MoveSticks(dt);
                Tidy();
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                    SetPhase(Phase.Play);
                }
                break;
            case Phase.Clear:
                Walk(dt, c);
                MoveBullets(dt);
                if (PhaseTime >= 1.2 && PhaseTime - dt < 1.2 && Bonus > 0)
                {
                    Score += Bonus;
                    CheckExtra();
                }
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the gunslinger ─────────────────────────────

    /// <summary>One step of walking: eight ways, out of anything solid, kept on the screen.</summary>
    public static void Step(Stage stage, Gunslinger p, Controls c, double camera, double speed, double dt)
    {
        if (c.Dx == 0 && c.Dy == 0) return;
        double k = c.Dx != 0 && c.Dy != 0 ? Math.Sqrt(0.5) : 1;
        double x = p.X + Math.Sign(c.Dx) * speed * k * dt, y = p.Y + Math.Sign(c.Dy) * speed * k * dt;
        stage.Push(ref x, ref y, Gunslinger.Radius);
        p.X = Math.Clamp(x, Gunslinger.Radius, Field.Width - Gunslinger.Radius);
        p.Y = Math.Clamp(y, camera + Gunslinger.Radius + 4, camera + Field.Height - Field.Hud - Gunslinger.Radius);
        if (c.Dx != 0) p.Lean = Math.Sign(c.Dx);
        p.Walk += dt;
    }

    private void Walk(double dt, Controls c)
    {
        var p = Player;
        if (!p.Alive) return;
        p.Safe = Math.Max(0, p.Safe - dt);
        p.FireIn -= dt;
        Step(Stage, p, c, Camera, Speed, dt);
    }

    /// <summary>The way each trigger fires: up and to the left, straight up, up and to the right.</summary>
    public static (double X, double Y) Aim(int trigger) => trigger switch
    {
        0 => (-Math.Sqrt(0.5), Math.Sqrt(0.5)),
        2 => (Math.Sqrt(0.5), Math.Sqrt(0.5)),
        _ => (0, 1),
    };

    /// <summary>Both guns at once, the way of whichever trigger is held (taking turns, if more than one is).</summary>
    private void Fire(Controls c)
    {
        var p = Player;
        if (!c.Firing || p.FireIn > 0 || !p.Alive) return;
        if (Bullets.Count(b => !b.Hostile) >= 2 * (p.Rapid ? RapidPairs : MaxPairs)) return;
        var held = new List<int>();
        if (c.Left) held.Add(0);
        if (c.Up) held.Add(1);
        if (c.Right) held.Add(2);
        int trigger = held[_fireTurn++ % held.Count];
        p.FireIn = p.Rapid ? RapidGap : FireGap;
        var (ax, ay) = Aim(trigger);
        double life = p.Rifle ? RifleLife : ShotLife;
        _volley++;
        foreach (int side in new[] { -1, 1 })
        {
            double ox = side * PairSpread * ay, oy = -side * PairSpread * ax;
            Bullets.Add(new Bullet { X = p.X + ox + ax * 10, Y = p.Y + oy + ay * 10, Vx = ax * BulletSpeed, Vy = ay * BulletSpeed, Life = life, Volley = _volley });
        }
        Play(Sfx.Fire);
    }

    /// <summary>
    /// The screen creeps up the trail on its own (unless he's hanging back at the bottom), and follows him if he
    /// goes ahead; it stops at the hideout, where the showdown begins.
    /// </summary>
    private void Scroll(double dt)
    {
        double was = Camera, want = Player.Y - (Field.Height - Field.Hud) * 0.5;
        if (Player.Y - Camera > 70) Camera += AutoScroll * dt;
        if (want > Camera) Camera = want;
        Camera = Math.Min(Camera, Stage.CameraEnd);
        _scrolled += Camera - was;
    }

    /// <summary>Back in where the last one fell, a little further down the screen, safe for a moment, with just his guns.</summary>
    private void Respawn()
    {
        double x = Player.X, y = Math.Max(Camera + 110, Math.Min(Player.Y, Camera + 200));
        Strip();
        PlacePlayer(x, y);
        Bullets.RemoveAll(b => b.Hostile);
        Sticks.Clear();
    }

    // ───────────────────────────── the outlaws ─────────────────────────────

    /// <summary>
    /// The outlaws waiting in the map come on as the screen reaches them; more come in from the top and sides all
    /// the while; and at the hideout, the wanted man comes out with his gang.
    /// </summary>
    private void Reinforce(double dt)
    {
        while (_spawnIndex < _spawns.Count && _spawns[_spawnIndex].Y <= ViewTop + 40)
        {
            var s = _spawns[_spawnIndex++];
            if (!Calm) Bring(s);
        }
        if (Calm) return;
        if (!Showdown && Camera >= Stage.CameraEnd - 0.5)
        {
            Showdown = true;
            var w = Stage.Boss;
            Boss = new Boss { X = Field.Width / 2, Y = Stage.HideoutY - 30, Hits = w.Hits, MaxHits = w.Hits };
            _henchIn = 3;
            Say($"{w.Name}", $"WANTED {w.Crime} - ${w.Bounty}", 2.6);
            Play(Sfx.Showdown);
        }
        if (Showdown)
        {
            // His gang, from the sides, while he stands.
            if (Boss is { Dead: false } && (_henchIn -= dt) <= 0)
            {
                _henchIn = Math.Max(2, 4 - 0.3 * (Wave - 1));
                if (Outlaws.Count(o => o.Kind == OutlawKind.Henchman) < 3)
                {
                    bool left = _rng.Next(2) == 0;
                    var o = new Outlaw { Kind = OutlawKind.Henchman, X = left ? 80 : Field.Width - 80, Y = Stage.HideoutY - 20, FireIn = OutlawFireGap };
                    Choose(o);
                    Outlaws.Add(o);
                }
            }
            return;
        }
        bool due = (_ambientIn -= dt) <= 0;
        if (_scrolled >= SpawnSpacing) { _scrolled -= SpawnSpacing; due = true; }
        if (!due) return;
        _ambientIn = AmbientEvery * (0.7 + _rng.NextDouble() * 0.6);
        if (Outlaws.Count(s => s.Kind is OutlawKind.Outlaw or OutlawKind.Dynamiter) >= MaxOutlaws) return;
        var kind = _rng.Next(4) == 0 ? SpawnKind.Dynamiter : SpawnKind.Outlaw;
        for (int i = 0; i < 10; i++)
        {
            double x = 160 + _rng.NextDouble() * (Field.Width - 320), y = ViewTop + 24;
            if (Stage.Blocks(Field.ColOf(x), Field.RowOf(y))) continue;
            Bring(new Spawn(kind, x, y));
            break;
        }
    }

    private void Bring(Spawn s)
    {
        switch (s.Kind)
        {
            case SpawnKind.Outlaw or SpawnKind.Dynamiter:
            {
                var o = new Outlaw { Kind = s.Kind == SpawnKind.Dynamiter ? OutlawKind.Dynamiter : OutlawKind.Outlaw, X = s.X, Y = s.Y, Facing = -Math.PI / 2, FireIn = OutlawFireGap * (0.5 + _rng.NextDouble()) };
                Choose(o);
                Outlaws.Add(o);
                break;
            }
            case SpawnKind.Window:
            {
                // In the doorway of a window, just out into the street from the shopfront.
                double x = s.X < Field.Width / 2 ? s.X + 8 : s.X - 8;
                Outlaws.Add(new Outlaw { Kind = OutlawKind.Window, X = x, Y = s.Y, Wait = 0.8 + _rng.NextDouble() * 1.5, Facing = -Math.PI / 2 });
                break;
            }
            case SpawnKind.Rider:
            {
                double x = Field.Width / 2 + (_rng.NextDouble() - 0.5) * 360;
                Outlaws.Add(new Outlaw { Kind = OutlawKind.Rider, X = x, Y = ViewTop + 30, Hits = 2, Facing = -Math.PI / 2, FireIn = 0.8 });
                Play(Sfx.Gallop);
                break;
            }
            case SpawnKind.Bag:
                Items.Add(new Item { Kind = ItemKind.Bag, X = s.X, Y = s.Y });
                break;
        }
    }

    /// <summary>Where an outlaw walks to next: somewhere above the player and to one side, that he can stand on.</summary>
    private void Choose(Outlaw s)
    {
        var p = Player;
        double above = s.Kind == OutlawKind.Dynamiter ? 220 + _rng.NextDouble() * 120 : 130 + _rng.NextDouble() * 170;
        for (int i = 0; i < 10; i++)
        {
            double x = Math.Clamp(p.X + (_rng.NextDouble() - 0.5) * 460, 20, Field.Width - 20);
            double y = Math.Clamp(p.Y + above, Camera + 60, Math.Min(ViewTop - 20, Showdown ? Stage.HideoutY - 20 : double.MaxValue));
            if (Stage.Blocks(Field.ColOf(x), Field.RowOf(y))) continue;
            s.ToX = x;
            s.ToY = y;
            s.T = 0;
            return;
        }
        s.ToX = s.X;
        s.ToY = s.Y;
    }

    private void MoveOutlaws(double dt)
    {
        var p = Player;
        foreach (var s in Outlaws.ToList())
        {
            if (Calm || s.Dead) continue;
            s.T += dt;
            s.FireIn -= dt;
            switch (s.Kind)
            {
                case OutlawKind.Window:
                    // Ducked in, then out to fire once, then in again.
                    if ((s.Wait -= dt) > 0)
                    {
                        if (s.Showing && s.Wait < 0.9 && !s.Aiming && Sees(s)) { s.Aiming = true; Shoot(s); }
                        break;
                    }
                    s.Showing = !s.Showing;
                    s.Aiming = false;
                    s.Wait = s.Showing ? 1.5 : 1.5 + _rng.NextDouble() * 2;
                    break;
                case OutlawKind.Rider:
                {
                    // Down the street, drifting towards the player's side of it, firing as it comes.
                    double speed = 200, x = s.X + Math.Clamp(p.X - s.X, -40, 40) * dt, y = s.Y - speed * dt;
                    Stage.Push(ref x, ref y, Outlaw.Radius + 4);
                    s.X = x;
                    s.Y = y;
                    s.Walk += dt;
                    if (Sees(s) && s.FireIn <= 0 && p.Y < s.Y) { Shoot(s); s.FireIn = 1.1; }
                    break;
                }
                default:
                    if (s.Aiming)
                    {
                        s.Facing = Math.Atan2(p.Y - s.Y, p.X - s.X);
                        if ((s.Wait -= dt) > 0) break;
                        s.Aiming = false;
                        if (Sees(s))
                        {
                            if (s.Kind == OutlawKind.Dynamiter) Lob(s.X, s.Y, p.X, p.Y, 48);
                            else Shoot(s);
                            s.FireIn = OutlawFireGap * (s.Kind == OutlawKind.Dynamiter ? 1.6 : 1);
                        }
                        Choose(s);
                        break;
                    }
                    double before = Field.Distance(s.X, s.Y, s.ToX, s.ToY);
                    Stroll(s, s.ToX, s.ToY, OutlawSpeed, dt);
                    double after = Field.Distance(s.X, s.Y, s.ToX, s.ToY);
                    s.Stuck = before - after < OutlawSpeed * dt * 0.3 ? s.Stuck + dt : 0;
                    if (after < 6 || s.T > 2.6 || s.Stuck > 0.3)
                    {
                        if (s.FireIn <= 0 && p.Alive && Phase == Phase.Play) { s.Aiming = true; s.Wait = 0.45; }
                        else Choose(s);
                    }
                    break;
            }
        }
    }

    private void Stroll(Outlaw s, double tx, double ty, double speed, double dt)
    {
        double dx = tx - s.X, dy = ty - s.Y, d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 1) return;
        double step = Math.Min(d, speed * dt);
        double x = s.X + dx / d * step, y = s.Y + dy / d * step;
        Stage.Push(ref x, ref y, Outlaw.Radius);
        s.X = x;
        s.Y = y;
        s.Facing = Math.Atan2(dy, dx);
        s.Walk += dt;
    }

    /// <summary>Whether an outlaw (on the screen) has a clear shot at the player.</summary>
    private bool Sees(Outlaw s) =>
        Player.Alive && Player.Safe <= 0 && Phase == Phase.Play && s.Y < ViewTop + 10 && s.Y > Camera && s.X > 0 && s.X < Field.Width
        && Stage.Clear(s.X, s.Y, Player.X, Player.Y);

    private void Shoot(Outlaw s) => Shot(s.X, s.Y, Math.Atan2(Player.Y - s.Y, Player.X - s.X) + (_rng.NextDouble() - 0.5) * 2 * AimError, out s.Facing);

    private void Shot(double x, double y, double a, out double facing, bool knife = false, double speed = 0)
    {
        facing = a;
        double v = speed > 0 ? speed : EnemyShotSpeed;
        Bullets.Add(new Bullet { X = x + Math.Cos(a) * 12, Y = y + Math.Sin(a) * 12, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Life = 2.4, Hostile = true, Knife = knife });
        Play(knife ? Sfx.Throw : Sfx.EnemyFire);
    }

    /// <summary>A stick of dynamite thrown at a point, give or take.</summary>
    private void Lob(double x, double y, double tx, double ty, double spread)
    {
        tx += (_rng.NextDouble() - 0.5) * spread;
        ty += (_rng.NextDouble() - 0.5) * spread;
        Sticks.Add(new Dynamite { FromX = x, FromY = y, ToX = tx, ToY = ty, Time = Math.Clamp(Field.Distance(x, y, tx, ty) / 300, 0.6, 1.3) });
        Play(Sfx.Throw);
    }

    /// <summary>
    /// The wanted man: back and forth in front of his hideout, each with his own way of fighting: knives thrown in a
    /// fan, dynamite lobbed two at a time, or a hand-cranked gun sweeping the street.
    /// </summary>
    private void MoveBoss(double dt)
    {
        if (Boss is not { } b || b.Dead || Calm) return;
        b.T += dt;
        b.Hit = Math.Max(0, b.Hit - dt);
        double speed = Stage.Theme switch { 0 => 110, 1 => 90, _ => 70 };
        // He stands still a moment each time he attacks: the time to get under him.
        b.Still = Math.Max(0, b.Still - dt);
        if (b.Still <= 0 && b.Burst == 0) b.X += b.Dir * speed * dt;
        if (b.X < 180 || b.X > Field.Width - 180) b.Dir = -b.Dir;
        b.X = Math.Clamp(b.X, 180, Field.Width - 180);
        var p = Player;
        if (!p.Alive || p.Safe > 0 || Phase != Phase.Play) return;
        double a = Math.Atan2(p.Y - b.Y, p.X - b.X);
        if (b.Burst > 0)
        {
            // The gun's sweep, a shot at a time across the street.
            if ((b.FireIn -= dt) > 0) return;
            b.FireIn = 0.11;
            if (--b.Burst == 0) b.FireIn = Math.Max(1.6, 2.4 - 0.05 * (Wave - 1));
            Shot(b.X, b.Y - 10, a + (b.Burst - 4) * 0.12 * b.Dir, out _);
            return;
        }
        if ((b.FireIn -= dt) > 0) return;
        b.Still = 0.8;
        switch (Stage.Theme)
        {
            case 0:
                foreach (double off in new[] { -0.25, 0, 0.25 }) Shot(b.X, b.Y, a + off, out _, knife: true, speed: 270);
                b.FireIn = Math.Max(0.9, 1.4 - 0.05 * (Wave - 1));
                break;
            case 1:
                Lob(b.X, b.Y, p.X - 30, p.Y, 30);
                Lob(b.X, b.Y, p.X + 30, p.Y, 30);
                Shot(b.X, b.Y, a, out _);
                b.FireIn = Math.Max(1.2, 1.8 - 0.05 * (Wave - 1));
                break;
            default:
                b.Burst = 9;
                b.FireIn = 0;
                break;
        }
    }

    // ───────────────────────────── bullets, dynamite, and what they hit ─────────────────────────────

    private void MoveBullets(double dt)
    {
        foreach (var b in Bullets)
        {
            for (int k = 0; k < 2 && b.Life > 0; k++)
            {
                b.X += b.Vx * dt / 2;
                b.Y += b.Vy * dt / 2;
                if (b.X < -10 || b.X > Field.Width + 10 || b.Y < Camera - 10 || b.Y > ViewTop + 10) { b.Life = 0; break; }
                char at = Stage.AtPoint(b.X, b.Y);
                if (Ground.Stops(at))
                {
                    b.Life = 0;
                    if (at == Ground.Barrel && !b.Hostile) HitKeg(Field.ColOf(b.X), Field.RowOf(b.Y), b.Volley);
                    else Puff(b.X, b.Y);
                    break;
                }
                if (b.Hostile) HitPlayer(b);
                else HitOutlaw(b);
            }
            b.Life -= dt;
        }
        Bullets.RemoveAll(b => b.Life <= 0);
    }

    private void HitOutlaw(Bullet b)
    {
        foreach (var s in Outlaws)
        {
            if (s.Dead || !s.Hittable || Field.Distance(b.X, b.Y, s.X, s.Y) > Outlaw.Radius + (s.Kind == OutlawKind.Rider ? 6 : 2)) continue;
            b.Life = 0;
            if (s.LastVolley == b.Volley) return;
            s.LastVolley = b.Volley;
            if (--s.Hits > 0) { Puff(s.X, s.Y); Play(Sfx.Ricochet); return; }
            Down(s);
            return;
        }
        if (Boss is { Dead: false } boss && Field.Distance(b.X, b.Y, boss.X, boss.Y) < Boss.Radius + 2)
        {
            b.Life = 0;
            if (boss.LastVolley == b.Volley) return;
            boss.LastVolley = b.Volley;
            boss.Hit = 0.15;
            Play(Sfx.BossHit);
            if (--boss.Hits > 0) return;
            boss.Dead = true;
            Award(Stage.Boss.Bounty, boss.X, boss.Y);
            Sparks(boss.X, boss.Y, 0xFFE0C090, 20, 1.4);
            Effects.Add(new Effect { Kind = EffectKind.Word, X = boss.X, Y = boss.Y + 40, Text = $"BOUNTY ${Stage.Boss.Bounty}", Life = 2, Colour = 0xFFFFE14A });
            Play(Sfx.BossDown);
            foreach (var o in Outlaws) o.Dead = true;
            foreach (var x in Bullets.Where(x => x.Hostile)) x.Life = 0;
            Sticks.Clear();
            _clearIn = 1.5;
        }
    }

    private void Down(Outlaw s)
    {
        s.Dead = true;
        Award(PointsFor(s.Kind), s.X, s.Y);
        Sparks(s.X, s.Y, 0xFFB08A60, 6, 0.5);
        Play(Sfx.OutlawDown);
    }

    /// <summary>A shot into a barrel: the third breaks it, and out may come something useful.</summary>
    private void HitKeg(int col, int row, int volley)
    {
        var k = Kegs.FirstOrDefault(k => k.Col == col && k.Row == row);
        if (k == null || k.LastVolley == volley) return;
        k.LastVolley = volley;
        Puff(k.X, k.Y);
        if (--k.Hits > 0) { Play(Sfx.Ricochet); return; }
        Kegs.Remove(k);
        Stage.Rows[row][col] = Ground.Open;
        Award(BarrelPoints, k.X, k.Y);
        Sparks(k.X, k.Y, 0xFF8A5A2A, 10, 0.7);
        Play(Sfx.BarrelBreak);
        int roll = _rng.Next(100);
        ItemKind? kind = roll switch { < 14 => ItemKind.Boots, < 26 => ItemKind.Rifle, < 42 => ItemKind.Rapid, < 50 => ItemKind.Horse, < 72 => ItemKind.Bag, _ => null };
        if (kind is { } got) Items.Add(new Item { Kind = got, X = k.X, Y = k.Y });
    }

    private void HitPlayer(Bullet b)
    {
        var p = Player;
        if (!p.Alive || Field.Distance(b.X, b.Y, p.X, p.Y) > Gunslinger.Radius + (p.Horse > 0 ? 4 : 0)) return;
        b.Life = 0;
        Hurt(b.Knife ? "knifed" : "shot");
    }

    /// <summary>Hit: the horse takes it if he has one; otherwise the life goes.</summary>
    private void Hurt(string cause)
    {
        var p = Player;
        if (p.Safe > 0 || !p.Alive || Phase != Phase.Play) return;
        if (p.Horse > 0)
        {
            p.Horse--;
            p.Safe = 1.2;
            Effects.Add(new Effect { Kind = EffectKind.Word, X = p.X, Y = p.Y + 30, Text = p.Horse > 0 ? "WHOA!" : "HORSE LOST", Life = 1, Colour = 0xFFFFB04A });
            Play(Sfx.Gallop);
            return;
        }
        Kill(cause);
    }

    private void MoveSticks(double dt)
    {
        foreach (var d in Sticks)
        {
            d.T += dt;
            if (d.T < d.Time) continue;
            Blast(d.ToX, d.ToY, 0.9);
            if (Field.Distance(d.ToX, d.ToY, Player.X, Player.Y) < DynamiteBlast) Hurt("dynamite");
        }
        Sticks.RemoveAll(d => d.T >= d.Time);
    }

    /// <summary>Running into an outlaw (or a galloping horse) is the end; whatever's lying in the road is picked up.</summary>
    private void Contact()
    {
        var p = Player;
        if (!p.Alive) return;
        foreach (var i in Items.Where(i => Field.Distance(i.X, i.Y, p.X, p.Y) < 24).ToList())
        {
            Items.Remove(i);
            Take(i);
        }
        if (p.Safe > 0) return;
        foreach (var s in Outlaws)
        {
            if (s.Dead || s.Kind == OutlawKind.Window) continue;
            double reach = Outlaw.Radius + Gunslinger.Radius - 2 + (s.Kind == OutlawKind.Rider ? 8 : 0);
            if (Field.Distance(s.X, s.Y, p.X, p.Y) > reach) continue;
            Hurt(s.Kind == OutlawKind.Rider ? "trampled" : "run into");
            return;
        }
    }

    private void Take(Item i)
    {
        var p = Player;
        string word;
        switch (i.Kind)
        {
            case ItemKind.Boots: p.Boots = Math.Min(MaxBoots, p.Boots + 1); word = "BOOTS"; break;
            case ItemKind.Rifle: p.Rifle = true; word = "RIFLE"; break;
            case ItemKind.Rapid: p.Rapid = true; word = "QUICK DRAW"; break;
            case ItemKind.Horse: p.Horse = HorseHits; word = "HORSE"; Play(Sfx.Gallop); break;
            default: Award(BagPoints, i.X, i.Y); word = "GOLD"; break;
        }
        Effects.Add(new Effect { Kind = EffectKind.Word, X = i.X, Y = i.Y + 26, Text = word, Life = 1, Colour = 0xFFFFE14A });
        Play(Sfx.PowerUp);
    }

    /// <summary>The dead, whatever has fallen off the bottom of the screen, and items left too long go.</summary>
    private void Tidy()
    {
        Outlaws.RemoveAll(s => s.Dead || s.Y < Camera - 40);
        Items.RemoveAll(i => i.Y < Camera - 30 || i.Age > ItemLife);
    }

    private void Kill(string cause)
    {
        var p = Player;
        Cause = cause;
        p.Alive = false;
        Lives--;
        Sparks(p.X, p.Y, 0xFFE0C090, 14, 1.2);
        Play(Sfx.Death);
        SetPhase(Phase.Dying);
        if (Lives <= 0) Say("GAME OVER", null, DyingSeconds);
    }

    private void Blast(double x, double y, double size)
    {
        Effects.Add(new Effect { Kind = EffectKind.Blast, X = x, Y = y, Life = 0.55, Size = size });
        Sparks(x, y, 0xFFFFB04A, 8, 0.6);
        Play(Sfx.Blast);
    }

    private void Puff(double x, double y) => Effects.Add(new Effect { Kind = EffectKind.Puff, X = x, Y = y, Life = 0.3 });

    private void Clear()
    {
        Bonus = 1000 * Wave;
        Bullets.RemoveAll(b => b.Hostile);
        Music(false);
        Play(Sfx.StageClear);
        Say($"{Stage.Boss.Name} BROUGHT IN", $"STAGE BONUS {Bonus}", ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Award(int points, double x, double y)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y + 16, Text = points.ToString(), Life = 0.7, Colour = 0xFFFFE14A });
        CheckExtra();
    }

    private void CheckExtra()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    private void Sparks(double x, double y, uint colour, int n, double life)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 30 + _rng.NextDouble() * 150;
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Life = life * (0.5 + _rng.NextDouble() * 0.5), Colour = colour });
        }
    }

    private void MoveDebris(double dt)
    {
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            d.Vx *= 1 - 2 * dt;
            d.Vy *= 1 - 2 * dt;
        }
        Wreckage.RemoveAll(d => d.Age >= d.Life);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a stage, past its name.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Player.Safe = 0;
        Message = SubMessage = null;
    }

    internal Outlaw PutOutlaw(OutlawKind kind, double x, double y)
    {
        var s = new Outlaw { Kind = kind, X = x, Y = y, ToX = x, ToY = y, FireIn = 0, Hits = kind == OutlawKind.Rider ? 2 : 1, Showing = kind == OutlawKind.Window, Wait = 5 };
        Outlaws.Add(s);
        return s;
    }

    internal void PutPlayer(double x, double y)
    {
        Player.X = x;
        Player.Y = y;
        Player.Alive = true;
        Player.Safe = 0;
    }

    /// <summary>The screen moved straight to a point up the map (the player with it).</summary>
    internal void MoveCamera(double camera)
    {
        Camera = Math.Min(camera, Stage.CameraEnd);
        Player.Y = Camera + 120;
        Stage.Push(ref Player.X, ref Player.Y, Gunslinger.Radius);
        while (_spawnIndex < _spawns.Count && _spawns[_spawnIndex].Y < Camera) _spawnIndex++;
    }

    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
