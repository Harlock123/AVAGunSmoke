using Avalonia.Input;

namespace AVAGunSmoke.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: walking eight ways, and the three triggers that fire both guns up and to the left, straight up, or up and to the right.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update() => _pad.Update();

    public Controls Controls
    {
        get
        {
            int dx = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            if (dx == 0) dx = _pad.DPadLeft || _pad.LeftStickX < -0.4f ? -1 : _pad.DPadRight || _pad.LeftStickX > 0.4f ? 1 : 0;
            int dy = (Any(Key.Up, Key.W, Key.NumPad8) ? 1 : 0) - (Any(Key.Down, Key.S, Key.NumPad2) ? 1 : 0);
            if (dy == 0) dy = _pad.DPadUp || _pad.LeftStickY < -0.4f ? 1 : _pad.DPadDown || _pad.LeftStickY > 0.4f ? -1 : 0;
            // Three triggers, one for each way the guns point: up and to the left, straight up, up and to the right.
            bool left = Any(Key.Z, Key.J, Key.Q) || _pad.ButtonX || _pad.LeftBumper;
            bool up = Any(Key.X, Key.K, Key.Space, Key.LeftCtrl, Key.RightCtrl) || _pad.ButtonY || _pad.ButtonA;
            bool right = Any(Key.C, Key.L, Key.E) || _pad.ButtonB || _pad.RightBumper;
            return new Controls(dx, dy, left, up, right);
        }
    }

    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
