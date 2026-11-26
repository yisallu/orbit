namespace OpenGamepadCompanion.Core.Models;

public enum ModifierLayer
{
    Primary = 0,
    Back = 1,
    Alt1 = 2,
    Alt2 = 3
}

public enum PadControl
{
    A,
    B,
    X,
    Y,
    DPadLeft,
    DPadRight,
    DPadUp,
    DPadDown,
    LeftShoulder,
    RightShoulder,
    LeftThumb,
    RightThumb,
    LeftTrigger,
    RightTrigger,
    LeftStick,
    RightStick,
    Start,
    BackButton,
    Guide
}

public readonly record struct PadSlot(ModifierLayer Layer, PadControl Control)
{
    public bool IsStick => Control is PadControl.LeftStick or PadControl.RightStick;

    public string ToDirectiveString()
    {
        string ctrl = ControlToDirective(Control);
        return Layer switch
        {
            ModifierLayer.Primary => ctrl,
            ModifierLayer.Back => $"back {ctrl}",
            ModifierLayer.Alt1 => $"alt1 {ctrl}",
            ModifierLayer.Alt2 => $"alt2 {ctrl}",
            _ => ctrl
        };
    }

    public string ToDisplayNameZh()
    {
        string ctrl = Control switch
        {
            PadControl.A => "A 键",
            PadControl.B => "B 键",
            PadControl.X => "X 键",
            PadControl.Y => "Y 键",
            PadControl.DPadLeft => "方向键 左",
            PadControl.DPadRight => "方向键 右",
            PadControl.DPadUp => "方向键 上",
            PadControl.DPadDown => "方向键 下",
            PadControl.LeftShoulder => "LB (左肩键)",
            PadControl.RightShoulder => "RB (右肩键)",
            PadControl.LeftThumb => "LS (左摇杆按下)",
            PadControl.RightThumb => "RS (右摇杆按下)",
            PadControl.LeftTrigger => "LT (左扳机)",
            PadControl.RightTrigger => "RT (右扳机)",
            PadControl.LeftStick => "左摇杆",
            PadControl.RightStick => "右摇杆",
            PadControl.Start => "Start 键",
            PadControl.BackButton => "Back 键",
            PadControl.Guide => "Xbox 导航键 (Guide)",
            _ => Control.ToString()
        };
        return Layer switch
        {
            ModifierLayer.Primary => ctrl,
            ModifierLayer.Back => $"Back + {ctrl}",
            ModifierLayer.Alt1 => $"Alt1 + {ctrl}",
            ModifierLayer.Alt2 => $"Alt2 + {ctrl}",
            _ => ctrl
        };
    }

    public static string ControlToDirective(PadControl control) => control switch
    {
        PadControl.A => "a",
        PadControl.B => "b",
        PadControl.X => "x",
        PadControl.Y => "y",
        PadControl.DPadLeft => "dpad left",
        PadControl.DPadRight => "dpad right",
        PadControl.DPadUp => "dpad up",
        PadControl.DPadDown => "dpad down",
        PadControl.LeftShoulder => "left shoulder",
        PadControl.RightShoulder => "right shoulder",
        PadControl.LeftThumb => "left thumb",
        PadControl.RightThumb => "right thumb",
        PadControl.LeftTrigger => "left trigger",
        PadControl.RightTrigger => "right trigger",
        PadControl.LeftStick => "left stick",
        PadControl.RightStick => "right stick",
        PadControl.Start => "start",
        PadControl.BackButton => "backbutton",
        PadControl.Guide => "guide",
        _ => throw new ArgumentOutOfRangeException(nameof(control))
    };

    public static bool TryParseDirective(string raw, out PadSlot slot)
    {
        slot = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var text = string.Join(' ', raw.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var layer = ModifierLayer.Primary;

        if (text.StartsWith("back ") && text != "backbutton")
        {
            layer = ModifierLayer.Back;
            text = text["back ".Length..].Trim();
        }
        else if (text.StartsWith("alt1 "))
        {
            layer = ModifierLayer.Alt1;
            text = text["alt1 ".Length..].Trim();
        }
        else if (text.StartsWith("alt2 "))
        {
            layer = ModifierLayer.Alt2;
            text = text["alt2 ".Length..].Trim();
        }

        PadControl? control = text switch
        {
            "a" => PadControl.A,
            "b" => PadControl.B,
            "x" => PadControl.X,
            "y" => PadControl.Y,
            "dpad left" => PadControl.DPadLeft,
            "dpad right" => PadControl.DPadRight,
            "dpad up" => PadControl.DPadUp,
            "dpad down" => PadControl.DPadDown,
            "left shoulder" => PadControl.LeftShoulder,
            "right shoulder" => PadControl.RightShoulder,
            "left thumb" => PadControl.LeftThumb,
            "right thumb" => PadControl.RightThumb,
            "left trigger" => PadControl.LeftTrigger,
            "right trigger" => PadControl.RightTrigger,
            "left stick" => PadControl.LeftStick,
            "right stick" => PadControl.RightStick,
            "start" => PadControl.Start,
            "backbutton" or "back" => PadControl.BackButton,
            "guide" => PadControl.Guide,
            _ => null
        };

        if (control == null)
            return false;

        slot = new PadSlot(layer, control.Value);
        return true;
    }

    public static IReadOnlyList<PadControl> AllControls { get; } =
    [
        PadControl.A,
        PadControl.B,
        PadControl.X,
        PadControl.Y,
        PadControl.DPadUp,
        PadControl.DPadDown,
        PadControl.DPadLeft,
        PadControl.DPadRight,
        PadControl.LeftShoulder,
        PadControl.RightShoulder,
        PadControl.LeftTrigger,
        PadControl.RightTrigger,
        PadControl.LeftThumb,
        PadControl.RightThumb,
        PadControl.Start,
        PadControl.BackButton,
        PadControl.Guide,
        PadControl.LeftStick,
        PadControl.RightStick
    ];
}
