using OpenGamepadCompanion.Core.Models;

namespace OpenGamepadCompanion.Core.Input;

public readonly record struct StickVector(float X, float Y)
{
    public float Magnitude => MathF.Sqrt(X * X + Y * Y);
    public static StickVector Zero => new(0f, 0f);
}

public sealed class NormalizedPadState
{
    public bool IsConnected { get; set; }
    public int ConnectedCount { get; set; }
    public ushort RawButtons { get; set; }
    public float LeftTrigger { get; set; }
    public float RightTrigger { get; set; }
    public bool LeftTriggerDigital { get; set; }
    public bool RightTriggerDigital { get; set; }
    public StickVector LeftStick { get; set; }
    public StickVector RightStick { get; set; }

    public bool IsControlDown(PadControl control) => control switch
    {
        PadControl.A => (RawButtons & XInputButtons.A) != 0,
        PadControl.B => (RawButtons & XInputButtons.B) != 0,
        PadControl.X => (RawButtons & XInputButtons.X) != 0,
        PadControl.Y => (RawButtons & XInputButtons.Y) != 0,
        PadControl.DPadUp => (RawButtons & XInputButtons.DPadUp) != 0,
        PadControl.DPadDown => (RawButtons & XInputButtons.DPadDown) != 0,
        PadControl.DPadLeft => (RawButtons & XInputButtons.DPadLeft) != 0,
        PadControl.DPadRight => (RawButtons & XInputButtons.DPadRight) != 0,
        PadControl.LeftShoulder => (RawButtons & XInputButtons.LeftShoulder) != 0,
        PadControl.RightShoulder => (RawButtons & XInputButtons.RightShoulder) != 0,
        PadControl.LeftThumb => (RawButtons & XInputButtons.LeftThumb) != 0,
        PadControl.RightThumb => (RawButtons & XInputButtons.RightThumb) != 0,
        PadControl.Start => (RawButtons & XInputButtons.Start) != 0,
        PadControl.BackButton => (RawButtons & XInputButtons.Back) != 0,
        PadControl.Guide => (RawButtons & XInputButtons.Guide) != 0,
        PadControl.LeftTrigger => LeftTriggerDigital,
        PadControl.RightTrigger => RightTriggerDigital,
        _ => false
    };

    public NormalizedPadState Clone() => (NormalizedPadState)MemberwiseClone();
}

public interface IGamepadPoller
{
    NormalizedPadState Poll(int leftDeadZone, int rightDeadZone);
    void TurnOffControllers();
}

public sealed class XInputGamepadPoller : IGamepadPoller
{
    private const byte TriggerPressThreshold = 45;
    private const byte TriggerReleaseThreshold = 30;

    private bool _prevLeftTriggerDigital;
    private bool _prevRightTriggerDigital;
    private long _pollCounter;
    private readonly bool[] _slotConnected = new bool[4];

    public NormalizedPadState Poll(int leftDeadZone, int rightDeadZone)
    {
        _pollCounter++;
        // Probe disconnected controller slots once every ~64 ticks (~0.5s) to avoid XInput Win32 stall overhead
        bool probeDisconnected = (_pollCounter & 63) == 1;

        ushort mergedButtons = 0;
        byte maxLT = 0;
        byte maxRT = 0;
        StickVector bestLeft = StickVector.Zero;
        StickVector bestRight = StickVector.Zero;
        int connectedCount = 0;

        for (uint i = 0; i < 4; i++)
        {
            if (!_slotConnected[i] && !probeDisconnected)
                continue;

            if (XInputNative.TryGetState(i, out var raw))
            {
                _slotConnected[i] = true;
                connectedCount++;
                mergedButtons |= raw.Gamepad.wButtons;
                if (raw.Gamepad.bLeftTrigger > maxLT) maxLT = raw.Gamepad.bLeftTrigger;
                if (raw.Gamepad.bRightTrigger > maxRT) maxRT = raw.Gamepad.bRightTrigger;

                var ls = ApplyRadialDeadZone(raw.Gamepad.sThumbLX, raw.Gamepad.sThumbLY, leftDeadZone);
                if (ls.Magnitude > bestLeft.Magnitude) bestLeft = ls;

                var rs = ApplyRadialDeadZone(raw.Gamepad.sThumbRX, raw.Gamepad.sThumbRY, rightDeadZone);
                if (rs.Magnitude > bestRight.Magnitude) bestRight = rs;
            }
            else
            {
                _slotConnected[i] = false;
            }
        }

        if (connectedCount == 0)
        {
            _prevLeftTriggerDigital = false;
            _prevRightTriggerDigital = false;
            return new NormalizedPadState { IsConnected = false };
        }

        _prevLeftTriggerDigital = _prevLeftTriggerDigital
            ? maxLT > TriggerReleaseThreshold
            : maxLT >= TriggerPressThreshold;

        _prevRightTriggerDigital = _prevRightTriggerDigital
            ? maxRT > TriggerReleaseThreshold
            : maxRT >= TriggerPressThreshold;

        return new NormalizedPadState
        {
            IsConnected = true,
            ConnectedCount = connectedCount,
            RawButtons = mergedButtons,
            LeftTrigger = maxLT / 255f,
            RightTrigger = maxRT / 255f,
            LeftTriggerDigital = _prevLeftTriggerDigital,
            RightTriggerDigital = _prevRightTriggerDigital,
            LeftStick = bestLeft,
            RightStick = bestRight
        };
    }

    public void TurnOffControllers() => XInputNative.PowerOffAllControllers();

    public static StickVector ApplyRadialDeadZone(short rawX, short rawY, int deadZone)
    {
        float x = Math.Clamp(rawX / 32767f, -1f, 1f);
        float y = Math.Clamp(rawY / 32767f, -1f, 1f);
        float dz = Math.Clamp(deadZone / 32767f, 0f, 0.95f);

        float mag = MathF.Sqrt(x * x + y * y);
        if (mag <= dz || mag < 1e-5f)
            return StickVector.Zero;

        float normalizedMag = Math.Min(1f, (mag - dz) / (1f - dz));
        float scale = normalizedMag / mag;
        return new StickVector(x * scale, y * scale);
    }
}
