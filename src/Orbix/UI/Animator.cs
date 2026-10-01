using System.Diagnostics;
using System.Windows.Media;

namespace Orbix.UI;

internal static class Easing
{
    public static double Linear(double t) => t;

    public static double OutCubic(double t)
    {
        double u = 1 - t;
        return 1 - u * u * u;
    }

    public static double InOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    public static double InCubic(double t) => t * t * t;

    /// <summary>Ease-out with a small overshoot - gives the icons a lively "pop" when they fan out.</summary>
    public static double OutBack(double t)
    {
        const double c1 = 1.25;
        const double c3 = c1 + 1;
        double u = t - 1;
        return 1 + c3 * u * u * u + c1 * u * u;
    }
}

/// <summary>A number that glides to a target value; the owner receives every new value through a callback.</summary>
internal sealed class AnimatedDouble
{
    private readonly Animator _owner;
    private readonly Action<double>? _apply;
    private double _from;
    private double _to;
    private double _startMs;
    private double _durationMs;
    private double _delayMs;
    private Func<double, double> _ease = Easing.OutCubic;
    private Action? _completed;

    internal AnimatedDouble(Animator owner, double initial, Action<double>? apply)
    {
        _owner = owner;
        _apply = apply;
        Value = _from = _to = initial;
    }

    public double Value { get; private set; }

    public double Target => _to;

    public bool IsActive { get; internal set; }

    /// <summary>Jumps to the value without animation.</summary>
    public void Snap(double value)
    {
        IsActive = false;
        _completed = null;
        Value = _from = _to = value;
        _apply?.Invoke(value);
    }

    /// <summary>Starts a transition to <paramref name="target"/>. A zero duration (animations off) snaps.</summary>
    public void Go(double target, double durationMs, double delayMs = 0, Func<double, double>? ease = null, Action? completed = null)
    {
        durationMs = _owner.Scale(durationMs);
        delayMs = _owner.Scale(delayMs);
        if (!_owner.Enabled || (durationMs <= 0 && delayMs <= 0))
        {
            Snap(target);
            completed?.Invoke();
            return;
        }

        _completed = completed;

        _from = Value;
        _to = target;
        _durationMs = Math.Max(1, durationMs);
        _delayMs = delayMs;
        _ease = ease ?? Easing.OutCubic;
        _startMs = _owner.NowMs;
        IsActive = true;
        _owner.Activate(this);
    }

    internal bool Step(double nowMs)
    {
        if (!IsActive)
        {
            return false; // snapped or cancelled while queued
        }

        double t = (nowMs - _startMs - _delayMs) / _durationMs;
        if (t < 0)
        {
            return true; // still waiting for the delay
        }

        if (t >= 1)
        {
            Value = _to;
            IsActive = false;
            _apply?.Invoke(Value);
            var completed = _completed;
            _completed = null;
            completed?.Invoke();
            return false;
        }

        Value = _from + (_to - _from) * _ease(t);
        _apply?.Invoke(Value);
        return true;
    }
}

/// <summary>
/// Drives all <see cref="AnimatedDouble"/>s from <c>CompositionTarget.Rendering</c>. The render hook exists only
/// while something is animating, so an idle menu costs no CPU at all.
/// </summary>
internal sealed class Animator
{
    private readonly List<AnimatedDouble> _active = new();
    private readonly List<AnimatedDouble> _scratch = new();
    private bool _hooked;

    /// <summary>Speed multiplier (1 = normal, 2 = twice as fast).</summary>
    public double Speed { get; set; } = 1.0;

    /// <summary>False = every transition is applied immediately.</summary>
    public bool Enabled { get; set; } = true;

    public bool IsBusy => _active.Count > 0;

    public double NowMs => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    /// <summary>Raised (once) when the last running transition has finished.</summary>
    public event Action? Idle;

    public AnimatedDouble Create(double initial, Action<double>? apply = null) => new(this, initial, apply);

    internal double Scale(double ms) => ms / Math.Max(0.1, Speed);

    internal void Activate(AnimatedDouble value)
    {
        if (!_active.Contains(value))
        {
            _active.Add(value);
        }

        if (!_hooked)
        {
            CompositionTarget.Rendering += OnRendering;
            _hooked = true;
        }
    }

    /// <summary>Stops everything immediately (values stay where they are).</summary>
    public void CancelAll()
    {
        foreach (var value in _active)
        {
            value.IsActive = false;
        }

        _active.Clear();
        Unhook();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = NowMs;
        _scratch.Clear();
        _scratch.AddRange(_active);
        foreach (var value in _scratch)
        {
            if (!value.Step(now))
            {
                _active.Remove(value);
            }
        }

        if (_active.Count == 0)
        {
            Unhook();
            Idle?.Invoke();
        }
    }

    private void Unhook()
    {
        if (_hooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _hooked = false;
        }
    }
}
