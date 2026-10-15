using System.Windows.Threading;
using RetroHub.Core.Input;

namespace RetroHub.App.Input;

public enum PadButton { Up, Down, Left, Right, A, B, X, Y, LB, RB, Start, Back }

/// <summary>
/// Reads the Xbox controllers at ~60 Hz and turns them into navigation events:
/// D-pad and left stick repeat while held; Back + Start held for 1 s = "quit game".
/// Any of the 4 controllers can navigate.
/// </summary>
public sealed class GamepadNavigator : IDisposable
{
    private const short StickThreshold = 16000;
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(380);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(110);
    private static readonly TimeSpan ExitHold = TimeSpan.FromSeconds(1);

    private readonly DispatcherTimer _timer;
    private readonly HashSet<PadButton> _down = [];
    private readonly Dictionary<PadButton, DateTime> _nextRepeat = [];
    private DateTime? _comboSince;
    private bool _comboFired;

    public event Action<PadButton>? Pressed;
    public event Action? ExitComboHeld;

    public GamepadNavigator()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() => _timer.Start();
    public void Dispose() => _timer.Stop();

    private void Poll()
    {
        var now = DateTime.UtcNow;
        var current = new HashSet<PadButton>();
        for (var slot = 0; slot < 4; slot++)
            if (XInput.TryGetState(slot, out var state))
                current.UnionWith(Map(state));

        // Back + Start held: quit the game (fires once per press).
        if (current.Contains(PadButton.Back) && current.Contains(PadButton.Start))
        {
            _comboSince ??= now;
            if (!_comboFired && now - _comboSince >= ExitHold)
            {
                _comboFired = true;
                ExitComboHeld?.Invoke();
            }
        }
        else
        {
            _comboSince = null;
            _comboFired = false;
        }

        foreach (var button in current)
        {
            if (_down.Add(button))
            {
                _nextRepeat[button] = now + RepeatDelay;
                Pressed?.Invoke(button);
            }
            else if (IsDirection(button) && now >= _nextRepeat[button])
            {
                _nextRepeat[button] = now + RepeatInterval;
                Pressed?.Invoke(button);
            }
        }
        _down.IntersectWith(current);
    }

    private static IEnumerable<PadButton> Map(GamepadState s)
    {
        var b = s.Buttons;
        if (b.HasFlag(XInputButtons.DPadUp) || s.LeftY > StickThreshold) yield return PadButton.Up;
        if (b.HasFlag(XInputButtons.DPadDown) || s.LeftY < -StickThreshold) yield return PadButton.Down;
        if (b.HasFlag(XInputButtons.DPadLeft) || s.LeftX < -StickThreshold) yield return PadButton.Left;
        if (b.HasFlag(XInputButtons.DPadRight) || s.LeftX > StickThreshold) yield return PadButton.Right;
        if (b.HasFlag(XInputButtons.A)) yield return PadButton.A;
        if (b.HasFlag(XInputButtons.B)) yield return PadButton.B;
        if (b.HasFlag(XInputButtons.X)) yield return PadButton.X;
        if (b.HasFlag(XInputButtons.Y)) yield return PadButton.Y;
        if (b.HasFlag(XInputButtons.LeftShoulder)) yield return PadButton.LB;
        if (b.HasFlag(XInputButtons.RightShoulder)) yield return PadButton.RB;
        if (b.HasFlag(XInputButtons.Start)) yield return PadButton.Start;
        if (b.HasFlag(XInputButtons.Back)) yield return PadButton.Back;
    }

    private static bool IsDirection(PadButton b) => b is PadButton.Up or PadButton.Down or PadButton.Left or PadButton.Right;
}
