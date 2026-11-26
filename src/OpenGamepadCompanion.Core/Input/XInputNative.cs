using System.Runtime.InteropServices;

namespace OpenGamepadCompanion.Core.Input;

[StructLayout(LayoutKind.Sequential)]
public struct XInputGamepad
{
    public ushort wButtons;
    public byte bLeftTrigger;
    public byte bRightTrigger;
    public short sThumbLX;
    public short sThumbLY;
    public short sThumbRX;
    public short sThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
public struct XInputState
{
    public uint dwPacketNumber;
    public XInputGamepad Gamepad;
}

public static class XInputButtons
{
    public const ushort DPadUp = 0x0001;
    public const ushort DPadDown = 0x0002;
    public const ushort DPadLeft = 0x0004;
    public const ushort DPadRight = 0x0008;
    public const ushort Start = 0x0010;
    public const ushort Back = 0x0020;
    public const ushort LeftThumb = 0x0040;
    public const ushort RightThumb = 0x0080;
    public const ushort LeftShoulder = 0x0100;
    public const ushort RightShoulder = 0x0200;
    public const ushort Guide = 0x0400; // Exposed via XInputGetStateEx (Ordinal #100)!
    public const ushort A = 0x1000;
    public const ushort B = 0x2000;
    public const ushort X = 0x4000;
    public const ushort Y = 0x8000;
}

public static class XInputNative
{
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, IntPtr procOrdinal);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint XInputGetStateDelegate(uint dwUserIndex, out XInputState pState);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint XInputPowerOffDelegate(uint dwUserIndex);

    private static readonly XInputGetStateDelegate? _getState;
    private static readonly XInputPowerOffDelegate? _powerOff;
    public static string LoadedDllName { get; } = "None";
    public static bool SupportsGuideButton { get; }

    static XInputNative()
    {
        string[] candidates = ["xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll"];
        foreach (var dll in candidates)
        {
            if (NativeLibrary.TryLoad(dll, out IntPtr handle))
            {
                LoadedDllName = dll;

                // Ordinal #100 is XInputGetStateEx which includes the Xbox Guide button (0x0400)
                IntPtr proc100 = GetProcAddress(handle, (IntPtr)100);
                if (proc100 != IntPtr.Zero)
                {
                    _getState = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(proc100);
                    SupportsGuideButton = true;
                }
                else if (NativeLibrary.TryGetExport(handle, "XInputGetState", out IntPtr procStd))
                {
                    _getState = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(procStd);
                    SupportsGuideButton = false;
                }

                // Ordinal #103 is XInputPowerOffController (wireless Xbox 360 / compatible receivers)
                IntPtr proc103 = GetProcAddress(handle, (IntPtr)103);
                if (proc103 != IntPtr.Zero)
                {
                    _powerOff = Marshal.GetDelegateForFunctionPointer<XInputPowerOffDelegate>(proc103);
                }

                if (_getState != null)
                    break;
            }
        }
    }

    public static bool TryGetState(uint userIndex, out XInputState state)
    {
        if (_getState == null)
        {
            state = default;
            return false;
        }
        return _getState(userIndex, out state) == 0;
    }

    public static void PowerOffAllControllers()
    {
        if (_powerOff == null)
            return;

        for (uint i = 0; i < 4; i++)
        {
            try
            {
                _powerOff(i);
            }
            catch
            {
                // Ignore unsupported devices
            }
        }
    }
}
