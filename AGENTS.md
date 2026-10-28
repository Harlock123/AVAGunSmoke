# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A Wild West shoot-'em-up in the style of Capcom's 1985 arcade game Gun.Smoke, in C# on .NET 10 and Avalonia 12,
drawn through Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM).
Everything in it is original: the graphics are drawn in code, and the sounds and tunes are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAGunSmoke.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAGunSmoke.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVAGunSmoke          # play it; --no-audio for silence
GUNSMOKE_SHOTS=$PWD/Images dotnet test tests/AVAGunSmoke.Tests --filter ScreenshotTests   # refresh the README's pictures
GUNSMOKE_PROBE=1 dotnet test tests/AVAGunSmoke.Tests --filter PilotProbe --logger "console;verbosity=detailed"   # tuning probe
python3 tools/make_sounds.py                  # rebuild src/AVAGunSmoke/Sounds
```

`GUNSMOKE_PROBE=lazy` runs the probe with a player that just walks up the street firing straight ahead.
`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAGunSmoke/Engine/`: the game, with no Avalonia drawing in it.
  - `Entities.cs` has `Controls`, the `Field` (measures and the 1/60 s `Step`), the `Stage` builder, the wanted
    men, and the gunslinger, outlaws, bosses, items, kegs, bullets and effects.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `GameEngine.Play.cs` plays a stage: the walk up the trail, the pickups and the showdown with the boss.
  - `SlingerPilot.cs` is the computer player, used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAGunSmoke/Views/`: `GameCanvas` draws the fixed 1024x768 picture, scaled to the window, and runs the
  60 Hz timer; `GameCanvas.Draw.cs` holds the drawing.
- `src/AVAGunSmoke/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAGunSmoke/Sounds/`: the generated WAV files.
- `tests/AVAGunSmoke.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: `GameplayTests`, `ScreenshotTests` and
  the opt-in `PilotProbe`.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` feeds frame time into an accumulator and runs the game in fixed
  `Field.Step` (1/60 s) steps, so tests can play stages deterministically from a seed. Draw random numbers from
  the engine's seeded `_rng` (`new Random(seed)`), never from `Random.Shared`. `Stage.Build` lays each stage out
  from its own fixed seed, so a stage number always gives the same ground.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** In this game `Dy` is positive for up (W, Up, D-pad up and a stick
  pushed up all give +1), unlike most of the AVA games. Keep keys, D-pad and stick agreeing, and test new controls
  through `InputHandler`, not by building `Controls` directly.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAGunSmoke`); tests load
  the table from a temporary folder and run with `Silent = true`. `Calm = true` makes the outlaws stand still and
  hold their fire.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
After a release it asks the portfolio site to refresh, if the `SITE_REFRESH_TOKEN` secret is set.
