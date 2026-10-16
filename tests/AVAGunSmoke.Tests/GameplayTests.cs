using AVAGunSmoke.Engine;

namespace AVAGunSmoke.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avagunsmoke-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Stage 1 in play with the first stretch of the map cleared to open road and nobody about: calm (whoever is put
    /// there stands still and holds his fire) unless <paramref name="calm"/> is false.
    /// </summary>
    private static GameEngine Game(bool calm = true, int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = calm };
        g.Go(wave);
        for (int r = 0; r < 40; r++) Array.Fill(g.Stage.Rows[r], Ground.Open);
        g.Outlaws.Clear();
        g.Kegs.Clear();
        g.Items.Clear();
        g.PutPlayer(512, 200);
        return g;
    }

    private static readonly Controls Up = new(Up: true), Left = new(Left: true), Right = new(Right: true);

    private static void Set(GameEngine g, int col, int row, char what) => g.Stage.Rows[row][col] = what;

    // ───────────────────────────── the trail ─────────────────────────────

    [Fact]
    public void Every_stage_is_a_way_up_its_trail_to_the_wanted_mans_hideout()
    {
        for (int n = 1; n <= 6; n++)
        {
            var s = Stage.Build(n);
            Assert.All(s.Rows, row => Assert.Equal(Field.Cols, row.Length));
            Assert.Equal(Ground.Hideout, s.At(16, s.HideoutRow));
            Assert.NotEmpty(s.Barrels);
            // On foot, from the start to the ground in front of the hideout.
            var seen = new HashSet<(int, int)> { (16, 3) };
            var queue = new Queue<(int C, int R)>([(16, 3)]);
            while (queue.Count > 0)
            {
                var (c, r) = queue.Dequeue();
                foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    if (!s.Blocks(c + dc, r + dr) && r + dr >= 0 && seen.Add((c + dc, r + dr))) queue.Enqueue((c + dc, r + dr));
            }
            Assert.Contains((16, s.HideoutRow - 1), seen);
            if (s.Theme != 1)
            {
                Assert.NotEmpty(s.Signs);
                Assert.Contains(s.Spawns, x => x.Kind == SpawnKind.Window);
            }
        }
        Assert.Equal(new string(Stage.Build(2).Rows.SelectMany(r => r).ToArray()), new string(Stage.Build(2).Rows.SelectMany(r => r).ToArray()));
        Assert.Equal(["DUST CREEK", "RATTLESNAKE CANYON", "IRON HORSE JUNCTION", "DUST CREEK"], Enumerable.Range(1, 4).Select(n => Stage.Build(n).Name));
        // The wanted men come back the second time round worth more, and harder to bring in.
        Assert.True(Stage.Build(4).Boss.Bounty > Stage.Build(1).Boss.Bounty && Stage.Build(4).Boss.Hits > Stage.Build(1).Boss.Hits);
    }

    // ───────────────────────────── walking and shooting ─────────────────────────────

    [Fact]
    public void He_walks_eight_ways_quicker_in_boots_and_quicker_still_on_a_horse()
    {
        var g = Game();
        g.Run(30, new Controls(Dx: 1));
        Assert.Equal(512 + GameEngine.WalkSpeed / 2, g.Player.X, 1);
        double x = g.Player.X, y = g.Player.Y;
        g.Run(30, new Controls(Dx: -1, Dy: 1));
        Assert.Equal(GameEngine.WalkSpeed / 2, Field.Distance(x, y, g.Player.X, g.Player.Y), 1);
        g.Player.Boots = 2;
        g.Player.Horse = GameEngine.HorseHits;
        Assert.Equal(GameEngine.WalkSpeed + 2 * GameEngine.BootsSpeed + GameEngine.HorseSpeed, g.Speed);
    }

    [Fact]
    public void Shopfronts_and_water_stop_him_but_a_bridge_does_not()
    {
        var g = Game();
        Set(g, 16, 9, Ground.Building);
        g.PutPlayer(Field.CellX(16), 200);
        g.Run(60, new Controls(Dy: 1));
        Assert.Equal(9 * Field.Cell - Gunslinger.Radius, g.Player.Y, 0);
        Array.Fill(g.Stage.Rows[12], Ground.Water);
        Set(g, 20, 12, Ground.Bridge);
        g.PutPlayer(Field.CellX(18), 300);
        g.Run(90, new Controls(Dy: 1));
        Assert.True(g.Player.Y < 12 * Field.Cell);
        g.PutPlayer(Field.CellX(20), 300);
        g.Run(90, new Controls(Dy: 1));
        Assert.True(g.Player.Y > 13 * Field.Cell);
    }

    [Fact]
    public void Each_trigger_fires_both_guns_its_own_way()
    {
        foreach (var (c, way) in new[] { (Left, 0), (Up, 1), (Right, 2) })
        {
            var g = Game();
            g.Run(1, c);
            Assert.Equal(2, g.Bullets.Count);
            var (ax, ay) = GameEngine.Aim(way);
            Assert.All(g.Bullets, b => Assert.Equal((ax * GameEngine.BulletSpeed, ay * GameEngine.BulletSpeed), (b.Vx, b.Vy)));
            Assert.Equal(2 * GameEngine.PairSpread, Field.Distance(g.Bullets[0].X, g.Bullets[0].Y, g.Bullets[1].X, g.Bullets[1].Y), 3);
        }
    }

    [Fact]
    public void So_many_shots_at_once_more_with_quick_draw_and_further_with_the_rifle()
    {
        int Most(GameEngine g)
        {
            int most = 0;
            for (int i = 0; i < 60; i++) { g.Run(1, Up); most = Math.Max(most, g.Bullets.Count); }
            return most;
        }
        var g = Game();
        g.PutPlayer(512, 120);
        Assert.Equal(2 * GameEngine.MaxPairs, Most(g));
        var h = Game();
        h.PutPlayer(512, 120);
        h.Player.Rapid = true;
        Assert.Equal(2 * GameEngine.RapidPairs, Most(h));
        var k = Game();
        double plain = k.Range;
        k.Player.Rifle = true;
        Assert.Equal(GameEngine.BulletSpeed * GameEngine.RifleLife, k.Range, 3);
        Assert.True(k.Range > plain);
    }

    [Fact]
    public void A_barrel_takes_three_shots_and_leaves_open_road()
    {
        var g = Game();
        Set(g, 16, 10, Ground.Barrel);
        g.Kegs.Add(new Keg { Col = 16, Row = 10 });
        g.PutPlayer(Field.CellX(16), 200);
        for (int i = 0; i < 2; i++) { g.Run(1, Up); g.Idle(30); }
        Assert.Single(g.Kegs);
        Assert.True(g.Stage.Blocks(16, 10));
        g.Run(1, Up);
        g.Idle(30);
        Assert.Empty(g.Kegs);
        Assert.False(g.Stage.Blocks(16, 10));
        Assert.Equal(GameEngine.BarrelPoints, g.Score);
    }

    [Fact]
    public void Walking_over_what_comes_out_of_a_barrel_takes_it()
    {
        var g = Game();
        foreach (var (kind, at) in new[] { (ItemKind.Boots, 240.0), (ItemKind.Rifle, 280.0), (ItemKind.Rapid, 320.0), (ItemKind.Horse, 360.0), (ItemKind.Bag, 400.0) })
            g.Items.Add(new Item { Kind = kind, X = 512, Y = at });
        g.Run(80, new Controls(Dy: 1));
        Assert.Empty(g.Items);
        var p = g.Player;
        Assert.Equal((1, true, true, GameEngine.HorseHits), (p.Boots, p.Rifle, p.Rapid, p.Horse));
        Assert.Equal(GameEngine.BagPoints, g.Score);
    }

    // ───────────────────────────── the outlaws ─────────────────────────────

    [Fact]
    public void An_outlaw_stops_draws_and_fires_and_a_bullet_costs_a_life_and_everything_carried()
    {
        var g = Game(calm: false);
        g.SetLives(3);
        g.Player.Rifle = g.Player.Rapid = true;
        g.Player.Boots = 2;
        g.PutOutlaw(OutlawKind.Outlaw, 512, 480);
        int t = 0;
        while (g.Phase == Phase.Play && t++ < 600) g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("shot", g.Cause);
        Assert.Equal(2, g.Lives);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.True(g.Player.Alive && g.Player.Safe > 0);
        Assert.Equal((0, false, false), (g.Player.Boots, g.Player.Rifle, g.Player.Rapid));
    }

    [Fact]
    public void A_horse_takes_three_hits_for_him()
    {
        var g = Game(calm: false);
        g.SetLives(3);
        g.Player.Horse = GameEngine.HorseHits;
        for (int i = 0; i < GameEngine.HorseHits; i++)
        {
            g.Player.Safe = 0;
            g.Bullets.Add(new Bullet { X = 512, Y = 230, Vy = -300, Life = 1, Hostile = true });
            g.Idle(20);
        }
        Assert.Equal(0, g.Player.Horse);
        Assert.Equal(3, g.Lives);
        Assert.True(g.Player.Alive);
        g.Player.Safe = 0;
        g.Bullets.Add(new Bullet { X = 512, Y = 230, Vy = -300, Life = 1, Hostile = true });
        g.Idle(20);
        Assert.Equal(2, g.Lives);
    }

    [Fact]
    public void A_gunman_at_a_window_can_only_be_hit_while_he_leans_out()
    {
        var g = Game();
        var o = g.PutOutlaw(OutlawKind.Window, 512, 400);
        o.Showing = false;
        g.Run(1, Up);
        g.Idle(40);
        Assert.False(o.Dead);
        o.Showing = true;
        g.Run(1, Up);
        g.Idle(40);
        Assert.True(o.Dead);
        Assert.Equal(GameEngine.PointsFor(OutlawKind.Window), g.Score);
        // Live, he ducks in and out, and fires while he's out.
        var h = Game(calm: false);
        var w = h.PutOutlaw(OutlawKind.Window, 300, 420);
        w.Showing = false;
        w.Wait = 0.2;
        bool showed = false;
        for (int i = 0; i < 300 && !h.Bullets.Any(b => b.Hostile); i++) { h.Idle(1); showed |= w.Showing; }
        Assert.True(showed);
        Assert.Contains(h.Bullets, b => b.Hostile);
    }

    [Fact]
    public void A_rider_gallops_down_takes_two_hits_and_tramples_whoever_is_in_the_way()
    {
        var g = Game();
        var r = g.PutOutlaw(OutlawKind.Rider, 512, 420);
        g.Run(1, Up);
        g.Idle(40);
        Assert.False(r.Dead);
        g.Run(1, Up);
        g.Idle(40);
        Assert.Equal(GameEngine.PointsFor(OutlawKind.Rider), g.Score);
        var h = Game(calm: false);
        h.PutOutlaw(OutlawKind.Rider, 512, 500).FireIn = 99;
        int t = 0;
        while (h.Phase == Phase.Play && t++ < 300) h.Idle(1);
        Assert.Equal("trampled", h.Cause);
    }

    [Fact]
    public void Dynamite_is_lobbed_and_its_blast_kills()
    {
        var g = Game(calm: false);
        g.PutOutlaw(OutlawKind.Dynamiter, 512, 470);
        int t = 0;
        while (g.Sticks.Count == 0 && t++ < 600) g.Idle(1);
        var d = Assert.Single(g.Sticks);
        Assert.InRange(Field.Distance(d.ToX, d.ToY, g.Player.X, g.Player.Y), 0, 40);
        while (g.Phase == Phase.Play && t++ < 900) g.Idle(1);
        Assert.Equal("dynamite", g.Cause);
    }

    // ───────────────────────────── the screen, the showdown, and the whole game ─────────────────────────────

    [Fact]
    public void The_screen_creeps_up_on_its_own_and_follows_him_but_never_back()
    {
        var g = Game();
        g.Idle(60);
        Assert.Equal(g.AutoScroll, g.Camera, 0);
        g.Run(240, new Controls(Dy: 1));
        double camera = g.Camera;
        Assert.True(camera > 200);
        g.Run(240, new Controls(Dy: -1));
        Assert.Equal(g.Camera + Gunslinger.Radius + 4, g.Player.Y, 1);
        // Hanging back at the bottom holds it still.
        double held = g.Camera;
        g.Idle(60);
        Assert.Equal(held, g.Camera);
    }

    [Fact]
    public void The_outlaws_in_the_map_come_on_as_the_screen_reaches_them()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        var first = g.Stage.Spawns.Where(s => s.Kind is SpawnKind.Outlaw && s.Y > g.ViewTop + 100).OrderBy(s => s.Y).First();
        g.Player.Safe = 1000;
        for (int i = 0; i < 60 * 60 && g.ViewTop + 40 < first.Y; i++) g.Run(1, new Controls(Dy: 1));
        g.Run(1, new Controls(Dy: 1));
        Assert.Contains(g.Outlaws, s => s.Kind == OutlawKind.Outlaw && Field.Distance(s.X, s.Y, first.X, first.Y) < 20);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void At_the_hideout_the_wanted_man_comes_out_fights_his_way_and_pays_his_bounty(int stage)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(stage);
        g.SetLives(99);
        g.MoveCamera(g.Stage.CameraEnd);
        g.Outlaws.Clear();
        g.Player.Safe = 0;
        g.PutPlayer(512, g.Stage.HideoutY - 260);
        g.Idle(2);
        Assert.True(g.Showdown);
        var b = g.Boss!;
        Assert.Equal(g.Stage.Boss.Hits, b.Hits);
        // His own way of fighting.
        Func<bool> attacked = stage switch
        {
            1 => () => g.Bullets.Count(x => x.Knife) == 3,
            2 => () => g.Sticks.Count == 2,
            _ => () => b.Burst > 0,
        };
        for (int i = 0; i < 400 && g.Phase == Phase.Play && !attacked(); i++)
        {
            g.Outlaws.Clear();
            g.Idle(1);
        }
        Assert.True(attacked());
        // Brought in, shot by shot.
        g.Player.Safe = 1000;
        int score = g.Score;
        for (int i = 0; i < 60 * 60 && !b.Dead; i++)
        {
            g.Outlaws.Clear();
            g.PutPlayer(b.X, b.Y - 200);
            g.Player.Safe = 1000;
            g.Run(1, Up);
        }
        Assert.True(b.Dead);
        Assert.True(g.Score >= score + g.Stage.Boss.Bounty);
        g.Idle(120);
        Assert.Equal(Phase.Clear, g.Phase);
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(stage + 1, g.Wave);
        Assert.Equal(0, g.Camera);
    }

    [Fact]
    public void An_extra_life_every_30000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.ExtraLifeEvery - 50);
        g.PutOutlaw(OutlawKind.Outlaw, 512, 400);
        g.Run(1, Up);
        g.Idle(40);
        Assert.Equal(lives + 1, g.Lives);
    }

    [Fact]
    public void The_last_life_ends_the_game_and_a_high_score_asks_for_initials()
    {
        var g = Game();
        g.SetLives(1);
        g.SetScore(99999);
        g.PutOutlaw(OutlawKind.Outlaw, 512, 214);
        g.Idle(1);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "TEX") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("TEX", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_and_the_demos_tour_the_stages()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        var names = new List<string>();
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            names.Add(g.Stage.Name);
            for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
        Assert.Equal(["DUST CREEK", "RATTLESNAKE CANYON"], names);
    }

    [Fact]
    public void Enter_starts_a_game()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal((1, 3), (g.Wave, g.Lives));
    }

    [Fact]
    public void The_computer_player_brings_in_the_first_wanted_man()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        g.SetLives(99);
        for (int i = 0; i < 60 * 300 && g.Wave < 2 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 2, $"still on stage {g.Wave}, at {g.Camera:0} of {g.Stage.CameraEnd:0}, boss {g.Boss?.Hits}");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(2);
            for (int i = 0; i < 60 * 30; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Player.X:0.00} {g.Player.Y:0.00} {g.Camera:0.00} {g.Outlaws.Count} {g.Phase}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Game(calm: false);
        g.Run(30, new Controls(Dy: 1));
        double y = g.Player.Y;
        g.Pause();
        g.Idle(120);
        Assert.Equal(y, g.Player.Y);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
