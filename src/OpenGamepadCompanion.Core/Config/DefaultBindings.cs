using OpenGamepadCompanion.Core.Models;

namespace OpenGamepadCompanion.Core.Config;

public static class DefaultBindings
{
    private static readonly string[] MouseModeLines =
    [
        "a @ Left mouse button: mouse left",
        "b @ Backspace: key back",
        "x @ Right mouse button: mouse right",
        "y @ Media play/pause: key MediaPlayPause norepeat",
        "dpad left @ Left: key left",
        "dpad right @ Right: key right",
        "dpad up @ Up: key up",
        "dpad down @ Down: key down",
        "left shoulder @ Media previous track: key MediaPreviousTrack norepeat",
        "right shoulder @ Media next track: key MediaNextTrack norepeat",
        "left thumb @ Show Virtual Keyboard: virtualkeyboard show",
        "right thumb @ Middle mouse button: mouse middle",
        "left trigger @ Ctrl: key lcontrolkey norepeat",
        "right trigger @ Shift: key lshiftkey norepeat",
        "left stick @ Mouse movement: mouse movement",
        "right stick @ Mouse scroll: mouse scroll",
        "start @ Enter: key enter norepeat",
        "backbutton @ Use Secondary Bindings: modifier back",
        "guide @ Launch Steam Big-Picture: cmd process steam://open/bigpicture",
        "back guide @ Turn off controller: cmd turnOffController",
        "back a @ Display second screen only: cmd process displayswitch.exe /external",
        "back y @ Display PC screen only: cmd process displayswitch.exe /internal",
        "back x @ Minimize all windows: cmd keyCombo LWin M",
        "back right shoulder @ Open Task Switcher (Ctrl+Alt+Tab): cmd keyCombo LControlKey LMenu Tab",
        "back dpad up @ Volume up: key VolumeUp",
        "back dpad down @ Volume down: key VolumeDown",
        "back b @ Escape: key escape norepeat",
        "back right thumb @ Toggle help: cmd togglehelp"
    ];

    private static readonly string[] KeyboardModeLines =
    [
        "a @ Virtual Keyboard Select: virtualkeyboard select",
        "b @ Period: key OemPeriod norepeat",
        "x @ Backspace: key back",
        "y @ Space: key space",
        "left shoulder @ IME Toggle: key LShiftKey norepeat",
        "right shoulder @ Space: key Space",
        "dpad left @ Left: key left",
        "dpad right @ Right: key right",
        "dpad up @ Up: key up",
        "dpad down @ Down: key down",
        "left thumb @ Hide Virtual Keyboard: virtualkeyboard hide",
        "left trigger @ Virtual Keyboard AltGr: virtualkeyboard altgr",
        "right trigger @ Virtual Keyboard Shift: virtualkeyboard shift",
        "start @ Enter: key enter norepeat",
        "backbutton @ Use Secondary Bindings: modifier back",
        "back right thumb @ Toggle help: cmd togglehelp",
        "right thumb @ Cycle Virtual Keyboard Language: virtualkeyboard language",
        "left stick @ Virtual Keyboard Input: virtualkeyboard move",
        "right stick @ Arrow keys: key Up Down Left Right norepeat"
    ];

    public static BindingProfileMap CreateDefaultMouseModeMap()
    {
        var map = new BindingProfileMap();
        foreach (var line in MouseModeLines)
        {
            if (DirectiveParser.TryParseBindingLine(line, out var entry) && entry != null)
                map.Set(entry);
        }
        return map;
    }

    public static BindingProfileMap CreateDefaultKeyboardModeMap()
    {
        var map = new BindingProfileMap();
        foreach (var line in KeyboardModeLines)
        {
            if (DirectiveParser.TryParseBindingLine(line, out var entry) && entry != null)
                map.Set(entry);
        }
        return map;
    }
}
