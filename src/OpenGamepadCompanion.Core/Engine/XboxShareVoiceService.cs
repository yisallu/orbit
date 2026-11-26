using System.Runtime.InteropServices;
using OpenGamepadCompanion.Core.Config;
using OpenGamepadCompanion.Core.Input;

namespace OpenGamepadCompanion.Core.Engine;

/// <summary>
/// Monitors the Xbox Series X|S Share button directly over XUSB (IOCTL_XUSB_GET_STATE byte 28 bit 0)
/// and triggers hardware-level Right Alt for Doubao IME voice input (both Bluetooth LE and USB).
/// </summary>
public sealed class XboxShareVoiceService : IDisposable
{
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_XUSB_GET_STATE = 0x8000E00C;

    private static readonly Guid GUID_DEVINTERFACE_XUSB = new("EC87F1E3-C13B-4100-B5F7-8B84D54260CB");
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, out uint RequiredSize, IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, byte[] lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private readonly ConfigManager _config;
    private readonly HardwareKeySender _keySender = new();
    private readonly Dictionary<string, IntPtr> _handles = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private Thread? _worker;

    private volatile bool _rescanRequested = true;
    private volatile bool _sharePressed;
    private volatile int _activeControllerCount;
    private volatile int _triggerCount;
    private long _lastTapTick;

    public bool IsHardwareMode => _keySender.IsHardwareMode;
    public int ActiveControllerCount => _activeControllerCount;
    public bool IsSharePressed => _sharePressed;
    public int TriggerCount => _triggerCount;

    public event Action? StateChanged;

    public XboxShareVoiceService(ConfigManager config)
    {
        _config = config;
        _config.ConfigurationChanged += OnConfigChanged;
    }

    private void OnConfigChanged()
    {
        var sv = _config.Settings.ShareVoiceSettings;
        if (!sv.Enabled && _sharePressed && !sv.TapToggleMode)
        {
            _sharePressed = false;
            _keySender.SendRightAlt(false);
            StateChanged?.Invoke();
        }
    }

    public void Start()
    {
        if (_worker != null) return;
        _worker = new Thread(RunLoop)
        {
            Name = "Orbit.XboxShareVoiceLoop",
            IsBackground = true
        };
        _worker.Start();
    }

    public void RequestRescan() => _rescanRequested = true;

    public async Task TestTriggerAsync()
    {
        bool tap = _config.Settings.ShareVoiceSettings.TapToggleMode;
        _keySender.SendRightAlt(true);
        await Task.Delay(tap ? 100 : 800);
        _keySender.SendRightAlt(false);
        _triggerCount++;
        StateChanged?.Invoke();
    }

    private void RunLoop()
    {
        var token = _cts.Token;
        var inBuf = new byte[] { 0x01, 0x01, 0x00 };
        var outBuf = new byte[64];
        long lastScanTick = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                long now = Environment.TickCount64;
                if (_rescanRequested || now - lastScanTick >= 3000)
                {
                    _rescanRequested = false;
                    lastScanTick = now;
                    SyncXusbDevices();
                }

                if (_handles.Count == 0)
                {
                    if (_sharePressed)
                    {
                        _sharePressed = false;
                        _keySender.SendRightAlt(false);
                    }
                    UpdateStatus(0, false);
                    WaitInterval(token, 500);
                    continue;
                }

                bool shareNow = false;
                int aliveCount = 0;
                List<string>? deadPaths = null;

                foreach (var kv in _handles)
                {
                    bool deviceAlive = false;
                    for (byte subIdx = 0; subIdx < 4; subIdx++)
                    {
                        inBuf[2] = subIdx;
                        if (DeviceIoControl(
                                kv.Value,
                                IOCTL_XUSB_GET_STATE,
                                inBuf,
                                3,
                                outBuf,
                                29,
                                out uint ret,
                                IntPtr.Zero) && ret >= 29)
                        {
                            deviceAlive = true;
                            if (outBuf[2] == 1 && (outBuf[28] & 1) != 0)
                            {
                                shareNow = true;
                            }
                        }
                    }

                    if (deviceAlive)
                        aliveCount++;
                    else
                        (deadPaths ??= []).Add(kv.Key);
                }

                if (deadPaths != null)
                {
                    foreach (var p in deadPaths) CloseDevice(p);
                }

                var sv = _config.Settings.ShareVoiceSettings;
                if (!sv.Enabled)
                    shareNow = false;

                if (shareNow != _sharePressed)
                {
                    _sharePressed = shareNow;
                    if (sv.TapToggleMode)
                    {
                        if (shareNow)
                        {
                            long tick = Environment.TickCount64;
                            if (tick - _lastTapTick >= 250)
                            {
                                _lastTapTick = tick;
                                _triggerCount++;
                                UpdateStatus(aliveCount, true, forceNotify: true);
                                _keySender.SendRightAlt(true);
                                WaitInterval(token, 100);
                                _keySender.SendRightAlt(false);
                            }
                        }
                        else
                        {
                            UpdateStatus(aliveCount, false, forceNotify: true);
                        }
                    }
                    else
                    {
                        if (shareNow) _triggerCount++;
                        _keySender.SendRightAlt(shareNow);
                        UpdateStatus(aliveCount, shareNow, forceNotify: true);
                    }
                }
                else
                {
                    UpdateStatus(aliveCount, shareNow);
                }

                WaitInterval(token, aliveCount > 0 ? 15 : 500);
            }
        }
        finally
        {
            if (_sharePressed && !_config.Settings.ShareVoiceSettings.TapToggleMode)
            {
                _sharePressed = false;
                _keySender.SendRightAlt(false);
            }
            CloseAllDevices();
        }
    }

    private void UpdateStatus(int count, bool pressed, bool forceNotify = false)
    {
        if (forceNotify || count != _activeControllerCount)
        {
            _activeControllerCount = count;
            StateChanged?.Invoke();
        }
    }

    private static void WaitInterval(CancellationToken token, int ms)
    {
        try { token.WaitHandle.WaitOne(ms); } catch (ObjectDisposedException) { }
    }

    private void SyncXusbDevices()
    {
        var currentPaths = EnumerateXusbPaths();

        foreach (var existing in _handles.Keys.Where(k => !currentPaths.Contains(k)).ToList())
            CloseDevice(existing);

        foreach (var path in currentPaths)
        {
            if (_handles.ContainsKey(path)) continue;

            IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == INVALID_HANDLE_VALUE)
                h = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

            if (h != IntPtr.Zero && h != INVALID_HANDLE_VALUE)
                _handles[path] = h;
        }
    }

    private static HashSet<string> EnumerateXusbPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Guid g = GUID_DEVINTERFACE_XUSB;
        IntPtr hDevInfo = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (hDevInfo == IntPtr.Zero || hDevInfo == INVALID_HANDLE_VALUE)
            return paths;

        try
        {
            uint idx = 0;
            var did = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            while (SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref g, idx++, ref did))
            {
                SetupDiGetDeviceInterfaceDetail(hDevInfo, ref did, IntPtr.Zero, 0, out uint req, IntPtr.Zero);
                if (req == 0) continue;

                IntPtr detail = Marshal.AllocHGlobal((int)req);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref did, detail, req, out _, IntPtr.Zero))
                    {
                        string? p = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                        if (!string.IsNullOrEmpty(p)) paths.Add(p);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return paths;
    }

    private void CloseDevice(string path)
    {
        if (_handles.Remove(path, out IntPtr h) && h != IntPtr.Zero && h != INVALID_HANDLE_VALUE)
            CloseHandle(h);
    }

    private void CloseAllDevices()
    {
        foreach (var h in _handles.Values)
        {
            if (h != IntPtr.Zero && h != INVALID_HANDLE_VALUE) CloseHandle(h);
        }
        _handles.Clear();
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _config.ConfigurationChanged -= OnConfigChanged;
        _cts.Cancel();
        if (_worker?.IsAlive == true) _worker.Join(800);
        _cts.Dispose();
        _keySender.Dispose();
    }
}
