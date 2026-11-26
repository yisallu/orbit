using OpenGamepadCompanion.Core.Input;

namespace OpenGamepadCompanion.Core.Engine;

public sealed class MousePhysics
{
    private double _leftoverX;
    private double _leftoverY;
    private double _accelAccumulator;
    private double _leftoverWheel;

    public float MoveSpeed { get; set; } = 600f;
    public float Acceleration { get; set; } = 1500f;
    public float AccelerationThreshold { get; set; } = 0.95f;
    public float WheelSpeed { get; set; } = 4800f;
    public float SpeedScale { get; set; } = 1.0f;

    public void ResetMovement()
    {
        _leftoverX = 0.0;
        _leftoverY = 0.0;
        _accelAccumulator = 0.0;
    }

    /// <summary>
    /// Exact Controller Companion pointer velocity &amp; acceleration formula verified against numerical probe:
    /// Below threshold (0.95): velocity = stick * MoveSpeed * SpeedScale.
    /// At/above threshold (0.95): _accelAccumulator += Acceleration * dt, velocity = stick * (MoveSpeed + _accelAccumulator) * SpeedScale.
    /// </summary>
    public (int dx, int dy) StepMovement(StickVector axes, double dtSeconds, bool inverted)
    {
        double dt = Math.Clamp(dtSeconds, 0.0, 0.1);
        float mag = axes.Magnitude;
        if (mag <= 1e-5f)
        {
            _accelAccumulator = 0.0;
            return (0, 0);
        }

        if (mag >= AccelerationThreshold)
        {
            _accelAccumulator = Math.Min(MoveSpeed * 8.0, _accelAccumulator + Acceleration * dt);
        }
        else
        {
            _accelAccumulator = 0.0;
        }

        double effectiveSpeed = (MoveSpeed + _accelAccumulator) * SpeedScale;
        double ySign = inverted ? 1.0 : -1.0;

        double rawDx = axes.X * effectiveSpeed * dt + _leftoverX;
        double rawDy = axes.Y * ySign * effectiveSpeed * dt + _leftoverY;

        int dx = (int)rawDx;
        int dy = (int)rawDy;

        _leftoverX = rawDx - dx;
        _leftoverY = rawDy - dy;

        return (dx, dy);
    }

    /// <summary>
    /// Exact Controller Companion vertical scroll wheel formula: WheelSpeed * axes.Y * dt.
    /// </summary>
    public int StepScroll(StickVector axes, double dtSeconds)
    {
        double dt = Math.Clamp(dtSeconds, 0.0, 0.1);
        if (MathF.Abs(axes.Y) <= 1e-5f)
        {
            _leftoverWheel = 0.0;
            return 0;
        }

        double rawWheel = axes.Y * WheelSpeed * dt + _leftoverWheel;
        int wheelDelta = (int)rawWheel;
        _leftoverWheel = rawWheel - wheelDelta;
        return wheelDelta;
    }
}
