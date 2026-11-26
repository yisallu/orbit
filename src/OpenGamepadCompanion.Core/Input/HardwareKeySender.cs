using System.Runtime.InteropServices;

namespace OpenGamepadCompanion.Core.Input;

/// <summary>
/// Sends hardware-level Right Alt (ScanCode 0xE0 0x38) via Interception kernel driver
/// without LLKHF_INJECTED flag so Doubao IME voice input triggers reliably across all versions,
/// falling back cleanly to Win32 SendInput if the driver isn't loaded yet.
/// </summary>
public sealed class HardwareKeySender : IDisposable
{
    private const ushort ScanCodeAlt = 0x38;
    private const ushort KeyDownFlag = 0x00;
    private const ushort KeyUpFlag = 0x01;
    private const ushort KeyE0Flag = 0x02;

    private const uint INPUT_KEYBOARD = 1;
    private const ushort VK_RMENU = 0xA5;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct InterceptionKeyStroke
    {
        public ushort Code;
        public ushort State;
        public uint Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
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
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL, wParamH;
    }

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr interception_create_context();

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void interception_destroy_context(IntPtr context);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int interception_send(IntPtr context, int device, ref InterceptionKeyStroke stroke, uint nstroke);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint interception_get_hardware_id(IntPtr context, int device, byte[] hardwareIdBuffer, uint bufferSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private IntPtr _context = IntPtr.Zero;
    private int _keyboardDevice = 1;
    private readonly object _lock = new();

    public bool IsHardwareMode => _context != IntPtr.Zero;

    public HardwareKeySender()
    {
        TryInitInterception();
    }

    public bool TryInitInterception()
    {
        lock (_lock)
        {
            if (_context != IntPtr.Zero) return true;

            try
            {
                IntPtr ctx = interception_create_context();
                if (ctx == IntPtr.Zero) return false;

                int selectedDevice = 1;
                var idBuf = new byte[512];
                for (int dev = 1; dev <= 10; dev++)
                {
                    uint len = interception_get_hardware_id(ctx, dev, idBuf, (uint)idBuf.Length);
                    if (len > 0)
                    {
                        selectedDevice = dev;
                        break;
                    }
                }

                _context = ctx;
                _keyboardDevice = selectedDevice;
                return true;
            }
            catch
            {
                _context = IntPtr.Zero;
                return false;
            }
        }
    }

    public void SendRightAlt(bool keyDown)
    {
        lock (_lock)
        {
            if (_context != IntPtr.Zero)
            {
                var stroke = new InterceptionKeyStroke
                {
                    Code = ScanCodeAlt,
                    State = (ushort)((keyDown ? KeyDownFlag : KeyUpFlag) | KeyE0Flag),
                    Information = 0
                };
                int sent = interception_send(_context, _keyboardDevice, ref stroke, 1);
                if (sent > 0) return;

                var idBuf = new byte[512];
                for (int dev = 1; dev <= 10; dev++)
                {
                    if (interception_get_hardware_id(_context, dev, idBuf, (uint)idBuf.Length) > 0)
                    {
                        if (interception_send(_context, dev, ref stroke, 1) > 0)
                        {
                            _keyboardDevice = dev;
                            return;
                        }
                    }
                }
            }
        }

        SendRightAltFallback(keyDown);
    }

    private static void SendRightAltFallback(bool keyDown)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = VK_RMENU,
                    wScan = ScanCodeAlt,
                    dwFlags = keyDown ? KEYEVENTF_EXTENDEDKEY : (KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP)
                }
            }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_context != IntPtr.Zero)
            {
                try { interception_destroy_context(_context); } catch { }
                _context = IntPtr.Zero;
            }
        }
    }
}
