using System.Diagnostics;
using System.Media;
using System.Windows.Forms;
using OpenGamepadCompanion.Core.Config;
using OpenGamepadCompanion.Core.Input;
using OpenGamepadCompanion.Core.Models;

namespace OpenGamepadCompanion.Core.Engine;

public enum CompanionStatus
{
    Disconnected,
    Enabled,
    DisabledManual,
    DisabledPerProcess,
    DisabledFullscreen
}

public interface IVirtualKeyboardController
{
    bool IsVisible { get; }
    bool IsShiftActive { get; set; }
    bool IsAltGrActive { get; set; }
    void Show();
    void Hide();
    void UpdateStick(StickVector stick, double dtSeconds);
    void StepSelection(int dx, int dy);
    void SelectHighlightedKey();
    void CycleLanguage();
}

public sealed class CompanionEngine : IDisposable
{
    private const double InitialRepeatDelaySeconds = 0.42;
    private const double RepeatIntervalSeconds = 0.055;
    private const float DirectionalStickThreshold = 0.38f;
    private const int ScrollStepDelta = 120;

    private readonly ConfigManager _config;
    private readonly IGamepadPoller _poller;
    private readonly IInputSimulator _input;
    private readonly IActiveWindowTracker _windowTracker;
    private readonly MousePhysics _mousePhysics = new();

    private readonly HashSet<Keys> _heldPhysicalKeys = new();
    private readonly HashSet<MouseButtonTarget> _heldMouseButtons = new();
    private readonly Dictionary<PadSlot, double> _nextRepeatAt = new();
    private readonly HashSet<PadControl> _prevDownControls = new();
    private readonly Dictionary<PadControl, PadSlot> _activeBoundSlotByControl = new();

    private Thread? _loopThread;
    private volatile bool _running;
    private double _totalSeconds;

    private bool _manualDisabledSystemWide;
    private IntPtr _fullscreenOverriddenWindow;
    private bool _toggleComboLatched;
    private CompanionStatus _lastStatus = CompanionStatus.Disconnected;
    private string _activeProfileTitle = "桌面";

    public IVirtualKeyboardController? VirtualKeyboard { get; set; }

    public CompanionStatus CurrentStatus => _lastStatus;
    public string ActiveProfileTitle => _activeProfileTitle;
    public NormalizedPadState LastPadState { get; private set; } = new();
    public ActiveProcessInfo LastForeground { get; private set; } = new("", null, "", false, IntPtr.Zero);

    public event Action<CompanionStatus, string>? StatusChanged;
    public event Action<string, string>? BalloonNotificationRequested;
    public event Action? ToggleHelpRequested;

    public CompanionEngine(
        ConfigManager config,
        IGamepadPoller poller,
        IInputSimulator input,
        IActiveWindowTracker windowTracker)
    {
        _config = config;
        _poller = poller;
        _input = input;
        _windowTracker = windowTracker;
    }

    public void Start()
    {
        if (_running)
            return;

        _running = true;
        _loopThread = new Thread(LoopMain)
        {
            Name = "OpenGamepadCompanion.EngineLoop",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _loopThread.Start();
    }

    public void Stop()
    {
        _running = false;
        _loopThread?.Join(500);
        ReleaseAllHeldOutputs();
    }

    public void SetManualEnabled(bool enabled)
    {
        if (_config.Settings.DisablerSettings.PerProcessDisable)
        {
            var fg = _windowTracker.GetCurrentForeground();
            if (!string.IsNullOrWhiteSpace(fg.ProcessName))
            {
                if (enabled)
                    _config.DisabledProcesses.Remove(fg.ProcessName);
                else
                    _config.DisabledProcesses.Add(fg.ProcessName);
                _config.SaveDisabledProcesses();
            }
        }
        else
        {
            _manualDisabledSystemWide = !enabled;
        }

        if (!enabled)
            ReleaseAllHeldOutputs();
    }

    private void LoopMain()
    {
        var sw = Stopwatch.StartNew();
        double prevSec = sw.Elapsed.TotalSeconds;

        while (_running)
        {
            double nowSec = sw.Elapsed.TotalSeconds;
            double dt = Math.Clamp(nowSec - prevSec, 0.001, 0.1);
            prevSec = nowSec;

            try
            {
                Step(dt);
            }
            catch
            {
                // Never crash the input loop thread
            }

            Thread.Sleep(8); // ~125Hz responsive polling
        }
    }

    /// <summary>
    /// Executes a single deterministic engine frame (exposed publicly for automated tests).
    /// </summary>
    public void Step(double dtSeconds)
    {
        _totalSeconds += dtSeconds;
        var settings = _config.Settings;

        var pad = _poller.Poll(
            settings.ControllerSettings.LeftStickDeadZone,
            settings.ControllerSettings.RightStickDeadZone);
        LastPadState = pad;

        var fg = _windowTracker.GetCurrentForeground();
        LastForeground = fg;

        if (fg.WindowHandle != _fullscreenOverriddenWindow && !fg.IsFullscreen)
        {
            _fullscreenOverriddenWindow = IntPtr.Zero;
        }

        if (!pad.IsConnected)
        {
            ReleaseAllHeldOutputs();
            _prevDownControls.Clear();
            _toggleComboLatched = false;
            UpdateStatus(CompanionStatus.Disconnected, "已断开连接");
            return;
        }

        // Check Back + Start toggle chord BEFORE anything else (works even while disabled!)
        bool backDown = pad.IsControlDown(PadControl.BackButton);
        bool startDown = pad.IsControlDown(PadControl.Start);
        if (backDown && startDown)
        {
            if (!_toggleComboLatched)
            {
                _toggleComboLatched = true;
                HandleBackStartToggle(fg);
            }
            // Swallow Start/Back outputs during the toggle chord
            ReleaseAllHeldOutputs();
            SyncPrevControls(pad);
            return;
        }
        else if (!backDown && !startDown)
        {
            _toggleComboLatched = false;
        }

        // Resolve matching process profile
        var procProfile = _config.FindProfileForProcess(fg.ProcessName);

        // Check Disablers (exact Controller Companion MasterManualDisablingLooper + FullScreenLoopDisabler order)
        var status = EvaluateEnabledStatus(fg, procProfile);
        string profileLabel = procProfile != null ? $"{procProfile.Title} ({procProfile.PrimaryProcessName})" : "桌面";
        UpdateStatus(status, profileLabel);

        if (status != CompanionStatus.Enabled)
        {
            // When auto-paused by a fullscreen game or disabled, force-hide virtual keyboard & block all simulated inputs!
            ReleaseAllHeldOutputs(hideVirtualKeyboard: true);
            SyncPrevControls(pad);
            return;
        }

        // Choose active BindingProfileMap (VirtualKeyboard mode vs Process profile vs Desktop MouseMode)
        bool vkVisible = VirtualKeyboard?.IsVisible == true;
        BindingProfileMap activeMap;
        if (vkVisible)
        {
            activeMap = _config.KeyboardBindings;
        }
        else if (procProfile != null)
        {
            activeMap = procProfile.BuildRuntimeMap();
        }
        else
        {
            activeMap = _config.DesktopBindings;
        }

        // Sync mouse physics parameters (allowing per-profile overrides!)
        _mousePhysics.MoveSpeed = procProfile?.MouseSpeed ?? settings.MouseSettings.Speed;
        _mousePhysics.Acceleration = procProfile?.MouseAcceleration ?? settings.MouseSettings.Acceleration;
        _mousePhysics.AccelerationThreshold = settings.MouseSettings.AccelerationThreshold;
        _mousePhysics.WheelSpeed = procProfile?.MouseWheelSpeed ?? settings.MouseSettings.WheelSpeed;

        // Determine active modifier layer (Primary, Back, Alt1, Alt2)
        ModifierLayer activeLayer = ResolveActiveModifierLayer(pad, activeMap);

        // Compute mouse speed scaler if any held button binds MouseSpeedScale
        float speedScale = 1.0f;
        foreach (var ctrl in PadSlot.AllControls)
        {
            if (!ctrl.IsStickControl() && pad.IsControlDown(ctrl))
            {
                var entry = ResolveSlotEntry(activeMap, activeLayer, ctrl);
                if (entry?.Action.Kind == ActionKind.MouseSpeedScale)
                    speedScale *= entry.Action.SpeedScale;
            }
        }
        _mousePhysics.SpeedScale = speedScale;

        // Process buttons & triggers
        if (vkVisible && activeLayer == ModifierLayer.Primary)
        {
            ProcessVirtualKeyboardDpad(pad);
        }
        ProcessButtons(pad, activeMap, activeLayer, procProfile?.CanLaunchPrograms ?? true, vkVisible);

        // Process Left & Right Sticks
        ProcessStick(PadControl.LeftStick, pad.LeftStick, activeMap, activeLayer, dtSeconds);
        ProcessStick(PadControl.RightStick, pad.RightStick, activeMap, activeLayer, dtSeconds);

        SyncPrevControls(pad);
    }

    private double _nextVkDpadRepeatAt;

    private void ProcessVirtualKeyboardDpad(NormalizedPadState pad)
    {
        int dx = (pad.IsControlDown(PadControl.DPadRight) ? 1 : 0) - (pad.IsControlDown(PadControl.DPadLeft) ? 1 : 0);
        int dy = (pad.IsControlDown(PadControl.DPadDown) ? 1 : 0) - (pad.IsControlDown(PadControl.DPadUp) ? 1 : 0);

        bool wasAnyDpad = _prevDownControls.Contains(PadControl.DPadUp) ||
                          _prevDownControls.Contains(PadControl.DPadDown) ||
                          _prevDownControls.Contains(PadControl.DPadLeft) ||
                          _prevDownControls.Contains(PadControl.DPadRight);

        if (dx == 0 && dy == 0)
        {
            _nextVkDpadRepeatAt = 0;
            return;
        }

        if (!wasAnyDpad)
        {
            VirtualKeyboard?.StepSelection(dx, dy);
            _nextVkDpadRepeatAt = _totalSeconds + 0.28;
        }
        else if (_totalSeconds >= _nextVkDpadRepeatAt)
        {
            VirtualKeyboard?.StepSelection(dx, dy);
            _nextVkDpadRepeatAt = _totalSeconds + 0.075;
        }
    }

    private CompanionStatus EvaluateEnabledStatus(ActiveProcessInfo fg, ProcessProfile? matchingProfile)
    {
        var settings = _config.Settings;

        if (settings.DisablerSettings.PerProcessDisable)
        {
            if (!string.IsNullOrWhiteSpace(fg.ProcessName) && _config.DisabledProcesses.Contains(fg.ProcessName))
                return CompanionStatus.DisabledPerProcess;
        }
        else
        {
            if (_manualDisabledSystemWide)
                return CompanionStatus.DisabledManual;
        }

        // Fullscreen auto-disable: ignored if user overrode with Back+Start, or process is in IgnoredFullscreenProcesses, or has an explicit process profile!
        if (fg.IsFullscreen &&
            fg.WindowHandle != _fullscreenOverriddenWindow &&
            matchingProfile == null &&
            (string.IsNullOrWhiteSpace(fg.ProcessName) || !_config.IgnoredFullscreenProcesses.Contains(fg.ProcessName)))
        {
            return CompanionStatus.DisabledFullscreen;
        }

        return CompanionStatus.Enabled;
    }

    private void HandleBackStartToggle(ActiveProcessInfo fg)
    {
        var settings = _config.Settings;

        if (_lastStatus == CompanionStatus.DisabledFullscreen)
        {
            // Override fullscreen auto-disable for this fullscreen window!
            _fullscreenOverriddenWindow = fg.WindowHandle;
            RaiseToggleFeedback(nowEnabled: true, "手柄控制已启用（已覆写全屏自动禁用）");
            return;
        }

        if (settings.DisablerSettings.PerProcessDisable)
        {
            if (!string.IsNullOrWhiteSpace(fg.ProcessName))
            {
                bool wasDisabled = _config.DisabledProcesses.Contains(fg.ProcessName);
                if (wasDisabled)
                {
                    _config.DisabledProcesses.Remove(fg.ProcessName);
                    _config.SaveDisabledProcesses();
                    RaiseToggleFeedback(nowEnabled: true, $"已为进程 {fg.ProcessName} 启用手柄控制");
                }
                else
                {
                    _config.DisabledProcesses.Add(fg.ProcessName);
                    _config.SaveDisabledProcesses();
                    RaiseToggleFeedback(nowEnabled: false, $"已为进程 {fg.ProcessName} 禁用手柄控制");
                }
            }
        }
        else
        {
            _manualDisabledSystemWide = !_manualDisabledSystemWide;
            RaiseToggleFeedback(
                nowEnabled: !_manualDisabledSystemWide,
                _manualDisabledSystemWide ? "控制已禁用（Back + Start）" : "控制已启用（Back + Start）");
        }
    }

    private void RaiseToggleFeedback(bool nowEnabled, string message)
    {
        if (!nowEnabled && _config.Settings.NotificationSettings.EnableSoundsOnDisable)
        {
            try { SystemSounds.Asterisk.Play(); } catch { }
        }

        if (_config.Settings.NotificationSettings.ShowBalloonTips)
        {
            BalloonNotificationRequested?.Invoke("Orbit", message);
        }
    }

    private static ModifierLayer ResolveActiveModifierLayer(NormalizedPadState pad, BindingProfileMap map)
    {
        foreach (var ctrl in PadSlot.AllControls)
        {
            if (ctrl.IsStickControl() || !pad.IsControlDown(ctrl))
                continue;

            var primary = map.TryGet(ModifierLayer.Primary, ctrl);
            if (primary?.Action.Kind == ActionKind.ModifierHold)
                return primary.Action.TargetModifier;
        }
        return ModifierLayer.Primary;
    }

    private static BindingEntry? ResolveSlotEntry(BindingProfileMap map, ModifierLayer activeLayer, PadControl control)
    {
        var entry = map.TryGet(activeLayer, control);
        if (entry != null)
            return entry;

        // For sticks, if no secondary stick binding exists, keep primary stick movement/scroll active while Back is held!
        if (control.IsStickControl() && activeLayer != ModifierLayer.Primary)
            return map.TryGet(ModifierLayer.Primary, control);

        return null;
    }

    private void ProcessButtons(
        NormalizedPadState pad,
        BindingProfileMap map,
        ModifierLayer activeLayer,
        bool canLaunchPrograms,
        bool vkVisible = false)
    {
        foreach (var ctrl in PadSlot.AllControls)
        {
            if (ctrl.IsStickControl())
                continue;

            // When Virtual Keyboard is open on Primary layer, D-Pad steps keyboard selection instead of sending raw arrow keys
            if (vkVisible && activeLayer == ModifierLayer.Primary &&
                ctrl is PadControl.DPadUp or PadControl.DPadDown or PadControl.DPadLeft or PadControl.DPadRight)
            {
                continue;
            }

            bool isDown = pad.IsControlDown(ctrl);
            bool wasDown = _prevDownControls.Contains(ctrl);

            if (isDown && !wasDown)
            {
                // Newly pressed: bind to current active layer!
                var entry = ResolveSlotEntry(map, activeLayer, ctrl);
                if (entry != null && entry.Action.Kind != ActionKind.None && entry.Action.Kind != ActionKind.ModifierHold)
                {
                    _activeBoundSlotByControl[ctrl] = entry.Slot;
                    OnButtonPressed(entry, canLaunchPrograms);
                }
            }
            else if (isDown && wasDown)
            {
                // Held down: check if the bound layer is still valid or handle auto-repeat
                if (_activeBoundSlotByControl.TryGetValue(ctrl, out var boundSlot))
                {
                    var entry = map.TryGet(boundSlot);
                    if (entry != null)
                    {
                        OnButtonHeld(entry);
                    }
                }
            }
            else if (!isDown && wasDown)
            {
                // Released: release whatever action was started when this button went down!
                if (_activeBoundSlotByControl.Remove(ctrl, out var boundSlot))
                {
                    var entry = map.TryGet(boundSlot);
                    if (entry != null)
                    {
                        OnButtonReleased(entry);
                    }
                }
            }
        }
    }

    private void OnButtonPressed(BindingEntry entry, bool canLaunchPrograms)
    {
        var action = entry.Action;
        switch (action.Kind)
        {
            case ActionKind.MouseButton:
                _input.MouseButton(action.MouseButton, down: true);
                _heldMouseButtons.Add(action.MouseButton);
                break;

            case ActionKind.MouseScrollStep:
                _input.MouseScrollVertical(action.ScrollDirection * ScrollStepDelta);
                if (action.AutoRepeat)
                    _nextRepeatAt[entry.Slot] = _totalSeconds + InitialRepeatDelaySeconds;
                break;

            case ActionKind.KeyChord:
                PressChordDown(action.Keys);
                if (action.AutoRepeat)
                    _nextRepeatAt[entry.Slot] = _totalSeconds + InitialRepeatDelaySeconds;
                break;

            case ActionKind.CommandKeyCombo:
                // Press all down in order, then release in reverse order
                foreach (var k in action.Keys)
                    _input.KeyDown(k);
                for (int i = action.Keys.Count - 1; i >= 0; i--)
                    _input.KeyUp(action.Keys[i]);
                break;

            case ActionKind.CommandProcess:
                if (canLaunchPrograms && !string.IsNullOrWhiteSpace(action.ProcessTarget))
                {
                    LaunchProcessSafe(action.ProcessTarget, action.ProcessArguments);
                }
                break;

            case ActionKind.CommandTurnOffController:
                _poller.TurnOffControllers();
                break;

            case ActionKind.CommandToggleHelp:
                ToggleHelpRequested?.Invoke();
                break;

            case ActionKind.VirtualKeyboard:
                ExecuteVirtualKeyboardAction(action.VkCommand, isDown: true);
                if (action.VkCommand == VirtualKeyboardCommand.Select)
                    _nextRepeatAt[entry.Slot] = _totalSeconds + InitialRepeatDelaySeconds;
                break;
        }
    }

    private void OnButtonHeld(BindingEntry entry)
    {
        var action = entry.Action;
        bool isRepeatableVkSelect = action.Kind == ActionKind.VirtualKeyboard && action.VkCommand == VirtualKeyboardCommand.Select;
        if (!action.AutoRepeat && !isRepeatableVkSelect)
            return;

        if (!_nextRepeatAt.TryGetValue(entry.Slot, out double dueTime) || _totalSeconds < dueTime)
            return;

        _nextRepeatAt[entry.Slot] = _totalSeconds + RepeatIntervalSeconds;

        if (isRepeatableVkSelect)
        {
            VirtualKeyboard?.SelectHighlightedKey();
        }
        else if (action.Kind == ActionKind.KeyChord && action.Keys.Count > 0)
        {
            // Repeat the final key in the chord while keeping modifier keys held!
            Keys mainKey = action.Keys[^1];
            _input.KeyUp(mainKey);
            _input.KeyDown(mainKey);
        }
        else if (action.Kind == ActionKind.MouseScrollStep)
        {
            _input.MouseScrollVertical(action.ScrollDirection * ScrollStepDelta);
        }
    }

    private void OnButtonReleased(BindingEntry entry)
    {
        _nextRepeatAt.Remove(entry.Slot);
        var action = entry.Action;

        switch (action.Kind)
        {
            case ActionKind.MouseButton:
                if (_heldMouseButtons.Remove(action.MouseButton))
                    _input.MouseButton(action.MouseButton, down: false);
                break;

            case ActionKind.KeyChord:
                ReleaseChordUp(action.Keys);
                break;

            case ActionKind.VirtualKeyboard:
                ExecuteVirtualKeyboardAction(action.VkCommand, isDown: false);
                break;
        }
    }

    private readonly HashSet<Keys> _activeDirectionalStickKeys = new();

    private void ProcessStick(
        PadControl stickCtrl,
        StickVector vec,
        BindingProfileMap map,
        ModifierLayer activeLayer,
        double dtSeconds)
    {
        var entry = ResolveSlotEntry(map, activeLayer, stickCtrl);
        if (entry == null || entry.Action.Kind == ActionKind.None)
        {
            if (stickCtrl == PadControl.LeftStick)
                ReleaseStickDirectionalKeys();
            return;
        }

        switch (entry.Action.Kind)
        {
            case ActionKind.MouseMovement:
            {
                if (stickCtrl == PadControl.LeftStick)
                    ReleaseStickDirectionalKeys();

                var (dx, dy) = _mousePhysics.StepMovement(vec, dtSeconds, entry.Action.Inverted);
                if (dx != 0 || dy != 0)
                    _input.MouseMoveRelative(dx, dy);
                break;
            }

            case ActionKind.MouseScroll:
            {
                int wheel = _mousePhysics.StepScroll(vec, dtSeconds);
                if (wheel != 0)
                    _input.MouseScrollVertical(wheel);
                break;
            }

            case ActionKind.DirectionalKeys when entry.Action.Keys.Count == 4:
            {
                // Order verified from user's YuanShen.ccp ("key W S A D") and Workshop 596912498 ("key Up Down Left Right"):
                // [0] = Up (+Y), [1] = Down (-Y), [2] = Left (-X), [3] = Right (+X)
                Keys kUp = entry.Action.Keys[0];
                Keys kDown = entry.Action.Keys[1];
                Keys kLeft = entry.Action.Keys[2];
                Keys kRight = entry.Action.Keys[3];

                SetDirectionalKey(kUp, vec.Y >= DirectionalStickThreshold);
                SetDirectionalKey(kDown, vec.Y <= -DirectionalStickThreshold);
                SetDirectionalKey(kLeft, vec.X <= -DirectionalStickThreshold);
                SetDirectionalKey(kRight, vec.X >= DirectionalStickThreshold);
                break;
            }

            case ActionKind.VirtualKeyboard when entry.Action.VkCommand == VirtualKeyboardCommand.Move:
            {
                VirtualKeyboard?.UpdateStick(vec, dtSeconds);
                break;
            }
        }
    }

    private void SetDirectionalKey(Keys key, bool shouldHold)
    {
        if (shouldHold)
        {
            if (_activeDirectionalStickKeys.Add(key))
            {
                _heldPhysicalKeys.Add(key);
                _input.KeyDown(key);
            }
        }
        else
        {
            if (_activeDirectionalStickKeys.Remove(key))
            {
                _heldPhysicalKeys.Remove(key);
                _input.KeyUp(key);
            }
        }
    }

    private void ReleaseStickDirectionalKeys()
    {
        if (_activeDirectionalStickKeys.Count == 0)
            return;

        foreach (var k in _activeDirectionalStickKeys)
        {
            _heldPhysicalKeys.Remove(k);
            _input.KeyUp(k);
        }
        _activeDirectionalStickKeys.Clear();
    }

    private void ExecuteVirtualKeyboardAction(VirtualKeyboardCommand cmd, bool isDown)
    {
        var vk = VirtualKeyboard;
        if (vk == null)
            return;

        switch (cmd)
        {
            case VirtualKeyboardCommand.Show when isDown:
                vk.Show();
                break;
            case VirtualKeyboardCommand.Hide when isDown:
                vk.Hide();
                break;
            case VirtualKeyboardCommand.Select when isDown:
                vk.SelectHighlightedKey();
                break;
            case VirtualKeyboardCommand.Language when isDown:
                vk.CycleLanguage();
                break;
            case VirtualKeyboardCommand.Shift:
                vk.IsShiftActive = isDown;
                break;
            case VirtualKeyboardCommand.AltGr:
                vk.IsAltGrActive = isDown;
                break;
        }
    }

    private void PressChordDown(IReadOnlyList<Keys> keys)
    {
        foreach (var k in keys)
        {
            _heldPhysicalKeys.Add(k);
            _input.KeyDown(k);
        }
    }

    private void ReleaseChordUp(IReadOnlyList<Keys> keys)
    {
        for (int i = keys.Count - 1; i >= 0; i--)
        {
            var k = keys[i];
            _heldPhysicalKeys.Remove(k);
            _input.KeyUp(k);
        }
    }

    private void ReleaseAllHeldOutputs(bool hideVirtualKeyboard = false)
    {
        if (hideVirtualKeyboard && VirtualKeyboard?.IsVisible == true)
        {
            VirtualKeyboard.Hide();
        }

        foreach (var mb in _heldMouseButtons)
            _input.MouseButton(mb, down: false);
        _heldMouseButtons.Clear();

        foreach (var k in _heldPhysicalKeys)
            _input.KeyUp(k);
        _heldPhysicalKeys.Clear();
        _activeDirectionalStickKeys.Clear();
        _activeBoundSlotByControl.Clear();
        _nextRepeatAt.Clear();
        _mousePhysics.ResetMovement();
    }

    private void SyncPrevControls(NormalizedPadState pad)
    {
        _prevDownControls.Clear();
        foreach (var ctrl in PadSlot.AllControls)
        {
            if (!ctrl.IsStickControl() && pad.IsControlDown(ctrl))
                _prevDownControls.Add(ctrl);
        }
    }

    private void UpdateStatus(CompanionStatus newStatus, string profileLabel)
    {
        bool changed = newStatus != _lastStatus || !string.Equals(_activeProfileTitle, profileLabel, StringComparison.Ordinal);
        CompanionStatus prev = _lastStatus;
        _lastStatus = newStatus;
        _activeProfileTitle = profileLabel;

        if (changed)
        {
            StatusChanged?.Invoke(newStatus, profileLabel);

            if (prev == CompanionStatus.DisabledFullscreen && newStatus == CompanionStatus.Enabled &&
                _config.Settings.NotificationSettings.ShowBalloonTips)
            {
                BalloonNotificationRequested?.Invoke(
                    "Orbit",
                    "全屏结束后已自动恢复控制（全屏中可按 Back+Start 强制启用）");
            }
        }
    }

    private static void LaunchProcessSafe(string target, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                Arguments = args,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore launch failures
        }
    }

    public void Dispose() => Stop();
}

internal static class PadControlExtensions
{
    public static bool IsStickControl(this PadControl control)
        => control is PadControl.LeftStick or PadControl.RightStick;
}
