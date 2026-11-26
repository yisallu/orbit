using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenGamepadCompanion.Core.Engine;

public sealed record ActiveProcessInfo(
    string ProcessName,
    string? ExecutablePath,
    string WindowTitle,
    bool IsFullscreen,
    IntPtr WindowHandle
);

public interface IActiveWindowTracker
{
    ActiveProcessInfo GetCurrentForeground();
    IReadOnlyList<(string ProcessName, string? ExecutablePath)> GetRecentProcesses();
}

public sealed class ActiveWindowTracker : IActiveWindowTracker
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private enum QUERY_USER_NOTIFICATION_STATE
    {
        QUNS_NOT_PRESENT = 1,
        QUNS_BUSY = 2,
        QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        QUNS_PRESENTATION_MODE = 4,
        QUNS_ACCEPTS_NOTIFICATIONS = 5,
        QUNS_QUIET_TIME = 6,
        QUNS_APP = 7
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE pquns);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref int lpdwSize);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int GWL_STYLE = -16;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;

    /// <summary>
    /// Built-in desktop apps, IDEs, browsers, terminals, chat clients, and media viewers
    /// that must NEVER trigger game-fullscreen auto-pause even when maximized or in F11 fullscreen.
    /// </summary>
    private static readonly HashSet<string> AlwaysDesktopApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "SearchHost", "SearchUI", "StartMenuExperienceHost", "ShellExperienceHost",
        "ApplicationFrameHost", "SystemSettings", "Taskmgr", "regedit", "mmc",
        "Antigravity", "Code", "Cursor", "Windsurf", "devenv", "rider64", "idea64", "pycharm64", "webstorm64",
        "WindowsTerminal", "pwsh", "powershell", "cmd", "conhost", "alacritty", "wezterm-gui", "kitty",
        "chrome", "msedge", "firefox", "zen", "brave", "vivaldi", "opera", "arc", "thorium",
        "WeChat", "Weixin", "QQ", "Telegram", "Discord", "Slack", "Teams", "Feishu", "DingTalk",
        "Obsidian", "Notion", "Typora", "notepad", "notepad++",
        "vlc", "mpv", "PotPlayerMini64", "PotPlayer64", "PotPlayer", "mpc-hc64", "mpc-hc32", "wmplayer",
        "Honeyview", "nomacs", "ShareX", "Snipaste", "glazewm", "yasb",
        "Orbit", "OpenGamepadCompanion"
    };

    private readonly LinkedList<(string ProcessName, string? ExecutablePath)> _recent = new();
    private IntPtr _cachedHwnd;
    private ActiveProcessInfo _cachedInfo = new("", null, "", false, IntPtr.Zero);
    private long _lastQueryTicks;

    public IReadOnlyList<(string ProcessName, string? ExecutablePath)> GetRecentProcesses()
    {
        lock (_recent)
        {
            return _recent.ToList();
        }
    }

    public ActiveProcessInfo GetCurrentForeground()
    {
        long now = Stopwatch.GetTimestamp();
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return new ActiveProcessInfo("", null, "", false, IntPtr.Zero);

        double elapsedMs = (now - _lastQueryTicks) * 1000.0 / Stopwatch.Frequency;
        if (hwnd == _cachedHwnd && elapsedMs < 100.0)
            return _cachedInfo;

        _lastQueryTicks = now;
        _cachedHwnd = hwnd;

        bool isShellOrDesktop = hwnd == GetShellWindow() || hwnd == GetDesktopWindow();
        GetWindowThreadProcessId(hwnd, out uint pid);

        string procName = "";
        string? exePath = null;
        if (pid != 0)
        {
            IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProc != IntPtr.Zero)
            {
                try
                {
                    var sb = new System.Text.StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(hProc, 0, sb, ref size))
                    {
                        exePath = sb.ToString();
                        procName = Path.GetFileNameWithoutExtension(exePath);
                    }
                }
                finally
                {
                    CloseHandle(hProc);
                }
            }

            if (string.IsNullOrEmpty(procName))
            {
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    procName = p.ProcessName;
                }
                catch
                {
                    // Ignore inaccessible process
                }
            }
        }

        var titleSb = new System.Text.StringBuilder(256);
        GetWindowText(hwnd, titleSb, titleSb.Capacity);
        string title = titleSb.ToString();

        bool isFullscreenGame = false;
        if (!isShellOrDesktop && !string.IsNullOrWhiteSpace(procName) && !AlwaysDesktopApps.Contains(procName))
        {
            isFullscreenGame = CheckIsFullscreenGame(hwnd, exePath);
        }

        if (!string.IsNullOrWhiteSpace(procName) &&
            !string.Equals(procName, "explorer", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(procName, "Orbit", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(procName, "OpenGamepadCompanion", StringComparison.OrdinalIgnoreCase))
        {
            TrackRecent(procName, exePath);
        }

        _cachedInfo = new ActiveProcessInfo(procName, exePath, title, isFullscreenGame, hwnd);
        return _cachedInfo;
    }

    private static bool CheckIsFullscreenGame(IntPtr hwnd, string? exePath)
    {
        if (!GetWindowRect(hwnd, out var winRect))
            return false;

        IntPtr hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (hMon == IntPtr.Zero)
            return false;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMon, ref mi))
            return false;

        int monW = mi.rcMonitor.Right - mi.rcMonitor.Left;
        int monH = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
        int winW = winRect.Right - winRect.Left;
        int winH = winRect.Bottom - winRect.Top;

        bool coversFullMonitor = monW > 0 && monH > 0
            && winRect.Left <= mi.rcMonitor.Left
            && winRect.Top <= mi.rcMonitor.Top
            && winRect.Right >= mi.rcMonitor.Right
            && winRect.Bottom >= mi.rcMonitor.Bottom
            && winW >= monW && winH >= monH;

        if (!coversFullMonitor)
            return false;

        // 1. Direct3D Exclusive Fullscreen -> Definitely a game
        if (SHQueryUserNotificationState(out var quns) == 0 &&
            quns == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN)
        {
            return true;
        }

        // 2. Borderless Fullscreen Game check: must have no standard window caption/sizing frame (or live inside a game library path)
        uint style = unchecked((uint)GetWindowLong(hwnd, GWL_STYLE));
        bool hasStandardWindowChrome = (style & (WS_CAPTION | WS_THICKFRAME)) == (WS_CAPTION | WS_THICKFRAME);
        if (!hasStandardWindowChrome)
            return true;

        if (!string.IsNullOrEmpty(exePath) &&
            (exePath.Contains("steamapps", StringComparison.OrdinalIgnoreCase) ||
             exePath.Contains("Epic Games", StringComparison.OrdinalIgnoreCase) ||
             exePath.Contains("Games", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private void TrackRecent(string procName, string? exePath)
    {
        lock (_recent)
        {
            var existing = _recent.FirstOrDefault(x => string.Equals(x.ProcessName, procName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(existing.ProcessName))
                _recent.Remove(existing);

            _recent.AddFirst((procName, exePath));
            while (_recent.Count > 20)
                _recent.RemoveLast();
        }
    }
}
