using System.IO;
using System.Windows.Forms;
using OpenGamepadCompanion.Core.Config;
using OpenGamepadCompanion.Core.Engine;
using OpenGamepadCompanion.Core.Input;
using OpenGamepadCompanion.Core.Models;

namespace OpenGamepadCompanion.Tests;

internal sealed class MockPoller : IGamepadPoller
{
    public NormalizedPadState NextState { get; set; } = new() { IsConnected = true, ConnectedCount = 1 };
    public int TurnOffCount { get; private set; }

    public NormalizedPadState Poll(int leftDeadZone, int rightDeadZone) => NextState.Clone();
    public void TurnOffControllers() => TurnOffCount++;
}

internal sealed class MockSimulator : IInputSimulator
{
    public List<string> Events { get; } = [];

    public void KeyDown(Keys key) => Events.Add($"KeyDown:{key}");
    public void KeyUp(Keys key) => Events.Add($"KeyUp:{key}");
    public void SendUnicodeText(string text) => Events.Add($"Text:{text}");
    public void MouseButton(MouseButtonTarget button, bool down) => Events.Add($"Mouse:{button}:{(down ? "Down" : "Up")}");
    public void MouseMoveRelative(int dx, int dy) => Events.Add($"Move:{dx},{dy}");
    public void MouseScrollVertical(int delta) => Events.Add($"Wheel:{delta}");
}

internal sealed class MockWindowTracker : IActiveWindowTracker
{
    public ActiveProcessInfo Current { get; set; } = new("explorer", null, "Desktop", false, (IntPtr)100);
    public ActiveProcessInfo GetCurrentForeground() => Current;
    public IReadOnlyList<(string ProcessName, string? ExecutablePath)> GetRecentProcesses() => [];
}

internal sealed class MockVirtualKeyboard : IVirtualKeyboardController
{
    public bool IsVisible { get; private set; }
    public bool IsShiftActive { get; set; }
    public bool IsAltGrActive { get; set; }
    public void Show() => IsVisible = true;
    public void Hide() => IsVisible = false;
    public void UpdateStick(StickVector stick, double dtSeconds) { }
    public void StepSelection(int dx, int dy) { }
    public void SelectHighlightedKey() { }
    public void CycleLanguage() { }
}

public static class Program
{
    private static int _passed;

    public static int Main()
    {
        string tempConfigDir = Path.Combine(Path.GetTempPath(), $"OrbitTests_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempConfigDir);
            Directory.CreateDirectory(Path.Combine(tempConfigDir, "profiles"));

            Console.WriteLine("[1/4] Verifying Settings XML & .cfg/.ccp parser compatibility...");
            SeedSampleConfig(tempConfigDir);
            var config = new ConfigManager(tempConfigDir);
            config.ReloadAll();
            VerifySettingsAndBindings(config);

            Console.WriteLine("[2/4] Verifying sequential .ccp overrides & secondary stick layer...");
            VerifyProcessProfiles(config);

            Console.WriteLine("[3/4] Verifying pixel-exact Mouse Physics & Scroll curve parity...");
            VerifyMousePhysicsParity();

            Console.WriteLine("[4/4] Verifying CompanionEngine state machine & Fullscreen Game VK lock...");
            VerifyEngineSimulation(config);

            Console.WriteLine($"\nALL {_passed} ASSERTIONS PASSED SUCCESSFULLY!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"TEST FAILED: {ex}");
            return 1;
        }
        finally
        {
            try { Directory.Delete(tempConfigDir, recursive: true); } catch { }
        }
    }

    private static void SeedSampleConfig(string dir)
    {
        var settings = new AppSettings();
        settings.MouseSettings.Acceleration = 1800f;
        settings.MouseSettings.WheelSpeed = 9000f;
        settings.NotificationSettings.EnableSoundsOnDisable = false;
        settings.SteamSettings.ShowInAppInSteam = true;
        settings.ShareVoiceSettings.Enabled = true;
        settings.ShareVoiceSettings.TapToggleMode = true;
        settings.SaveXml(Path.Combine(dir, "Settings.xml"));

        File.WriteAllLines(Path.Combine(dir, "MouseModeBindings.cfg"),
        [
            "b @ Right mouse button: mouse right",
            "x @ Backspace: key Back norepeat",
            "left trigger @ F8: key F8 norepeat",
            "back left trigger @ Win+Shift+Left: key LWin+LShiftKey+Left norepeat",
            "guide @ AltGr: key RMenu norepeat"
        ]);

        File.WriteAllLines(Path.Combine(dir, "KeyboardModeBindings.cfg"),
        [
            "y @ F4: key F4 norepeat"
        ]);

        File.WriteAllLines(Path.Combine(dir, "IgnoredFullscreenProcesses.txt"), ["chrome", "mpv", "vlc"]);
        File.WriteAllLines(Path.Combine(dir, "DisabledProcesses.txt"), ["SampleNativePadGame"]);

        File.WriteAllLines(Path.Combine(dir, "profiles", "SampleSandboxApp.ccp"),
        [
            "process: SampleSandboxApp",
            "title: Sample Sandbox App",
            "b @ Backspace: key back",
            "b @ Right mouse button: mouse right",
            "left stick @ WSAD keys: key W S A D norepeat",
            "back left stick @ Mouse movement: mouse movement"
        ]);
    }

    private static void VerifySettingsAndBindings(ConfigManager config)
    {
        var s = config.Settings;
        AssertEqual(600f, s.MouseSettings.Speed, "Mouse.Speed");
        AssertEqual(1800f, s.MouseSettings.Acceleration, "Mouse.Acceleration");
        AssertEqual(9000f, s.MouseSettings.WheelSpeed, "Mouse.WheelSpeed");
        AssertEqual(false, s.NotificationSettings.EnableSoundsOnDisable, "EnableSoundsOnDisable");
        AssertEqual(true, s.ShareVoiceSettings.Enabled, "ShareVoice.Enabled");
        AssertEqual(true, s.ShareVoiceSettings.TapToggleMode, "ShareVoice.TapToggleMode");

        var d = config.DesktopBindings;
        AssertCmd(d, ModifierLayer.Primary, PadControl.A, "mouse left");
        AssertCmd(d, ModifierLayer.Primary, PadControl.B, "mouse right");
        AssertCmd(d, ModifierLayer.Primary, PadControl.X, "key Back norepeat");
        AssertCmd(d, ModifierLayer.Primary, PadControl.LeftTrigger, "key F8 norepeat");
        AssertCmd(d, ModifierLayer.Primary, PadControl.Guide, "key RMenu norepeat");
        AssertCmd(d, ModifierLayer.Back, PadControl.LeftTrigger, "key LWin+LShiftKey+Left norepeat");

        var kb = config.KeyboardBindings;
        AssertCmd(kb, ModifierLayer.Primary, PadControl.Y, "key F4 norepeat");
        AssertCmd(kb, ModifierLayer.Primary, PadControl.A, "virtualkeyboard select");
    }

    private static void VerifyProcessProfiles(ConfigManager config)
    {
        var p = config.FindProfileForProcess("SampleSandboxApp");
        AssertTrue(p != null, "SampleSandboxApp profile loaded");

        var map = p!.BuildRuntimeMap();
        // Verify later line ("mouse right") overrode earlier line ("key back")
        AssertCmd(map, ModifierLayer.Primary, PadControl.B, "mouse right");
        AssertEqual(ActionKind.DirectionalKeys, map.TryGet(ModifierLayer.Primary, PadControl.LeftStick)!.Action.Kind, "Primary LeftStick WSAD");
        AssertEqual(ActionKind.MouseMovement, map.TryGet(ModifierLayer.Back, PadControl.LeftStick)!.Action.Kind, "Secondary LeftStick MouseMovement");
    }

    private static void VerifyMousePhysicsParity()
    {
        var mp = new MousePhysics
        {
            MoveSpeed = 600f,
            Acceleration = 1800f,
            AccelerationThreshold = 0.95f,
            WheelSpeed = 9000f
        };

        AssertEqual(150, RunMove100Steps(mp, 0.25f), "Move x=0.25");
        AssertEqual(300, RunMove100Steps(mp, 0.50f), "Move x=0.50");
        AssertEqual(450, RunMove100Steps(mp, 0.75f), "Move x=0.75");
        AssertEqual(1509, RunMove100Steps(mp, 1.00f), "Move x=1.00 with accel");

        mp.ResetMovement();
        var (_, dy) = mp.StepMovement(new StickVector(0f, 0.5f), 0.01, inverted: false);
        AssertEqual(-3, dy, "Stick +Y moves cursor up (-3 px)");

        int totalWheel = 0;
        for (int i = 0; i < 100; i++)
            totalWheel += mp.StepScroll(new StickVector(0f, 1.0f), 0.01);
        AssertEqual(9000, totalWheel, "Scroll 1.0s @ y=1.0 -> 9000");
    }

    private static int RunMove100Steps(MousePhysics mp, float x)
    {
        mp.ResetMovement();
        int totalDx = 0;
        for (int i = 0; i < 100; i++)
        {
            var (dx, _) = mp.StepMovement(new StickVector(x, 0f), 0.01, inverted: false);
            totalDx += dx;
        }
        return totalDx;
    }

    private static void VerifyEngineSimulation(ConfigManager config)
    {
        var poller = new MockPoller();
        var sim = new MockSimulator();
        var tracker = new MockWindowTracker();
        var mockVk = new MockVirtualKeyboard();
        using var engine = new CompanionEngine(config, poller, sim, tracker) { VirtualKeyboard = mockVk };

        // 1. Desktop: Press B -> Right mouse button; Press Guide -> RMenu (AltGr)
        poller.NextState = new NormalizedPadState
        {
            IsConnected = true,
            ConnectedCount = 1,
            RawButtons = XInputButtons.B | XInputButtons.Guide
        };
        engine.Step(0.01);
        AssertTrue(sim.Events.Contains("Mouse:Right:Down"), "Desktop B -> Right Mouse Down");
        AssertTrue(sim.Events.Contains("KeyDown:RMenu"), "Desktop Guide -> AltGr Down");

        poller.NextState = new NormalizedPadState { IsConnected = true, ConnectedCount = 1 };
        engine.Step(0.01);

        // 2. SampleSandboxApp: LeftStick holds W+D; holding Back switches LeftStick to MouseMovement
        sim.Events.Clear();
        tracker.Current = new ActiveProcessInfo("SampleSandboxApp", null, "Sandbox", true, (IntPtr)200);
        poller.NextState = new NormalizedPadState
        {
            IsConnected = true,
            ConnectedCount = 1,
            LeftStick = new StickVector(0.8f, 0.8f)
        };
        engine.Step(0.01);
        AssertEqual(CompanionStatus.Enabled, engine.CurrentStatus, "Profile app remains Enabled in fullscreen");
        AssertTrue(sim.Events.Contains("KeyDown:W") && sim.Events.Contains("KeyDown:D"), "LeftStick -> W+D");

        sim.Events.Clear();
        poller.NextState = new NormalizedPadState
        {
            IsConnected = true,
            ConnectedCount = 1,
            RawButtons = XInputButtons.Back,
            LeftStick = new StickVector(0.5f, 0f)
        };
        engine.Step(0.01);
        AssertTrue(sim.Events.Contains("KeyUp:W") && sim.Events.Contains("KeyUp:D"), "Holding Back releases WSAD");
        AssertTrue(sim.Events.Any(e => e.StartsWith("Move:")), "Holding Back moves mouse");

        poller.NextState = new NormalizedPadState { IsConnected = true, ConnectedCount = 1 };
        engine.Step(0.01);

        // 3. Unknown Fullscreen 3D Game -> Auto-pauses, force-hides Virtual Keyboard, blocks LS, then Back+Start overrides!
        mockVk.Show();
        tracker.Current = new ActiveProcessInfo("SomeFullscreen3DGame", null, "Game", true, (IntPtr)300);
        poller.NextState = new NormalizedPadState
        {
            IsConnected = true,
            ConnectedCount = 1,
            RawButtons = XInputButtons.LeftThumb
        };
        engine.Step(0.01);
        AssertEqual(CompanionStatus.DisabledFullscreen, engine.CurrentStatus, "Unknown fullscreen game auto-pauses");
        AssertEqual(false, mockVk.IsVisible, "Fullscreen game force-hides Virtual Keyboard and blocks LS");

        poller.NextState = new NormalizedPadState
        {
            IsConnected = true,
            ConnectedCount = 1,
            RawButtons = XInputButtons.Back | XInputButtons.Start
        };
        engine.Step(0.01);
        poller.NextState = new NormalizedPadState { IsConnected = true, ConnectedCount = 1 };
        engine.Step(0.01);
        AssertEqual(CompanionStatus.Enabled, engine.CurrentStatus, "Back+Start overrides fullscreen auto-pause");
    }

    private static void AssertCmd(BindingProfileMap map, ModifierLayer layer, PadControl ctrl, string expectedRawCmd)
    {
        var entry = map.TryGet(layer, ctrl);
        AssertTrue(entry != null, $"Slot [{layer} {ctrl}] must exist");
        AssertTrue(string.Equals(entry!.RawCommand, expectedRawCmd, StringComparison.OrdinalIgnoreCase),
            $"Slot [{layer} {ctrl}] expected '{expectedRawCmd}' but got '{entry.RawCommand}'");
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Assertion failed [{label}]: expected '{expected}', got '{actual}'");
        _passed++;
    }

    private static void AssertTrue(bool condition, string label)
    {
        if (!condition)
            throw new Exception($"Assertion failed [{label}]");
        _passed++;
    }
}
