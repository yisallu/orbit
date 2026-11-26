using System.Windows.Forms;

namespace OpenGamepadCompanion.Core.Models;

public enum ActionKind
{
    None,
    MouseButton,
    MouseMovement,
    MouseScroll,
    MouseScrollStep,
    MouseSpeedScale,
    KeyChord,
    DirectionalKeys,
    ModifierHold,
    CommandKeyCombo,
    CommandProcess,
    CommandTurnOffController,
    CommandToggleHelp,
    VirtualKeyboard
}

public enum MouseButtonTarget
{
    Left,
    Right,
    Middle,
    XButton1,
    XButton2
}

public enum VirtualKeyboardCommand
{
    Show,
    Hide,
    Select,
    Move,
    Shift,
    AltGr,
    Language
}

public sealed class BindingAction
{
    public ActionKind Kind { get; init; } = ActionKind.None;
    public MouseButtonTarget MouseButton { get; init; }
    public bool Inverted { get; init; }
    public int ScrollDirection { get; init; } // +1 up, -1 down
    public float SpeedScale { get; init; } = 1.0f;
    public IReadOnlyList<Keys> Keys { get; init; } = [];
    public bool AutoRepeat { get; init; }
    public ModifierLayer TargetModifier { get; init; }
    public string ProcessTarget { get; init; } = "";
    public string ProcessArguments { get; init; } = "";
    public VirtualKeyboardCommand VkCommand { get; init; }

    public static BindingAction None { get; } = new() { Kind = ActionKind.None };
}

public sealed class BindingEntry
{
    public required PadSlot Slot { get; init; }
    public required string Title { get; set; }
    public required string RawCommand { get; set; }
    public required BindingAction Action { get; set; }

    public string ToConfigLine() => $"{Slot.ToDirectiveString()} @ {Title}: {RawCommand}";

    public BindingEntry Clone() => new()
    {
        Slot = Slot,
        Title = Title,
        RawCommand = RawCommand,
        Action = Action
    };
}

public sealed class BindingProfileMap
{
    private readonly Dictionary<PadSlot, BindingEntry> _entries = new();

    public IReadOnlyDictionary<PadSlot, BindingEntry> Entries => _entries;

    public void Set(BindingEntry entry)
    {
        _entries[entry.Slot] = entry;
    }

    public void Remove(PadSlot slot)
    {
        _entries.Remove(slot);
    }

    public BindingEntry? TryGet(PadSlot slot)
        => _entries.TryGetValue(slot, out var entry) ? entry : null;

    public BindingEntry? TryGet(ModifierLayer layer, PadControl control)
        => TryGet(new PadSlot(layer, control));

    public BindingProfileMap Clone()
    {
        var copy = new BindingProfileMap();
        foreach (var kv in _entries)
            copy._entries[kv.Key] = kv.Value.Clone();
        return copy;
    }
}

public sealed class ProcessProfile
{
    public string FilePath { get; set; } = "";
    public List<string> ProcessNames { get; set; } = [];
    public string Title { get; set; } = "";
    public string? IconBase64 { get; set; }
    public string? WorkshopId { get; set; }
    public bool IsWorkshop { get; set; }
    public bool CanLaunchPrograms { get; set; } = true;
    public float? MouseSpeed { get; set; }
    public float? MouseAcceleration { get; set; }
    public float? MouseWheelSpeed { get; set; }
    public List<BindingEntry> Bindings { get; set; } = [];

    public string PrimaryProcessName => ProcessNames.FirstOrDefault() ?? Path.GetFileNameWithoutExtension(FilePath);

    public bool MatchesProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        foreach (var p in ProcessNames)
        {
            if (string.Equals(p, processName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public BindingProfileMap BuildRuntimeMap()
    {
        var map = new BindingProfileMap();
        // Controller Companion default profile always includes backbutton -> modifier back unless overridden
        map.Set(new BindingEntry
        {
            Slot = new PadSlot(ModifierLayer.Primary, PadControl.BackButton),
            Title = "Use Secondary Bindings",
            RawCommand = "modifier back",
            Action = new BindingAction { Kind = ActionKind.ModifierHold, TargetModifier = ModifierLayer.Back }
        });

        // Apply directives sequentially so later lines override earlier lines (exact .ccp behavior!)
        foreach (var b in Bindings)
        {
            map.Set(b);
        }
        return map;
    }
}
