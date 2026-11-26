using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using OpenGamepadCompanion.App.Assets;
using OpenGamepadCompanion.Core.Config;
using OpenGamepadCompanion.Core.Models;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;

namespace OpenGamepadCompanion.App.Views;

public partial class ActionBindDialog : Window
{
    private readonly PadSlot _slot;
    private readonly BindingEntry? _defaultEntry;
    private readonly List<Keys> _recordedKeys = [];

    public BindingEntry? ResultEntry { get; private set; }

    public ActionBindDialog(PadSlot slot, BindingEntry? currentEntry, BindingEntry? defaultEntry)
    {
        InitializeComponent();
        _slot = slot;
        _defaultEntry = defaultEntry;

        SlotBadgeText.Text = slot.ToDisplayNameZh();
        CurrentBoundText.Text = currentEntry != null && currentEntry.Action.Kind != ActionKind.None
            ? KeyNameResolver.LocalizeCommandTitle(currentEntry.Title)
            : "未绑定";

        BtnClose.Content = VectorIcons.CreateIcon(VectorIcons.CloseX, 14, new SolidColorBrush(Color.FromRgb(148, 163, 184)));
        BtnClearSlot.Content = MakeIconLabel(VectorIcons.Trash, "解除绑定");
        BtnResetSlot.Content = MakeIconLabel(VectorIcons.ResetRotate, "恢复默认");

        TabKeyBtn.Content = MakeIconLabel(VectorIcons.KeyCap, "键盘录制");
        TabMouseBtn.Content = MakeIconLabel(VectorIcons.MouseBody, slot.IsStick ? "摇杆模式" : "鼠标动作");
        TabSystemBtn.Content = MakeIconLabel(VectorIcons.ShieldBolt, "快捷与媒体");

        if (currentEntry?.Action.Kind == ActionKind.KeyChord)
        {
            _recordedKeys.AddRange(currentEntry.Action.Keys);
            ToggleAutoRepeat.IsChecked = currentEntry.Action.AutoRepeat;
        }

        BuildSpecialKeyChips();
        BuildMouseTiles();
        BuildSystemTiles();
        RenderRecordedKeyCaps();

        // Default to Mouse/Stick tab if slot is a stick, otherwise Key Recorder
        SelectTab(slot.IsStick ? 1 : 0);
    }

    private void SelectTab(int index)
    {
        PaneKeyRecorder.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        PaneMouseTiles.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        PaneSystemTiles.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;

        HighlightTab(TabKeyBtn, index == 0);
        HighlightTab(TabMouseBtn, index == 1);
        HighlightTab(TabSystemBtn, index == 2);
    }

    private static void HighlightTab(Button btn, bool active)
    {
        btn.BorderBrush = active
            ? new SolidColorBrush(Color.FromRgb(56, 189, 248))
            : new SolidColorBrush(Color.FromRgb(38, 47, 66));
        btn.Background = active
            ? new SolidColorBrush(Color.FromRgb(27, 40, 59))
            : new SolidColorBrush(Color.FromRgb(24, 29, 42));
    }

    private void BuildSpecialKeyChips()
    {
        (string Label, Keys Key)[] specials =
        [
            ("+ Win", Keys.LWin),
            ("+ Ctrl", Keys.LControlKey),
            ("+ Shift", Keys.LShiftKey),
            ("+ Alt", Keys.LMenu),
            ("AltGr", Keys.RMenu),
            ("Enter", Keys.Return),
            ("Backspace", Keys.Back),
            ("Space", Keys.Space),
            ("Tab", Keys.Tab),
            ("Esc", Keys.Escape),
            ("F4", Keys.F4),
            ("F5", Keys.F5),
            ("F8", Keys.F8),
            ("F11", Keys.F11),
            ("清空录制", Keys.None)
        ];

        foreach (var (label, key) in specials)
        {
            var chip = new Button
            {
                Content = label,
                FontSize = 11.5,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Style = (Style)FindResource("IconPillButtonStyle")
            };
            chip.Click += (_, _) =>
            {
                if (key == Keys.None)
                {
                    _recordedKeys.Clear();
                }
                else if (!_recordedKeys.Contains(key))
                {
                    _recordedKeys.Add(key);
                }
                RenderRecordedKeyCaps();
            };
            QuickSpecialKeysPanel.Children.Add(chip);
        }
    }

    private void BuildMouseTiles()
    {
        (string Icon, string Label, string Title, string RawCmd)[] items = _slot.IsStick
            ?
            [
                (VectorIcons.CursorMove, "鼠标指针移动", "Mouse movement", "mouse movement"),
                (VectorIcons.CursorMove, "指针移动 (Y轴倒置)", "Mouse movement (Inverted)", "mouse movement inverted"),
                (VectorIcons.MouseScroll, "平滑滚轮滚动", "Mouse scroll", "mouse scroll"),
                (VectorIcons.Gamepad, "WSAD 方向移动", "WSAD keys", "key W S A D norepeat"),
                (VectorIcons.ArrowUp, "上下左右方向键", "Arrow keys", "key Up Down Left Right norepeat"),
                (VectorIcons.RadialKeyboard, "虚拟键盘选键摇杆", "Virtual Keyboard Input", "virtualkeyboard move")
            ]
            :
            [
                (VectorIcons.MouseLeft, "鼠标左键", "Left mouse button", "mouse left"),
                (VectorIcons.MouseRight, "鼠标右键", "Right mouse button", "mouse right"),
                (VectorIcons.MouseBody, "鼠标中键", "Middle mouse button", "mouse middle"),
                (VectorIcons.ArrowLeft, "鼠标侧键 4 (后退)", "4th mouse button", "mouse x1"),
                (VectorIcons.ArrowRight, "鼠标侧键 5 (前进)", "5th mouse button", "mouse x2"),
                (VectorIcons.MouseScroll, "滚轮向上一步", "Mouse scroll up", "mouse scroll up norepeat"),
                (VectorIcons.MouseScroll, "滚轮向下一步", "Mouse scroll down", "mouse scroll down norepeat"),
                (VectorIcons.MotionCurve, "按住减速 50% (微操)", "Mouse speed 50%", "mouse speed 50%"),
                (VectorIcons.MotionCurve, "按住减速 25% (精瞄)", "Mouse speed 25%", "mouse speed 25%"),
                (VectorIcons.MotionCurve, "按住加速 150%", "Mouse speed 150%", "mouse speed 150%")
            ];

        foreach (var item in items)
            MouseTilesWrap.Children.Add(CreateActionTile(item.Icon, item.Label, item.Title, item.RawCmd));
    }

    private void BuildSystemTiles()
    {
        (string Icon, string Label, string Title, string RawCmd)[] items =
        [
            (VectorIcons.RadialKeyboard, "显示虚拟键盘", "Show Virtual Keyboard", "virtualkeyboard show"),
            (VectorIcons.RadialKeyboard, "隐藏虚拟键盘", "Hide Virtual Keyboard", "virtualkeyboard hide"),
            (VectorIcons.RadialKeyboard, "键盘确认输入", "Virtual Keyboard Select", "virtualkeyboard select"),
            (VectorIcons.RadialKeyboard, "键盘按住大写", "Virtual Keyboard Shift", "virtualkeyboard shift"),
            (VectorIcons.RadialKeyboard, "键盘符号层", "Virtual Keyboard AltGr", "virtualkeyboard altgr"),
            (VectorIcons.RadialKeyboard, "键盘切换中/英", "Cycle Virtual Keyboard Language", "virtualkeyboard language"),
            (VectorIcons.LayerSecondary, "按住切换副层 (Back)", "Use Secondary Bindings", "modifier back"),
            (VectorIcons.WindowSwitch, "任务切换视图", "Open Task Switcher (Ctrl+Alt+Tab)", "cmd keyCombo LControlKey LMenu Tab"),
            (VectorIcons.DesktopMonitor, "最小化所有窗口", "Minimize all windows", "cmd keyCombo LWin M"),
            (VectorIcons.ScreenSwitch, "仅电脑主屏幕", "Display PC screen only", "cmd process displayswitch.exe /internal"),
            (VectorIcons.ScreenSwitch, "仅第二外接屏", "Display second screen only", "cmd process displayswitch.exe /external"),
            (VectorIcons.ScreenSwitch, "扩展双屏桌面", "Display extend desktop", "cmd process displayswitch.exe /extend"),
            (VectorIcons.MediaPlay, "播放 / 暂停", "Media play/pause", "key MediaPlayPause norepeat"),
            (VectorIcons.MediaPrev, "上一曲目", "Media previous track", "key MediaPreviousTrack norepeat"),
            (VectorIcons.MediaNext, "下一曲目", "Media next track", "key MediaNextTrack norepeat"),
            (VectorIcons.VolumeUp, "音量增大", "Volume up", "key VolumeUp"),
            (VectorIcons.VolumeDown, "音量减小", "Volume down", "key VolumeDown"),
            (VectorIcons.SoundWave, "静音切换", "Volume mute", "key VolumeMute norepeat"),
            (VectorIcons.HelpCircle, "悬浮按键提示", "Toggle help", "cmd togglehelp"),
            (VectorIcons.PowerOff, "关闭无线手柄", "Turn off controller", "cmd turnOffController"),
            (VectorIcons.SparkleSteam, "Steam 大屏幕", "Launch Steam Big-Picture", "cmd process steam://open/bigpicture")
        ];

        foreach (var item in items)
            SystemTilesWrap.Children.Add(CreateActionTile(item.Icon, item.Label, item.Title, item.RawCmd));

        // Custom program launcher tile
        var customExeBtn = new Button
        {
            Width = 162,
            Height = 58,
            Margin = new Thickness(0, 0, 8, 8),
            Style = (Style)FindResource("IconPillButtonStyle"),
            Content = MakeTileContent(VectorIcons.FolderOpen, "启动自定义程序…")
        };
        customExeBtn.Click += (_, _) =>
        {
            var ofd = new OpenFileDialog { Title = "选择要一键启动的程序", Filter = "可执行程序 (*.exe)|*.exe|所有文件 (*.*)|*.*" };
            if (ofd.ShowDialog() == true)
            {
                string name = Path.GetFileNameWithoutExtension(ofd.FileName);
                CommitAndClose($"启动 {name}", $"cmd process \"{ofd.FileName}\"");
            }
        };
        SystemTilesWrap.Children.Add(customExeBtn);
    }

    private Button CreateActionTile(string iconPath, string label, string title, string rawCmd)
    {
        var btn = new Button
        {
            Width = 162,
            Height = 58,
            Margin = new Thickness(0, 0, 8, 8),
            Style = (Style)FindResource("IconPillButtonStyle"),
            Content = MakeTileContent(iconPath, label)
        };
        btn.Click += (_, _) => CommitAndClose(title, rawCmd);
        return btn;
    }

    private static UIElement MakeTileContent(string iconPath, string label)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(VectorIcons.CreateIcon(iconPath, 18, new SolidColorBrush(Color.FromRgb(56, 189, 248))));
        sp.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0)
        });
        return sp;
    }

    private static UIElement MakeIconLabel(string iconPath, string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(VectorIcons.CreateIcon(iconPath, 15, new SolidColorBrush(Color.FromRgb(226, 232, 240))));
        sp.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        return sp;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (PaneKeyRecorder.Visibility != Visibility.Visible)
            return;

        Key wpfKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (wpfKey == Key.None)
            return;

        e.Handled = true;
        int vk = KeyInterop.VirtualKeyFromKey(wpfKey);
        var wfKey = NormalizeWinFormsKey((Keys)vk, wpfKey);
        if (wfKey == Keys.None)
            return;

        // Start fresh recording if previous non-modifier key was already finished and no modifiers are currently held
        bool anyHeldMod = Keyboard.Modifiers != ModifierKeys.None;
        if (!anyHeldMod && _recordedKeys.Count > 0 && !IsModifierKey(wfKey))
        {
            _recordedKeys.Clear();
        }

        if (!_recordedKeys.Contains(wfKey))
            _recordedKeys.Add(wfKey);

        RenderRecordedKeyCaps();
    }

    private static bool IsModifierKey(Keys k) =>
        k is Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;

    private static Keys NormalizeWinFormsKey(Keys raw, Key wpfKey) => wpfKey switch
    {
        Key.LeftCtrl => Keys.LControlKey,
        Key.RightCtrl => Keys.RControlKey,
        Key.LeftShift => Keys.LShiftKey,
        Key.RightShift => Keys.RShiftKey,
        Key.LeftAlt => Keys.LMenu,
        Key.RightAlt => Keys.RMenu,
        Key.LWin => Keys.LWin,
        Key.RWin => Keys.RWin,
        _ => raw
    };

    private void RenderRecordedKeyCaps()
    {
        KeyCapsPanel.Children.Clear();
        if (_recordedKeys.Count == 0)
        {
            KeyCapsPanel.Children.Add(new TextBlock
            {
                Text = "等待按键输入…",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
            });
            return;
        }

        for (int i = 0; i < _recordedKeys.Count; i++)
        {
            if (i > 0)
            {
                KeyCapsPanel.Children.Add(new TextBlock
                {
                    Text = "+",
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0)
                });
            }

            KeyCapsPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 6, 12, 6),
                Child = new TextBlock
                {
                    Text = KeyNameResolver.ToFriendlyKeyName(_recordedKeys[i]),
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                }
            });
        }
    }

    private void OnConfirmRecordedKeyClick(object sender, RoutedEventArgs e)
    {
        if (_recordedKeys.Count == 0)
            return;

        string joinedRaw = string.Join('+', _recordedKeys.Select(k => k.ToString()));
        string friendlyTitle = string.Join("+", _recordedKeys.Select(KeyNameResolver.ToFriendlyKeyName));
        bool repeat = ToggleAutoRepeat.IsChecked == true;
        string rawCmd = repeat ? $"key {joinedRaw}" : $"key {joinedRaw} norepeat";

        CommitAndClose(friendlyTitle, rawCmd);
    }

    private void CommitAndClose(string title, string rawCmd)
    {
        ResultEntry = new BindingEntry
        {
            Slot = _slot,
            Title = title,
            RawCommand = rawCmd,
            Action = DirectiveParser.ParseCommand(rawCmd)
        };
        DialogResult = true;
    }

    private void OnClearSlotClick(object sender, RoutedEventArgs e) => CommitAndClose("Nothing", "null");

    private void OnResetSlotClick(object sender, RoutedEventArgs e)
    {
        if (_defaultEntry != null)
        {
            ResultEntry = _defaultEntry.Clone();
            DialogResult = true;
        }
        else
        {
            CommitAndClose("Nothing", "null");
        }
    }

    private void OnRecorderBoxClick(object sender, MouseButtonEventArgs e) => _recordedKeys.Clear();
    private void OnTabKeyClick(object sender, RoutedEventArgs e) => SelectTab(0);
    private void OnTabMouseClick(object sender, RoutedEventArgs e) => SelectTab(1);
    private void OnTabSystemClick(object sender, RoutedEventArgs e) => SelectTab(2);
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    private void OnDragBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
