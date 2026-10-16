using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAGunSmoke.Engine;
using AVAGunSmoke.Views;
using Controls = AVAGunSmoke.Engine.Controls;

namespace AVAGunSmoke.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set GUNSMOKE_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avagunsmoke-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("GUNSMOKE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAGunSmoke-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Left = false, Up = false, Right = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 186500, 6);
        g.HighScores.Insert("AVA", 92200, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A stage, and the computer at work in it until something worth showing.</summary>
    private static void Fight(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 200)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(30);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Player.Alive && g.Player.Safe <= 0 && busy(g), seconds));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_town() => Fight(1, "town", g => g.Camera > 300 && g.Outlaws.Count(o => o.Kind == OutlawKind.Window && o.Showing) >= 1 && g.Outlaws.Count >= 4 && g.Bullets.Count(b => !b.Hostile) >= 2);

    [AvaloniaFact]
    public void A_barrel_gives_something_up() => Fight(1, "barrel", g => g.Items.Any(i => i.Kind != ItemKind.Bag && i.Y > g.Camera + 80 && i.Y < g.ViewTop - 80) && g.Outlaws.Count >= 2);

    [AvaloniaFact]
    public void A_rider() => Fight(1, "rider", g => g.Outlaws.Any(o => o.Kind == OutlawKind.Rider && o.Y < g.ViewTop - 80 && o.Y > g.Player.Y + 120));

    [AvaloniaFact]
    public void The_canyon() => Fight(2, "canyon", g => g.Camera > 800 && g.Outlaws.Count >= 3 && g.Sticks.Count >= 1, seconds: 300);

    [AvaloniaFact]
    public void The_railroad() => Fight(3, "railroad", g => g.Stage.AtPoint(512, g.Player.Y + 160) == Ground.Track && g.Outlaws.Count >= 2, seconds: 300);

    [AvaloniaFact]
    public void The_showdown() => Fight(1, "showdown", g => g.Boss is { Dead: false } b && b.Hits < b.MaxHits - 2 && g.Bullets.Count(x => x.Knife) >= 2, seconds: 400);
}
