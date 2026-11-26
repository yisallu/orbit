using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenGamepadCompanion.App.Assets;
using OpenGamepadCompanion.App.Views;
using OpenGamepadCompanion.Core.Config;
using OpenGamepadCompanion.Core.Engine;
using OpenGamepadCompanion.Core.Input;
using OpenGamepadCompanion.Core.Models;
using OpenGamepadCompanion.Core.SystemIntegration;
using Application = System.Windows.Application;

namespace OpenGamepadCompanion.App;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showSettingsEvent;
    private NotifyIcon? _trayIcon;
    private ToolStripMenuItem? _statusMenuItem;
    private ToolStripMenuItem? _shareVoiceMenuItem;
    private DeviceHotplugSink? _deviceHotplugSink;

    public ConfigManager Config { get; private set; } = null!;
    public CompanionEngine Engine { get; private set; } = null!;
    public XboxShareVoiceService ShareVoice { get; private set; } = null!;
    public ActiveWindowTracker WindowTracker { get; private set; } = null!;
    public SendInputSimulator InputSimulator { get; private set; } = null!;

    private MainWindow? _settingsWindow;
    private SpiralKeyboardWindow? _keyboardWindow;
    private HelpOverlayWindow? _helpWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool isSelfTest = e.Args.Any(a => a.Equals("--self-test-ui", StringComparison.OrdinalIgnoreCase)
                                       || a.Equals("--render-ui-preview", StringComparison.OrdinalIgnoreCase));

        DispatcherUnhandledException += (_, exArgs) =>
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "ui_error.log");
            try { File.WriteAllText(logPath, exArgs.Exception.ToString()); } catch { }
            if (isSelfTest)
            {
                Console.Error.WriteLine(exArgs.Exception);
                Environment.Exit(2);
            }
        };

        if (!isSelfTest)
        {
            _singleInstanceMutex = new Mutex(true, "Local\\Orbit_Gamepad_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                try
                {
                    using var evt = EventWaitHandleAcl.OpenExisting("Local\\Orbit_Gamepad_ShowWindowEvent", EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize);
                    evt.Set();
                }
                catch { }
                Shutdown();
                return;
            }

            var ewhSec = new EventWaitHandleSecurity();
            ewhSec.AddAccessRule(new EventWaitHandleAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize,
                AccessControlType.Allow));
            _showSettingsEvent = EventWaitHandleAcl.Create(
                false,
                EventResetMode.AutoReset,
                "Local\\Orbit_Gamepad_ShowWindowEvent",
                out _,
                ewhSec);
            ThreadPool.RegisterWaitForSingleObject(
                _showSettingsEvent,
                (_, _) => Dispatcher.BeginInvoke(() =>
                {
                    if (_trayIcon != null) _trayIcon.Visible = true;
                    OpenSettingsWindow();
                }),
                null,
                -1,
                executeOnlyOnce: false);
        }

        string configDir = ResolveConfigDirectory();
        Config = new ConfigManager(configDir);

        if (!File.Exists(Config.SettingsPath))
        {
            CompanionMigrator.ImportFromControllerCompanion(configDir);
        }
        Config.ReloadAll();

        // Crucial Windows UIPI Fix:
        // Elevated apps (such as Antigravity, Task Manager, Admin Terminals) ignore SendInput from non-elevated processes!
        // When RunOnAdminAndLockScreen is enabled and Orbit isn't running as Administrator yet, seamlessly re-launch elevated!
        bool skipElevate = isSelfTest || e.Args.Any(a => a.Equals("--no-elevate", StringComparison.OrdinalIgnoreCase));
        if (!skipElevate &&
            Config.Settings.StartupSettings.RunOnAdminAndLockScreen &&
            !StartupManager.IsRunningAsAdministrator())
        {
            string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Orbit.exe");
            string forwardedArgs = string.Join(' ', e.Args.Append("--elevated-relaunch"));

            // Release mutex before handing off to the elevated instance!
            _showSettingsEvent?.Dispose();
            _showSettingsEvent = null;
            if (_singleInstanceMutex != null)
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }

            if (StartupManager.TryRelaunchAsAdmin(exePath, forwardedArgs))
            {
                Shutdown();
                return;
            }

            // User declined UAC prompt -> continue in non-elevated mode with "--no-elevate" behavior
        }

        // Once running as Administrator, retire the standalone XboxDoubaoVoice task/process so it never double-triggers Share -> Right Alt
        if (!isSelfTest && StartupManager.IsRunningAsAdministrator() && Config.Settings.ShareVoiceSettings.Enabled)
        {
            StartupManager.RetireLegacyXboxDoubaoVoice();
        }

        // Once running as Administrator with RunOnAdminAndLockScreen enabled, ensure the Logon HIGHEST task is registered
        if (!isSelfTest &&
            Config.Settings.StartupSettings.RunOnAdminAndLockScreen &&
            StartupManager.IsRunningAsAdministrator() &&
            !StartupManager.IsElevatedTaskInstalled())
        {
            string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Orbit.exe");
            StartupManager.ApplySystemStartup(exePath, true, true);
        }

        WindowTracker = new ActiveWindowTracker();
        InputSimulator = new SendInputSimulator();
        var poller = new XInputGamepadPoller();

        Engine = new CompanionEngine(Config, poller, InputSimulator, WindowTracker);
        ShareVoice = new XboxShareVoiceService(Config);
        _deviceHotplugSink = new DeviceHotplugSink(() => ShareVoice.RequestRescan());

        _keyboardWindow = new SpiralKeyboardWindow(Config, InputSimulator);
        _helpWindow = new HelpOverlayWindow(Config, Engine);
        Engine.VirtualKeyboard = _keyboardWindow;

        Engine.StatusChanged += OnEngineStatusChanged;
        Engine.BalloonNotificationRequested += OnBalloonRequested;
        Engine.ToggleHelpRequested += () => Dispatcher.BeginInvoke(() => _helpWindow?.ToggleOverlay());
        Config.ConfigurationChanged += () => Dispatcher.BeginInvoke(() =>
        {
            if (_shareVoiceMenuItem != null)
                _shareVoiceMenuItem.Checked = Config.Settings.ShareVoiceSettings.Enabled;
        });

        InitializeTrayIcon();
        Engine.Start();
        if (!isSelfTest)
            ShareVoice.Start();

        int previewIdx = Array.FindIndex(e.Args, a => a.Equals("--render-ui-preview", StringComparison.OrdinalIgnoreCase));
        if (previewIdx >= 0)
        {
            string outDir = previewIdx + 1 < e.Args.Length ? e.Args[previewIdx + 1] : Path.Combine(AppContext.BaseDirectory, "previews");
            RunAutomatedPreviewRender(outDir);
            return;
        }

        if (e.Args.Any(a => a.Equals("--self-test-ui", StringComparison.OrdinalIgnoreCase)))
        {
            OpenSettingsWindow();
            _settingsWindow!.UpdateLayout();
            ExitApplication();
            return;
        }

        bool startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)
                                           || a.Equals("--securedesktop", StringComparison.OrdinalIgnoreCase));
        if (!startMinimized)
        {
            OpenSettingsWindow();
        }
    }

    private void RunAutomatedPreviewRender(string outDir)
    {
        Directory.CreateDirectory(outDir);
        OpenSettingsWindow();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            var win = _settingsWindow!;

            // 1. Home Zero-Text Blueprint (First Glance!)
            win.SelectNavPage(0);
            CaptureWindowPng(win, Path.Combine(outDir, "01_home_zero_text.png"));

            // 2. Home with On-Demand Hover Capsule revealed
            var bSlot = new PadSlot(ModifierLayer.Primary, PadControl.B);
            win.ShowHoverCapsule(bSlot, Config.DesktopBindings.TryGet(bSlot));
            CaptureWindowPng(win, Path.Combine(outDir, "02_home_hover_reveal.png"));

            // 3. Motion & Deadzone Studio
            win.SelectNavPage(1);
            CaptureWindowPng(win, Path.Combine(outDir, "03_feel_studio.png"));

            // 4. Apps Icon Wall
            win.SelectNavPage(2);
            CaptureWindowPng(win, Path.Combine(outDir, "04_apps_grid.png"));

            // 5. Minimalist Pill Preferences
            win.SelectNavPage(3);
            CaptureWindowPng(win, Path.Combine(outDir, "05_system_prefs.png"));

            // 6. Action Quick-Bind Modal
            var bindDlg = new ActionBindDialog(
                new PadSlot(ModifierLayer.Back, PadControl.LeftTrigger),
                Config.DesktopBindings.TryGet(ModifierLayer.Back, PadControl.LeftTrigger),
                null);
            bindDlg.Show();
            CaptureWindowPng(bindDlg, Path.Combine(outDir, "06_bind_recorder.png"));
            bindDlg.Close();

            // 7. Spiral Radial Keyboard
            _keyboardWindow!.Show();
            _keyboardWindow.RenderRing();
            CaptureWindowPng(_keyboardWindow, Path.Combine(outDir, "07_spiral_keyboard.png"));

            ExitApplication();
        });
    }

    private static void CaptureWindowPng(Window window, string filePath)
    {
        window.Measure(new System.Windows.Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        window.UpdateLayout();

        int w = (int)Math.Ceiling(window.ActualWidth > 0 ? window.ActualWidth : window.Width);
        int h = (int)Math.Ceiling(window.ActualHeight > 0 ? window.ActualHeight : window.Height);

        var rtb = new RenderTargetBitmap(w * 2, h * 2, 192, 192, PixelFormats.Pbgra32);
        rtb.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(filePath);
        encoder.Save(fs);
    }

    public static string ResolveConfigDirectory()
    {
        string baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && dir != null; i++)
        {
            string candidate = Path.Combine(dir.FullName, "config");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Settings.xml")))
                return candidate;

            if (File.Exists(Path.Combine(dir.FullName, "Orbit.slnx")) ||
                File.Exists(Path.Combine(dir.FullName, "OpenGamepadCompanion.slnx")))
                return Path.Combine(dir.FullName, "config");

            dir = dir.Parent;
        }

        string localPortable = Path.Combine(baseDir, "config");
        if (Directory.Exists(localPortable))
            return localPortable;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitGamepad");
    }

    private void InitializeTrayIcon()
    {
        var menu = new ContextMenuStrip();

        _statusMenuItem = new ToolStripMenuItem("Orbit · 就绪") { Enabled = false };
        var toggleItem = new ToolStripMenuItem("启用 / 暂停操控 (Back + Start)", null, (_, _) =>
        {
            bool currentlyEnabled = Engine.CurrentStatus == CompanionStatus.Enabled;
            Engine.SetManualEnabled(!currentlyEnabled);
        });
        _shareVoiceMenuItem = new ToolStripMenuItem("分享键豆包语音 (Share → 右 Alt)", null, (_, _) =>
        {
            Config.Settings.ShareVoiceSettings.Enabled = !Config.Settings.ShareVoiceSettings.Enabled;
            _shareVoiceMenuItem!.Checked = Config.Settings.ShareVoiceSettings.Enabled;
            Config.SaveSettings();
        })
        {
            Checked = Config.Settings.ShareVoiceSettings.Enabled
        };
        var kbItem = new ToolStripMenuItem("虚拟键盘", null, (_, _) => ((IVirtualKeyboardController)_keyboardWindow!).Show());
        var helpItem = new ToolStripMenuItem("按键浮窗", null, (_, _) => _helpWindow?.ToggleOverlay());
        var openItem = new ToolStripMenuItem("打开 Orbit", null, (_, _) => OpenSettingsWindow());
        var exitItem = new ToolStripMenuItem("退出", null, (_, _) => ExitApplication());

        menu.Items.Add(_statusMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(openItem);
        menu.Items.Add(toggleItem);
        menu.Items.Add(_shareVoiceMenuItem);
        menu.Items.Add(kbItem);
        menu.Items.Add(helpItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = IconGenerator.CreateTrayIcon(Engine.CurrentStatus),
            Text = "Orbit",
            ContextMenuStrip = menu,
            Visible = !Config.Settings.NotificationSettings.HideNotificationIcon
        };
        _trayIcon.Click += (_, e) =>
        {
            if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
                OpenSettingsWindow();
        };
    }

    private void OnEngineStatusChanged(CompanionStatus status, string profileLabel)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_trayIcon == null) return;

            var oldIcon = _trayIcon.Icon;
            _trayIcon.Icon = IconGenerator.CreateTrayIcon(status);
            oldIcon?.Dispose();

            string statusZh = status switch
            {
                CompanionStatus.Enabled => $"已激活 · {profileLabel}",
                CompanionStatus.Disconnected => "等待手柄连接",
                CompanionStatus.DisabledFullscreen => "全屏自动暂停",
                CompanionStatus.DisabledPerProcess => $"进程已暂停 · {profileLabel}",
                CompanionStatus.DisabledManual => "已手动暂停",
                _ => status.ToString()
            };

            string tooltip = $"Orbit · {statusZh}";
            _trayIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
            if (_statusMenuItem != null)
                _statusMenuItem.Text = tooltip;
        });
    }

    private void OnBalloonRequested(string title, string body)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_trayIcon != null && _trayIcon.Visible && Config.Settings.NotificationSettings.ShowBalloonTips)
            {
                _trayIcon.ShowBalloonTip(2200, title, body, ToolTipIcon.None);
            }
        });
    }

    public void OpenSettingsWindow()
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new MainWindow(Config, Engine, WindowTracker, _keyboardWindow!, ShareVoice);
            MainWindow = _settingsWindow;
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Show();
        }

        _settingsWindow.Topmost = true;
        _settingsWindow.Activate();
        _settingsWindow.Topmost = false;
        _settingsWindow.Focus();
    }

    public void ExitApplication()
    {
        Engine.Stop();
        ShareVoice?.Dispose();
        _deviceHotplugSink?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _keyboardWindow?.Close();
        _helpWindow?.Close();
        _settingsWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Engine?.Dispose();
        ShareVoice?.Dispose();
        _deviceHotplugSink?.Dispose();
        _showSettingsEvent?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private sealed class DeviceHotplugSink : NativeWindow, IDisposable
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        private const int DBT_DEVNODES_CHANGED = 0x0007;

        private readonly Action _onChanged;

        public DeviceHotplugSink(Action onChanged)
        {
            _onChanged = onChanged;
            CreateHandle(new CreateParams { Caption = "OrbitDeviceHotplugSink" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE)
            {
                int evt = m.WParam.ToInt32();
                if (evt is DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE or DBT_DEVNODES_CHANGED)
                    _onChanged();
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}
