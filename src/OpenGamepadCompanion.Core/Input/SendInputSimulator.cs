using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenGamepadCompanion.Core.Models;

namespace OpenGamepadCompanion.Core.Input;

public interface IInputSimulator
{
    void KeyDown(Keys key);
    void KeyUp(Keys key);
    void SendUnicodeText(string text);
    void MouseButton(MouseButtonTarget button, bool down);
    void MouseMoveRelative(int dx, int dy);
    void MouseScrollVertical(int delta);
}

public sealed class SendInputSimulator : IInputSimulator
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const uint XBUTTON1 = 0x0001;
    private const uint XBUTTON2 = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    public void KeyDown(Keys key) => SendKey(key, up: false);

    public void KeyUp(Keys key) => SendKey(key, up: true);

    private static void SendKey(Keys key, bool up)
    {
        ushort vk = (ushort)((int)key & 0xFFFF);
        if (vk == 0)
            return;

        ushort scan = (ushort)MapVirtualKey(vk, 0);
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (IsExtendedKey(key))
            flags |= KEYEVENTF_EXTENDEDKEY;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = flags
                }
            }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    public void SendUnicodeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char ch in text)
        {
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_UNICODE }
                }
            });
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP }
                }
            });
        }
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    public void MouseButton(MouseButtonTarget button, bool down)
    {
        uint flags;
        uint data = 0;
        switch (button)
        {
            case MouseButtonTarget.Left:
                flags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
                break;
            case MouseButtonTarget.Right:
                flags = down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
                break;
            case MouseButtonTarget.Middle:
                flags = down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;
                break;
            case MouseButtonTarget.XButton1:
                flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP;
                data = XBUTTON1;
                break;
            case MouseButtonTarget.XButton2:
                flags = down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP;
                data = XBUTTON2;
                break;
            default:
                return;
        }

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT { dwFlags = flags, mouseData = data }
            }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    public void MouseMoveRelative(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
            return;

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSEEVENTF_MOVE }
            }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    public void MouseScrollVertical(int delta)
    {
        if (delta == 0)
            return;

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    mouseData = unchecked((uint)delta),
                    dwFlags = MOUSEEVENTF_WHEEL
                }
            }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static bool IsExtendedKey(Keys key) => key is
        Keys.RControlKey or
        Keys.RMenu or
        Keys.LWin or
        Keys.RWin or
        Keys.Apps or
        Keys.Insert or
        Keys.Delete or
        Keys.Home or
        Keys.End or
        Keys.PageUp or
        Keys.Next or
        Keys.Up or
        Keys.Down or
        Keys.Left or
        Keys.Right or
        Keys.NumLock or
        Keys.PrintScreen or
        Keys.Divide or
        Keys.MediaNextTrack or
        Keys.MediaPreviousTrack or
        Keys.MediaStop or
        Keys.MediaPlayPause or
        Keys.VolumeMute or
        Keys.VolumeUp or
        Keys.VolumeDown;
}
