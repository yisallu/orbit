using OpenGamepadCompanion.Core.Models;

namespace OpenGamepadCompanion.Core.Config;

public static class DefaultBindings
{
    private static readonly string[] MouseModeLines =
    [
        "a @ Left mouse button: mouse left",
        "b @ Right mouse button: mouse right",
        "x @ Backspace: key Back norepeat",
        "y @ Media play/pause: key MediaPlayPause norepeat",
        "dpad left @ Left: key left",
        "dpad right @ Right: key right",
        "dpad up @ Up: key up",
        "dpad down @ Down: key down",
        "left shoulder @ Ctrl: key LControlKey norepeat",
        "right shoulder @ Enter: key Return norepeat",
        "left thumb @ Show Virtual Keyboard: virtualkeyboard show",
        "right thumb @ Open Task Switcher (Ctrl+Alt+Tab): cmd keyCombo LControlKey LMenu Tab",
        "left trigger @ F8: key F8 norepeat",
        "right trigger @ F8: key F8 norepeat",
        "left stick @ Mouse movement: mouse movement",
        "right stick @ Mouse scroll: mouse scroll",
        "start @ Enter: key Return norepeat",
        "backbutton @ Use Secondary Bindings: modifier back",
        "guide @ AltGr: key RMenu norepeat",
        "back guide @ Turn off controller: cmd turnOffController",
        "back a @ Win+Enter: key LWin+Return norepeat",
        "back b @ Ctrl+3: key LControlKey+D3 norepeat",
        "back x @ Win+q: key LWin+Q norepeat",
        "back y @ Shift+.: key LShiftKey+OemPeriod norepeat",
        "back dpad left @ Shift+1: key LShiftKey+D1 norepeat",
        "back dpad right @ Shift+3: key LShiftKey+D3 norepeat",
        "back dpad up @ Volume up: key VolumeUp",
        "back dpad down @ Volume down: key VolumeDown",
        "back left shoulder @ F8: key F8 norepeat",
        "back right shoulder @ Open Task Switcher (Ctrl+Alt+Tab): cmd keyCombo LControlKey LMenu Tab",
        "back left trigger @ Win+Shift+Left: key LWin+LShiftKey+Left norepeat",
        "back right trigger @ Alt+Enter: key LMenu+Return norepeat",
        "back right thumb @ Ctrl+t: key LControlKey+T norepeat"
    ];

    private static readonly string[] KeyboardModeLines =
    [
        "a @ Virtual Keyboard Select: virtualkeyboard select",
        "b @ Period: key OemPeriod norepeat",
        "x @ Backspace: key back",
        "y @ F4: key F4 norepeat",
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
