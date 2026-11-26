using System.Windows.Forms;

namespace OpenGamepadCompanion.Core.Config;

public static class KeyNameResolver
{
    private static readonly Dictionary<string, Keys> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = Keys.Return,
        ["return"] = Keys.Return,
        ["back"] = Keys.Back,
        ["backspace"] = Keys.Back,
        ["esc"] = Keys.Escape,
        ["escape"] = Keys.Escape,
        ["space"] = Keys.Space,
        ["spacebar"] = Keys.Space,
        ["ctrl"] = Keys.LControlKey,
        ["lctrl"] = Keys.LControlKey,
        ["lcontrolkey"] = Keys.LControlKey,
        ["rctrl"] = Keys.RControlKey,
        ["rcontrolkey"] = Keys.RControlKey,
        ["shift"] = Keys.LShiftKey,
        ["lshift"] = Keys.LShiftKey,
        ["lshiftkey"] = Keys.LShiftKey,
        ["rshift"] = Keys.RShiftKey,
        ["rshiftkey"] = Keys.RShiftKey,
        ["alt"] = Keys.LMenu,
        ["lmenu"] = Keys.LMenu,
        ["altgr"] = Keys.RMenu,
        ["rmenu"] = Keys.RMenu,
        ["lwin"] = Keys.LWin,
        ["rwin"] = Keys.RWin,
        ["win"] = Keys.LWin,
        ["pgup"] = Keys.PageUp,
        ["pageup"] = Keys.PageUp,
        ["prior"] = Keys.PageUp,
        ["pgdn"] = Keys.Next,
        ["pagedown"] = Keys.Next,
        ["next"] = Keys.Next,
        ["del"] = Keys.Delete,
        ["delete"] = Keys.Delete,
        ["ins"] = Keys.Insert,
        ["insert"] = Keys.Insert,
        ["period"] = Keys.OemPeriod,
        ["oemperiod"] = Keys.OemPeriod,
        ["comma"] = Keys.Oemcomma,
        ["oemcomma"] = Keys.Oemcomma,
        ["oemopenbrackets"] = Keys.OemOpenBrackets,
        ["oemclosebrackets"] = Keys.OemCloseBrackets,
        ["oem6"] = Keys.OemCloseBrackets,
        ["oem4"] = Keys.OemOpenBrackets,
        ["oemminus"] = Keys.OemMinus,
        ["oemplus"] = Keys.Oemplus,
        ["oemquestion"] = Keys.OemQuestion,
        ["oemtilde"] = Keys.Oemtilde,
        ["oemsemicolon"] = Keys.OemSemicolon,
        ["oem1"] = Keys.Oem1,
        ["oemquotes"] = Keys.OemQuotes,
        ["oem7"] = Keys.Oem7,
        ["oempipe"] = Keys.OemPipe,
        ["oem5"] = Keys.Oem5,
        ["plus"] = Keys.Oemplus,
        ["minus"] = Keys.OemMinus,
        ["0"] = Keys.D0,
        ["1"] = Keys.D1,
        ["2"] = Keys.D2,
        ["3"] = Keys.D3,
        ["4"] = Keys.D4,
        ["5"] = Keys.D5,
        ["6"] = Keys.D6,
        ["7"] = Keys.D7,
        ["8"] = Keys.D8,
        ["9"] = Keys.D9,
    };

    public static bool TryParseKey(string token, out Keys key)
    {
        key = Keys.None;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var trimmed = token.Trim();
        if (Aliases.TryGetValue(trimmed, out key))
            return true;

        if (Enum.TryParse(trimmed, ignoreCase: true, out key) && key != Keys.None)
            return true;

        return false;
    }

    public static string ToFriendlyKeyName(Keys key) => key switch
    {
        Keys.LControlKey or Keys.ControlKey => "Ctrl",
        Keys.RControlKey => "RCtrl",
        Keys.LShiftKey or Keys.ShiftKey => "Shift",
        Keys.RShiftKey => "RShift",
        Keys.LMenu or Keys.Menu => "Alt",
        Keys.RMenu => "AltGr",
        Keys.LWin => "Win",
        Keys.RWin => "RWin",
        Keys.Return => "Enter",
        Keys.Back => "Backspace",
        Keys.Escape => "Esc",
        Keys.OemPeriod => ".",
        Keys.Oemcomma => ",",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.OemQuestion => "/",
        Keys.Oemtilde => "`",
        Keys.OemSemicolon => ";",
        Keys.OemQuotes => "'",
        Keys.Next => "Page Down",
        Keys.PageUp => "Page Up",
        Keys.D0 => "0",
        Keys.D1 => "1",
        Keys.D2 => "2",
        Keys.D3 => "3",
        Keys.D4 => "4",
        Keys.D5 => "5",
        Keys.D6 => "6",
        Keys.D7 => "7",
        Keys.D8 => "8",
        Keys.D9 => "9",
        Keys.MediaPlayPause => "媒体播放/暂停",
        Keys.MediaNextTrack => "媒体下一曲目",
        Keys.MediaPreviousTrack => "媒体上一曲目",
        Keys.MediaStop => "媒体停止",
        Keys.VolumeUp => "音量增大",
        Keys.VolumeDown => "音量减小",
        Keys.VolumeMute => "音量静音",
        _ => key.ToString()
    };

    public static string LocalizeCommandTitle(string englishOrCustomTitle)
    {
        return englishOrCustomTitle switch
        {
            "Left mouse button" => "鼠标左键",
            "Right mouse button" => "鼠标右键",
            "Middle mouse button" => "鼠标中键",
            "4th mouse button" => "第 4 个鼠标按钮",
            "5th mouse button" => "第 5 个鼠标按钮",
            "Mouse movement" => "鼠标移动",
            "Mouse movement (Inverted)" => "鼠标移动 (倒置)",
            "Mouse scroll" => "鼠标滚轮",
            "Mouse scroll up" => "鼠标向上滚动",
            "Mouse scroll down" => "鼠标向下滚动",
            "Show Virtual Keyboard" => "显示虚拟键盘",
            "Hide Virtual Keyboard" => "隐藏虚拟键盘",
            "Virtual Keyboard Select" => "虚拟键盘选择",
            "Virtual Keyboard Input" => "虚拟键盘输入",
            "Virtual Keyboard Shift" => "虚拟键盘 SHIFT",
            "Virtual Keyboard AltGr" => "虚拟键盘 AltGr",
            "Cycle Virtual Keyboard Language" => "循环虚拟键盘语言",
            "Use Secondary Bindings" => "使用辅助绑定",
            "Use Alt-1 Bindings" => "使用 Alt-1 绑定",
            "Use Alt-2 Bindings" => "使用 Alt-2 绑定",
            "Launch Steam Big-Picture" => "启动 Steam 大屏幕",
            "Turn off controller" => "关闭手柄",
            "Display second screen only" => "仅显示第二屏幕",
            "Display PC screen only" => "仅显示电脑屏幕",
            "Display extend desktop" => "显示扩展桌面",
            "Display clone desktop" => "显示克隆桌面",
            "Minimize all windows" => "最小化所有窗口",
            "Open Task Switcher (Ctrl+Alt+Tab)" => "打开任务切换 (Ctrl+Alt+Tab)",
            "Volume up" => "音量增大",
            "Volume down" => "音量减小",
            "Volume mute" => "音量静音",
            "Media play/pause" => "媒体播放/暂停",
            "Media previous track" => "媒体上一曲目",
            "Media next track" => "媒体下一曲目",
            "Toggle help" => "切换帮助",
            "WSAD keys" => "WSAD 键",
            "Arrow keys" => "方向键",
            "Nothing" => "无",
            _ => englishOrCustomTitle
        };
    }
}
