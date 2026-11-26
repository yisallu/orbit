using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using OpenGamepadCompanion.App.Assets;
using OpenGamepadCompanion.Core.Config;
using OpenGamepadCompanion.Core.Engine;
using OpenGamepadCompanion.Core.Input;
using OpenGamepadCompanion.Core.Models;
using OpenGamepadCompanion.Core.SystemIntegration;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using DataFormats = System.Windows.DataFormats;
using DragEventArgs = System.Windows.DragEventArgs;
using Image = System.Windows.Controls.Image;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Orientation = System.Windows.Controls.Orientation;
using Path = System.IO.Path;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using ShapePath = System.Windows.Shapes.Path;

namespace OpenGamepadCompanion.App.Views;

public enum BlueprintTargetKind
{
    Desktop,
    VirtualKeyboard,
    ProcessProfile
}

public partial class MainWindow : Window
{
    private readonly ConfigManager _config;
    private readonly CompanionEngine _engine;
    private readonly ActiveWindowTracker _windowTracker;
    private readonly SpiralKeyboardWindow _keyboardWindow;
    private readonly XboxShareVoiceService _shareVoice;
    private readonly DispatcherTimer _liveTimer;

    private bool _loadingUi;
    private int _currentNavIndex;
    private BlueprintTargetKind _targetKind = BlueprintTargetKind.Desktop;
    private ProcessProfile? _selectedProfile;
    private ModifierLayer _activeBlueprintLayer = ModifierLayer.Primary;
    private PadControl? _hoveredControl;

    private readonly Dictionary<PadControl, Shape> _diagramParts = new();
    private Ellipse? _leftStickDiagramDot;
    private Ellipse? _rightStickDiagramDot;
    private Ellipse? _leftRadarLiveDot;
    private Ellipse? _rightRadarLiveDot;

    private static readonly SolidColorBrush AppleBlueBrush = new(Color.FromRgb(10, 132, 255));
    private static readonly SolidColorBrush AppleGreenBrush = new(Color.FromRgb(48, 209, 88));
    private static readonly SolidColorBrush ApplePrimaryText = new(Color.FromRgb(245, 245, 247));
    private static readonly SolidColorBrush AppleSecondaryText = new(Color.FromRgb(152, 152, 159));

    private static readonly (PadControl Ctrl, string KeyBadge, string Label)[] LeftColumnControls =
    [
        (PadControl.LeftTrigger, "LT", "左扳机"),
        (PadControl.LeftShoulder, "LB", "左肩键"),
        (PadControl.LeftStick, "LS", "左摇杆移动"),
        (PadControl.LeftThumb, "L3", "左摇杆按下"),
        (PadControl.DPadUp, "↑", "十字键 上"),
        (PadControl.DPadDown, "↓", "十字键 下"),
        (PadControl.DPadLeft, "←", "十字键 左"),
        (PadControl.DPadRight, "→", "十字键 右"),
        (PadControl.BackButton, "⧉", "Back 键")
    ];

    private static readonly (PadControl Ctrl, string KeyBadge, string Label)[] RightColumnControls =
    [
        (PadControl.RightTrigger, "RT", "右扳机"),
        (PadControl.RightShoulder, "RB", "右肩键"),
        (PadControl.Y, "Y", "Y 键"),
        (PadControl.B, "B", "B 键"),
        (PadControl.A, "A", "A 键"),
        (PadControl.X, "X", "X 键"),
        (PadControl.RightStick, "RS", "右摇杆移动"),
        (PadControl.RightThumb, "R3", "右摇杆按下"),
        (PadControl.Start, "≡", "Start 键"),
        (PadControl.Guide, "⊕", "Xbox 导航键")
    ];

    public MainWindow(
        ConfigManager config,
        CompanionEngine engine,
        ActiveWindowTracker windowTracker,
        SpiralKeyboardWindow keyboardWindow,
        XboxShareVoiceService shareVoice)
    {
        _loadingUi = true;
        InitializeComponent();
        _config = config;
        _engine = engine;
        _windowTracker = windowTracker;
        _keyboardWindow = keyboardWindow;
        _shareVoice = shareVoice;

        InitializeIconsAndControls();
        SyncUiFromSettings();
        RebuildAppsGrid();
        SelectNavPage(0);
        RenderControllerBlueprint();
        RenderVelocityCurve();
        RenderDeadzoneRadars();

        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _liveTimer.Tick += (_, _) => UpdateLiveTelemetry();
        _liveTimer.Start();

        Closed += (_, _) => _liveTimer.Stop();
        _loadingUi = false;
    }

    private void InitializeIconsAndControls()
    {
        BrandLogoHost.Content = VectorIcons.CreateIcon(VectorIcons.OrbitBrand, 18, AppleBlueBrush, 2.0);
        TopMinimizeBtn.Content = VectorIcons.CreateIcon(VectorIcons.CloseX, 14, AppleSecondaryText);

        SidebarKeyboardBtn.Content = MakeButtonContent(VectorIcons.RadialKeyboard, "弹出虚拟键盘 (LS)", ApplePrimaryText);
        SidebarQuitBtn.Content = MakeButtonContent(VectorIcons.PowerOff, "彻底退出后台", new SolidColorBrush(Color.FromRgb(255, 105, 97)));

        ResetSpeedBtn.Content = VectorIcons.CreateIcon(VectorIcons.ResetRotate, 13, AppleSecondaryText);
        ResetAccelBtn.Content = VectorIcons.CreateIcon(VectorIcons.ResetRotate, 13, AppleSecondaryText);
        ResetWheelBtn.Content = VectorIcons.CreateIcon(VectorIcons.ResetRotate, 13, AppleSecondaryText);
        ResetLeftDzBtn.Content = VectorIcons.CreateIcon(VectorIcons.ResetRotate, 13, AppleSecondaryText);
        ResetRightDzBtn.Content = VectorIcons.CreateIcon(VectorIcons.ResetRotate, 13, AppleSecondaryText);

        BtnAddAppExe.Content = MakeButtonContent(VectorIcons.Plus, "添加应用", Brushes.White);
        BtnAddAppRecent.Content = MakeButtonContent(VectorIcons.SparkleSteam, "从最近运行选择", ApplePrimaryText);

        BtnRegisterSystemTask.Content = MakeButtonContent(VectorIcons.ShieldBolt, "写入系统开机自启", Brushes.White);
        BtnSyncOriginal.Content = MakeButtonContent(VectorIcons.SyncRefresh, "从原版同步配置", ApplePrimaryText);
        BtnOpenConfigDir.Content = MakeButtonContent(VectorIcons.FolderOpen, "打开配置文件夹", ApplePrimaryText);
    }

    private static UIElement MakeButtonContent(string iconPath, string text, System.Windows.Media.Brush fg)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(VectorIcons.CreateIcon(iconPath, 15, fg, 1.9));
        sp.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = fg,
            FontSize = 12.5,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        return sp;
    }

    public void SelectNavPage(int index)
    {
        _currentNavIndex = index;
        ViewCanvas.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        ViewFeel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        ViewApps.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        ViewSystem.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;

        HomeTopControlsBar.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageSectionTitle.Text = index switch
        {
            0 => "按键映射",
            1 => "摇杆与指针",
            2 => "应用专属配置",
            3 => "通用设置",
            _ => "Orbit"
        };

        StyleSidebarRow(NavBtnCanvas, VectorIcons.Gamepad, "按键映射", null, index == 0);
        StyleSidebarRow(NavBtnFeel, VectorIcons.MotionCurve, "摇杆与指针", null, index == 1);
        StyleSidebarRow(NavBtnApps, VectorIcons.AppsGrid, "应用专属", _config.Profiles.Count.ToString(), index == 2);
        StyleSidebarRow(NavBtnSystem, VectorIcons.SettingsSliders, "通用设置", null, index == 3);
    }

    private static void StyleSidebarRow(Button btn, string icon, string title, string? badge, bool active)
    {
        btn.Background = active
            ? new SolidColorBrush(Color.FromRgb(46, 46, 54))
            : Brushes.Transparent;

        var dock = new DockPanel();
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(VectorIcons.CreateIcon(icon, 16, active ? AppleBlueBrush : AppleSecondaryText, 1.9));
        left.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13.5,
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = active ? ApplePrimaryText : new SolidColorBrush(Color.FromRgb(209, 209, 214)),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        dock.Children.Add(left);

        if (!string.IsNullOrEmpty(badge))
        {
            var badgeEl = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(54, 54, 62)),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(7, 1, 7, 1),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = badge, FontSize = 11, Foreground = AppleSecondaryText }
            };
            dock.Children.Add(badgeEl);
        }

        btn.Content = dock;
    }

    // =========================================================================
    // VIEW 0: APPLE SPLIT CALLOUT LISTS + INTERACTIVE CONTROLLER DIAGRAM
    // =========================================================================
    private BindingProfileMap GetEditingMap() => _targetKind switch
    {
        BlueprintTargetKind.VirtualKeyboard => _config.KeyboardBindings,
        BlueprintTargetKind.ProcessProfile when _selectedProfile != null => _selectedProfile.BuildRuntimeMap(),
        _ => _config.DesktopBindings
    };

    private BindingProfileMap GetDefaultMapForTarget() => _targetKind switch
    {
        BlueprintTargetKind.VirtualKeyboard => DefaultBindings.CreateDefaultKeyboardModeMap(),
        _ => DefaultBindings.CreateDefaultMouseModeMap()
    };

    public void RenderControllerBlueprint()
    {
        UpdateSegmentedControls();
        var map = GetEditingMap();

        // 1. Populate Left & Right Apple Grouped Callout Lists
        LeftMappingRowsPanel.Children.Clear();
        var leftItems = LeftColumnControls
            .Where(x => !(_activeBlueprintLayer == ModifierLayer.Back && x.Ctrl == PadControl.BackButton))
            .ToArray();
        for (int i = 0; i < leftItems.Length; i++)
        {
            var item = leftItems[i];
            LeftMappingRowsPanel.Children.Add(CreateCalloutRow(item.Ctrl, item.KeyBadge, item.Label, map, i < leftItems.Length - 1));
        }

        RightMappingRowsPanel.Children.Clear();
        for (int i = 0; i < RightColumnControls.Length; i++)
        {
            var item = RightColumnControls[i];
            RightMappingRowsPanel.Children.Add(CreateCalloutRow(item.Ctrl, item.KeyBadge, item.Label, map, i < RightColumnControls.Length - 1));
        }

        // 2. Render Clean Central Controller Hardware Diagram
        DrawCenterControllerDiagram();
    }

    private UIElement CreateCalloutRow(
        PadControl ctrl,
        string keyBadge,
        string humanLabel,
        BindingProfileMap map,
        bool showBottomDivider)
    {
        var slot = new PadSlot(_activeBlueprintLayer, ctrl);
        var entry = map.TryGet(slot);
        if (entry == null && ctrl is PadControl.LeftStick or PadControl.RightStick && _activeBlueprintLayer != ModifierLayer.Primary)
            entry = map.TryGet(ModifierLayer.Primary, ctrl);

        bool bound = entry != null && entry.Action.Kind != ActionKind.None;
        string actionDisplay = bound
            ? KeyNameResolver.LocalizeCommandTitle(entry!.Title)
            : "未设置";

        var wrapper = new StackPanel();
        var rowBorder = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(12, 8, 12, 8),
            Cursor = Cursors.Hand
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });

        // Keycap Hardware Pill
        var cap = new Border
        {
            Height = 22,
            MinWidth = 26,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromRgb(48, 48, 56)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 80)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 0, 4, 0),
            Child = new TextBlock
            {
                Text = keyBadge,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = ApplePrimaryText,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var nameText = new TextBlock
        {
            Text = _activeBlueprintLayer == ModifierLayer.Back ? $"Back+{humanLabel}" : humanLabel,
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 226)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var actionText = new TextBlock
        {
            Text = actionDisplay,
            FontSize = 12.5,
            FontWeight = bound ? FontWeights.Medium : FontWeights.Normal,
            Foreground = bound ? AppleBlueBrush : new SolidColorBrush(Color.FromRgb(99, 99, 104)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 4, 0)
        };

        var chevron = new TextBlock
        {
            Text = "›",
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(99, 99, 104)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(cap, 0);
        Grid.SetColumn(nameText, 1);
        Grid.SetColumn(actionText, 2);
        Grid.SetColumn(chevron, 3);
        grid.Children.Add(cap);
        grid.Children.Add(nameText);
        grid.Children.Add(actionText);
        grid.Children.Add(chevron);
        rowBorder.Child = grid;

        rowBorder.MouseEnter += (_, _) =>
        {
            rowBorder.Background = new SolidColorBrush(Color.FromRgb(44, 44, 52));
            SetDiagramHighlight(ctrl, true);
        };
        rowBorder.MouseLeave += (_, _) =>
        {
            rowBorder.Background = Brushes.Transparent;
            SetDiagramHighlight(ctrl, false);
        };
        rowBorder.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenBindDialogForSlot(slot);
        };

        wrapper.Children.Add(rowBorder);
        if (showBottomDivider)
        {
            wrapper.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(44, 44, 50)),
                Margin = new Thickness(44, 0, 12, 0)
            });
        }

        return wrapper;
    }

    private void DrawCenterControllerDiagram()
    {
        ControllerBlueprintCanvas.Children.Clear();
        _diagramParts.Clear();

        // Top Triggers LT / RT
        AddDiagramRect(64, 24, 38, 16, 6, PadControl.LeftTrigger);
        AddDiagramRect(198, 24, 38, 16, 6, PadControl.RightTrigger);

        // Top Bumpers LB / RB
        AddDiagramRect(58, 45, 48, 14, 6, PadControl.LeftShoulder);
        AddDiagramRect(194, 45, 48, 14, 6, PadControl.RightShoulder);

        // Controller Ergonomic Body Shell
        var shell = new ShapePath
        {
            Data = Geometry.Parse(
                "M 68,62 C 102,56 130,60 150,60 C 170,60 198,56 232,62 " +
                "C 262,68 280,104 286,154 C 292,198 280,234 256,238 " +
                "C 234,242 218,214 202,192 C 192,178 176,182 150,182 " +
                "C 124,182 108,178 98,192 C 82,214 66,242 44,238 " +
                "C 20,234 8,198 14,154 C 20,104 38,68 68,62 Z"),
            Fill = new SolidColorBrush(Color.FromRgb(32, 32, 38)),
            Stroke = new SolidColorBrush(Color.FromRgb(64, 64, 74)),
            StrokeThickness = 1.8
        };
        ControllerBlueprintCanvas.Children.Add(shell);

        // Guide, Back, Start
        AddDiagramCircle(150, 86, 18, PadControl.Guide);
        AddDiagramCircle(124, 108, 12, PadControl.BackButton);
        AddDiagramCircle(176, 108, 12, PadControl.Start);

        // Left Stick + L3
        AddDiagramCircle(84, 112, 34, PadControl.LeftStick);
        AddDiagramCircle(84, 112, 14, PadControl.LeftThumb);
        _leftStickDiagramDot = new Ellipse { Width = 6, Height = 6, Fill = AppleBlueBrush, IsHitTestVisible = false };
        Canvas.SetLeft(_leftStickDiagramDot, 81);
        Canvas.SetTop(_leftStickDiagramDot, 109);
        ControllerBlueprintCanvas.Children.Add(_leftStickDiagramDot);

        // Right Stick + R3
        AddDiagramCircle(184, 154, 34, PadControl.RightStick);
        AddDiagramCircle(184, 154, 14, PadControl.RightThumb);
        _rightStickDiagramDot = new Ellipse { Width = 6, Height = 6, Fill = AppleBlueBrush, IsHitTestVisible = false };
        Canvas.SetLeft(_rightStickDiagramDot, 181);
        Canvas.SetTop(_rightStickDiagramDot, 151);
        ControllerBlueprintCanvas.Children.Add(_rightStickDiagramDot);

        // D-Pad Cross (Lower-Left)
        AddDiagramRect(109, 136, 14, 14, 3, PadControl.DPadUp);
        AddDiagramRect(109, 164, 14, 14, 3, PadControl.DPadDown);
        AddDiagramRect(95, 150, 14, 14, 3, PadControl.DPadLeft);
        AddDiagramRect(123, 150, 14, 14, 3, PadControl.DPadRight);

        // ABXY Face Buttons (Upper-Right)
        AddDiagramLabeledButton(218, 90, 18, "Y", PadControl.Y);
        AddDiagramLabeledButton(198, 110, 18, "X", PadControl.X);
        AddDiagramLabeledButton(238, 110, 18, "B", PadControl.B);
        AddDiagramLabeledButton(218, 130, 18, "A", PadControl.A);
    }

    private void AddDiagramCircle(double cx, double cy, double diameter, PadControl ctrl)
    {
        var el = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = new SolidColorBrush(Color.FromRgb(44, 44, 52)),
            Stroke = new SolidColorBrush(Color.FromRgb(82, 82, 94)),
            StrokeThickness = 1.4,
            Cursor = Cursors.Hand
        };
        WireDiagramPart(el, ctrl);
        Canvas.SetLeft(el, cx - diameter / 2);
        Canvas.SetTop(el, cy - diameter / 2);
        ControllerBlueprintCanvas.Children.Add(el);
        _diagramParts[ctrl] = el;
    }

    private void AddDiagramRect(double x, double y, double w, double h, double radius, PadControl ctrl)
    {
        var r = new Rectangle
        {
            Width = w,
            Height = h,
            RadiusX = radius,
            RadiusY = radius,
            Fill = new SolidColorBrush(Color.FromRgb(44, 44, 52)),
            Stroke = new SolidColorBrush(Color.FromRgb(82, 82, 94)),
            StrokeThickness = 1.4,
            Cursor = Cursors.Hand
        };
        WireDiagramPart(r, ctrl);
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        ControllerBlueprintCanvas.Children.Add(r);
        _diagramParts[ctrl] = r;
    }

    private void AddDiagramLabeledButton(double cx, double cy, double diameter, string letter, PadControl ctrl)
    {
        AddDiagramCircle(cx, cy, diameter, ctrl);
        var txt = new TextBlock
        {
            Text = letter,
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = AppleSecondaryText,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(txt, cx - 3.5);
        Canvas.SetTop(txt, cy - 6.5);
        ControllerBlueprintCanvas.Children.Add(txt);
    }

    private void WireDiagramPart(Shape shape, PadControl ctrl)
    {
        shape.MouseEnter += (_, _) => SetDiagramHighlight(ctrl, true);
        shape.MouseLeave += (_, _) => SetDiagramHighlight(ctrl, false);
        shape.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!(_activeBlueprintLayer == ModifierLayer.Back && ctrl == PadControl.BackButton))
                OpenBindDialogForSlot(new PadSlot(_activeBlueprintLayer, ctrl));
        };
    }

    private void SetDiagramHighlight(PadControl ctrl, bool highlight)
    {
        _hoveredControl = highlight ? ctrl : null;
        if (_diagramParts.TryGetValue(ctrl, out var part))
        {
            part.Fill = highlight
                ? AppleBlueBrush
                : new SolidColorBrush(Color.FromRgb(44, 44, 52));
            part.Stroke = highlight
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(82, 82, 94));
        }
    }

    // Kept for preview render harness compatibility
    public void ShowHoverCapsule(PadSlot slot, BindingEntry? entry)
    {
        SetDiagramHighlight(slot.Control, true);
    }

    private void OpenBindDialogForSlot(PadSlot slot)
    {
        var map = GetEditingMap().Clone();
        var defMap = GetDefaultMapForTarget();
        var current = map.TryGet(slot);
        var def = defMap.TryGet(slot);

        var dlg = new ActionBindDialog(slot, current, def) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.ResultEntry != null)
        {
            map.Set(dlg.ResultEntry);
            PersistEditedMap(map);
            RenderControllerBlueprint();
        }
    }

    private void PersistEditedMap(BindingProfileMap updatedMap)
    {
        switch (_targetKind)
        {
            case BlueprintTargetKind.Desktop:
                _config.SaveDesktopBindings(updatedMap);
                break;

            case BlueprintTargetKind.VirtualKeyboard:
                _config.SaveKeyboardBindings(updatedMap);
                break;

            case BlueprintTargetKind.ProcessProfile when _selectedProfile != null:
                _selectedProfile.Bindings = updatedMap.Entries.Values.Select(x => x.Clone()).ToList();
                _config.SaveProcessProfile(_selectedProfile);
                RebuildAppsGrid();
                break;
        }
    }

    private void UpdateSegmentedControls()
    {
        SetSegmentActive(TargetDesktopBtn, _targetKind == BlueprintTargetKind.Desktop);
        SetSegmentActive(TargetKeyboardBtn, _targetKind == BlueprintTargetKind.VirtualKeyboard);
        SetSegmentActive(TargetAppMenuBtn, _targetKind == BlueprintTargetKind.ProcessProfile);

        TargetAppMenuBtn.Content = _targetKind == BlueprintTargetKind.ProcessProfile && _selectedProfile != null
            ? $"应用: {_selectedProfile.Title} ▾"
            : "选择应用 ▾";

        SetSegmentActive(LayerPrimaryBtn, _activeBlueprintLayer == ModifierLayer.Primary);
        SetSegmentActive(LayerBackBtn, _activeBlueprintLayer == ModifierLayer.Back);
    }

    private static void SetSegmentActive(Button btn, bool active)
    {
        btn.Background = active
            ? new SolidColorBrush(Color.FromRgb(58, 58, 68))
            : Brushes.Transparent;
        btn.Foreground = active ? ApplePrimaryText : AppleSecondaryText;
        btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void OnTargetAppMenuClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(36, 36, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(58, 58, 68)),
            Foreground = ApplePrimaryText
        };

        foreach (var p in _config.Profiles.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new MenuItem
            {
                Header = $"{p.Title} ({p.PrimaryProcessName})",
                Foreground = ApplePrimaryText
            };
            var captured = p;
            item.Click += (_, _) =>
            {
                _selectedProfile = captured;
                _targetKind = BlueprintTargetKind.ProcessProfile;
                RenderControllerBlueprint();
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var manageItem = new MenuItem { Header = "管理全部应用专属配置…", Foreground = AppleBlueBrush };
        manageItem.Click += (_, _) => SelectNavPage(2);
        menu.Items.Add(manageItem);

        menu.PlacementTarget = TargetAppMenuBtn;
        menu.IsOpen = true;
    }

    // =========================================================================
    // VIEW 1: MOTION CURVE & DEADZONE RADARS
    // =========================================================================
    private void SyncUiFromSettings()
    {
        _loadingUi = true;
        var s = _config.Settings;

        SliderMouseSpeed.Value = s.MouseSettings.Speed;
        SliderMouseAccel.Value = s.MouseSettings.Acceleration;
        SliderWheelSpeed.Value = s.MouseSettings.WheelSpeed;
        SliderLeftDz.Value = s.ControllerSettings.LeftStickDeadZone;
        SliderRightDz.Value = s.ControllerSettings.RightStickDeadZone;

        ToggleAdmin.IsChecked = s.StartupSettings.RunOnAdminAndLockScreen;
        ToggleStartup.IsChecked = s.StartupSettings.RunOnAdminAndLockScreen || s.StartupSettings.RunOnStartup;
        ToggleStartup.IsEnabled = !s.StartupSettings.RunOnAdminAndLockScreen;

        ToggleSound.IsChecked = s.NotificationSettings.EnableSoundsOnDisable;
        TogglePerProc.IsChecked = s.DisablerSettings.PerProcessDisable;
        ToggleBalloon.IsChecked = s.NotificationSettings.ShowBalloonTips;
        ToggleSpiralRing.IsChecked = s.KeyboardSettings.Type == VirtualKeyboardType.Spiral;
        ToggleSteam.IsChecked = s.SteamSettings.ShowInAppInSteam;
        ToggleBlockGameBar.IsChecked = !StartupManager.IsWindowsGameBarCapturingGuideButton();
        ToggleShareVoice.IsChecked = s.ShareVoiceSettings.Enabled;
        ToggleShareTapMode.IsChecked = s.ShareVoiceSettings.TapToggleMode;

        UpdateShareVoiceSubtitle();
        UpdateFeelReadouts();
        UpdateListButtonBadges();
        _loadingUi = false;
    }

    private void UpdateShareVoiceSubtitle()
    {
        if (ShareVoiceDriverDesc == null || _shareVoice == null) return;
        string mode = _shareVoice.IsHardwareMode ? "硬件级直通 (Interception 已就绪)" : "兼容模式 (重启系统后启用硬件直通)";
        ShareVoiceDriverDesc.Text = $"引擎：{mode} · XUSB 手柄 {_shareVoice.ActiveControllerCount} 台 · 本次累计触发 {_shareVoice.TriggerCount} 次";
    }

    private void UpdateFeelReadouts()
    {
        if (ValMouseSpeed == null || ValRightDz == null) return;

        ValMouseSpeed.Text = $"{(int)Math.Round(SliderMouseSpeed.Value)} px/s";
        ValMouseAccel.Text = SliderMouseAccel.Value < 10 ? "已关闭" : $"+{(int)Math.Round(SliderMouseAccel.Value)}";
        ValWheelSpeed.Text = $"{(int)Math.Round(SliderWheelSpeed.Value)}";

        ValLeftDz.Text = $"{Math.Round(SliderLeftDz.Value * 100.0 / 32767.0)}%";
        ValRightDz.Text = $"{Math.Round(SliderRightDz.Value * 100.0 / 32767.0)}%";
    }

    private void RenderVelocityCurve()
    {
        if (VelocityCurveCanvas == null) return;
        VelocityCurveCanvas.Children.Clear();

        double w = 380, h = 136;
        for (int i = 1; i <= 3; i++)
        {
            double gy = h * i / 4.0;
            VelocityCurveCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = gy, X2 = w, Y2 = gy,
                Stroke = new SolidColorBrush(Color.FromRgb(38, 38, 44)),
                StrokeThickness = 1
            });
        }

        double threshX = w * 0.82;
        VelocityCurveCanvas.Children.Add(new Line
        {
            X1 = threshX, Y1 = 8, X2 = threshX, Y2 = h - 8,
            Stroke = new SolidColorBrush(Color.FromArgb(110, 142, 142, 147)),
            StrokeThickness = 1,
            StrokeDashArray = [3, 3]
        });

        double speed = SliderMouseSpeed.Value;
        double accel = SliderMouseAccel.Value;
        double maxPlot = 4200.0;

        var poly = new Polyline
        {
            Stroke = AppleBlueBrush,
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round
        };

        for (int i = 0; i <= 60; i++)
        {
            double t = i / 60.0;
            double v = t <= 0.82
                ? (t / 0.82) * speed
                : speed + ((t - 0.82) / 0.18) * accel * 0.75;

            double px = 14 + t * (w - 28);
            double py = (h - 14) - Math.Clamp(v / maxPlot, 0.0, 1.0) * (h - 28);
            poly.Points.Add(new Point(px, py));
        }

        VelocityCurveCanvas.Children.Add(poly);
    }

    private void RenderDeadzoneRadars()
    {
        if (LeftRadarCanvas == null || RightRadarCanvas == null) return;
        _leftRadarLiveDot = DrawSingleRadar(LeftRadarCanvas, SliderLeftDz.Value / 32767.0);
        _rightRadarLiveDot = DrawSingleRadar(RightRadarCanvas, SliderRightDz.Value / 32767.0);
    }

    private static Ellipse DrawSingleRadar(Canvas canvas, double dzFraction)
    {
        canvas.Children.Clear();
        double r = 56, cx = 62, cy = 62;

        var outer = new Ellipse
        {
            Width = r * 2,
            Height = r * 2,
            Fill = new SolidColorBrush(Color.FromRgb(25, 25, 29)),
            Stroke = new SolidColorBrush(Color.FromRgb(56, 56, 64)),
            StrokeThickness = 1.5
        };
        Canvas.SetLeft(outer, cx - r);
        Canvas.SetTop(outer, cy - r);
        canvas.Children.Add(outer);

        canvas.Children.Add(new Line { X1 = cx - r, Y1 = cy, X2 = cx + r, Y2 = cy, Stroke = new SolidColorBrush(Color.FromRgb(38, 38, 44)), StrokeThickness = 1 });
        canvas.Children.Add(new Line { X1 = cx, Y1 = cy - r, X2 = cx, Y2 = cy + r, Stroke = new SolidColorBrush(Color.FromRgb(38, 38, 44)), StrokeThickness = 1 });

        double dzR = Math.Clamp(dzFraction, 0.02, 0.85) * r;
        var dzCircle = new Ellipse
        {
            Width = dzR * 2,
            Height = dzR * 2,
            Fill = new SolidColorBrush(Color.FromArgb(30, 10, 132, 255)),
            Stroke = AppleBlueBrush,
            StrokeThickness = 1.4,
            StrokeDashArray = [3, 2]
        };
        Canvas.SetLeft(dzCircle, cx - dzR);
        Canvas.SetTop(dzCircle, cy - dzR);
        canvas.Children.Add(dzCircle);

        var dot = new Ellipse { Width = 8, Height = 8, Fill = Brushes.White };
        Canvas.SetLeft(dot, cx - 4);
        Canvas.SetTop(dot, cy - 4);
        canvas.Children.Add(dot);
        return dot;
    }

    private void OnFeelSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingUi) return;

        UpdateFeelReadouts();
        RenderVelocityCurve();
        RenderDeadzoneRadars();

        var s = _config.Settings;
        s.MouseSettings.Speed = (float)Math.Round(SliderMouseSpeed.Value);
        s.MouseSettings.Acceleration = (float)Math.Round(SliderMouseAccel.Value);
        s.MouseSettings.WheelSpeed = (float)Math.Round(SliderWheelSpeed.Value);
        s.ControllerSettings.LeftStickDeadZone = (int)Math.Round(SliderLeftDz.Value);
        s.ControllerSettings.RightStickDeadZone = (int)Math.Round(SliderRightDz.Value);
        _config.SaveSettings();
    }

    // =========================================================================
    // VIEW 2: APPS & RULES ICON WALL
    // =========================================================================
    private void UpdateListButtonBadges()
    {
        if (BtnFullscreenWhitelist == null) return;
        BtnFullscreenWhitelist.Content = MakeButtonContent(VectorIcons.EyeIgnore, $"全屏不暂停白名单 ({_config.IgnoredFullscreenProcesses.Count})", ApplePrimaryText);
        BtnDisabledList.Content = MakeButtonContent(VectorIcons.BanCircle, $"禁用名单 ({_config.DisabledProcesses.Count})", ApplePrimaryText);
    }

    private void RebuildAppsGrid()
    {
        if (AppCardsWrap == null) return;
        AppCardsWrap.Children.Clear();
        UpdateListButtonBadges();
        if (_currentNavIndex == 2)
            StyleSidebarRow(NavBtnApps, VectorIcons.AppsGrid, "应用专属", _config.Profiles.Count.ToString(), true);

        foreach (var profile in _config.Profiles.OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            var card = new Border
            {
                Width = 230,
                Height = 84,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(34, 34, 39)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(48, 48, 54)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 12, 12, 12),
                Margin = new Thickness(0, 0, 12, 12),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconImg = IconGenerator.DecodeBase64Icon(profile.IconBase64);
            UIElement iconEl = iconImg != null
                ? new Image { Source = iconImg, Width = 36, Height = 36, VerticalAlignment = VerticalAlignment.Center }
                : new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(9),
                    Background = new SolidColorBrush(Color.FromRgb(48, 48, 58)),
                    Child = new TextBlock
                    {
                        Text = profile.Title.Length > 0 ? profile.Title[..1].ToUpperInvariant() : "A",
                        Foreground = AppleBlueBrush,
                        FontWeight = FontWeights.Bold,
                        FontSize = 15,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

            var rightStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            rightStack.Children.Add(new TextBlock
            {
                Text = profile.Title,
                Foreground = ApplePrimaryText,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var miniTools = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var toolBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            DockPanel.SetDock(toolBtns, Dock.Right);

            var exportMini = CreateTinyCardButton(VectorIcons.ExportSave, "导出 .ccp");
            exportMini.Click += (_, e) =>
            {
                e.Handled = true;
                var sfd = new SaveFileDialog { Filter = "Profile (*.ccp)|*.ccp", FileName = $"{profile.PrimaryProcessName}.ccp" };
                if (sfd.ShowDialog() == true)
                    File.WriteAllText(sfd.FileName, DirectiveParser.SerializeCcp(profile));
            };

            var delMini = CreateTinyCardButton(VectorIcons.Trash, "删除配置");
            delMini.Click += (_, e) =>
            {
                e.Handled = true;
                if (_selectedProfile == profile)
                {
                    _selectedProfile = null;
                    _targetKind = BlueprintTargetKind.Desktop;
                }
                _config.DeleteProcessProfile(profile);
                RebuildAppsGrid();
                RenderControllerBlueprint();
            };

            toolBtns.Children.Add(exportMini);
            toolBtns.Children.Add(delMini);
            miniTools.Children.Add(toolBtns);
            miniTools.Children.Add(new TextBlock
            {
                Text = $"编辑按键 ›  ({profile.PrimaryProcessName})",
                Foreground = AppleBlueBrush,
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            rightStack.Children.Add(miniTools);

            Grid.SetColumn(iconEl, 0);
            Grid.SetColumn(rightStack, 1);
            grid.Children.Add(iconEl);
            grid.Children.Add(rightStack);
            card.Child = grid;

            card.MouseEnter += (_, _) => card.Background = new SolidColorBrush(Color.FromRgb(42, 42, 49));
            card.MouseLeave += (_, _) => card.Background = new SolidColorBrush(Color.FromRgb(34, 34, 39));
            card.MouseLeftButtonUp += (_, _) =>
            {
                _selectedProfile = profile;
                _targetKind = BlueprintTargetKind.ProcessProfile;
                SelectNavPage(0);
                RenderControllerBlueprint();
            };

            AppCardsWrap.Children.Add(card);
        }
    }

    private static Button CreateTinyCardButton(string iconData, string tooltip)
    {
        return new Button
        {
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            Content = VectorIcons.CreateIcon(iconData, 12.5, AppleSecondaryText)
        };
    }

    // =========================================================================
    // LIVE TELEMETRY
    // =========================================================================
    private void UpdateLiveTelemetry()
    {
        var pad = _engine.LastPadState;
        var st = _engine.CurrentStatus;

        StatusPulseDot.Fill = st switch
        {
            CompanionStatus.Enabled => AppleGreenBrush,
            CompanionStatus.Disconnected => new SolidColorBrush(Color.FromRgb(110, 110, 115)),
            _ => new SolidColorBrush(Color.FromRgb(255, 159, 10))
        };

        SidebarStatusText.Text = st switch
        {
            CompanionStatus.Enabled => "手柄操控中",
            CompanionStatus.Disconnected => "等待手柄连接",
            CompanionStatus.DisabledFullscreen => "全屏自动暂停",
            _ => "已暂停操控"
        };

        SidebarProfileText.Text = $"生效：{_engine.ActiveProfileTitle}";
        MasterEnableSwitch.IsChecked = st is not (CompanionStatus.DisabledManual or CompanionStatus.DisabledPerProcess);
        if (_currentNavIndex == 3)
            UpdateShareVoiceSubtitle();

        if (_leftStickDiagramDot != null)
        {
            Canvas.SetLeft(_leftStickDiagramDot, 81 + pad.LeftStick.X * 11);
            Canvas.SetTop(_leftStickDiagramDot, 109 - pad.LeftStick.Y * 11);
        }
        if (_rightStickDiagramDot != null)
        {
            Canvas.SetLeft(_rightStickDiagramDot, 181 + pad.RightStick.X * 11);
            Canvas.SetTop(_rightStickDiagramDot, 151 - pad.RightStick.Y * 11);
        }
        if (_leftRadarLiveDot != null)
        {
            Canvas.SetLeft(_leftRadarLiveDot, 58 + pad.LeftStick.X * 48);
            Canvas.SetTop(_leftRadarLiveDot, 58 - pad.LeftStick.Y * 48);
        }
        if (_rightRadarLiveDot != null)
        {
            Canvas.SetLeft(_rightRadarLiveDot, 58 + pad.RightStick.X * 48);
            Canvas.SetTop(_rightRadarLiveDot, 58 - pad.RightStick.Y * 48);
        }

        if (_currentNavIndex == 0)
        {
            foreach (var (ctrl, shape) in _diagramParts)
            {
                if (_hoveredControl == ctrl) continue;
                bool down = pad.IsConnected && pad.IsControlDown(ctrl);
                shape.Fill = down ? AppleBlueBrush : new SolidColorBrush(Color.FromRgb(44, 44, 52));
            }
        }
    }

    // =========================================================================
    // EVENT HANDLERS
    // =========================================================================
    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnNavCanvasClick(object sender, RoutedEventArgs e) => SelectNavPage(0);
    private void OnNavFeelClick(object sender, RoutedEventArgs e) => SelectNavPage(1);
    private void OnNavAppsClick(object sender, RoutedEventArgs e) => SelectNavPage(2);
    private void OnNavSystemClick(object sender, RoutedEventArgs e) => SelectNavPage(3);

    private void OnTargetDesktopClick(object sender, RoutedEventArgs e)
    {
        _targetKind = BlueprintTargetKind.Desktop;
        RenderControllerBlueprint();
    }

    private void OnTargetKeyboardClick(object sender, RoutedEventArgs e)
    {
        _targetKind = BlueprintTargetKind.VirtualKeyboard;
        RenderControllerBlueprint();
    }

    private void OnLayerPrimaryClick(object sender, RoutedEventArgs e)
    {
        _activeBlueprintLayer = ModifierLayer.Primary;
        RenderControllerBlueprint();
    }

    private void OnLayerBackClick(object sender, RoutedEventArgs e)
    {
        _activeBlueprintLayer = ModifierLayer.Back;
        RenderControllerBlueprint();
    }

    private void OnStatusToggleClick(object sender, RoutedEventArgs e)
    {
        bool enable = MasterEnableSwitch.IsChecked == true;
        _engine.SetManualEnabled(enable);
    }

    private void OnPreviewKeyboardClick(object sender, RoutedEventArgs e)
    {
        if (_keyboardWindow.IsVisible)
            ((IVirtualKeyboardController)_keyboardWindow).Hide();
        else
            ((IVirtualKeyboardController)_keyboardWindow).Show();
    }

    private void OnMinimizeTrayClick(object sender, RoutedEventArgs e) => Close();

    private void OnQuitOrbitClick(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
            app.ExitApplication();
        else
            Close();
    }

    private void OnResetSpeedClick(object sender, RoutedEventArgs e) => SliderMouseSpeed.Value = MouseSettings.SpeedDefault;
    private void OnResetAccelClick(object sender, RoutedEventArgs e) => SliderMouseAccel.Value = MouseSettings.AccelerationDefault;
    private void OnResetWheelClick(object sender, RoutedEventArgs e) => SliderWheelSpeed.Value = MouseSettings.WheelSpeedDefault;
    private void OnResetLeftDzClick(object sender, RoutedEventArgs e) => SliderLeftDz.Value = ControllerSettings.LeftStickDeadZoneDefault;
    private void OnResetRightDzClick(object sender, RoutedEventArgs e) => SliderRightDz.Value = ControllerSettings.RightStickDeadZoneDefault;

    private void OnAddAppExeClick(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog { Title = "选择应用程序 (.exe)", Filter = "可执行程序 (*.exe)|*.exe" };
        if (ofd.ShowDialog() == true)
            CreateAndSelectProfileFromExe(ofd.FileName);
    }

    private void CreateAndSelectProfileFromExe(string exePath)
    {
        string procName = Path.GetFileNameWithoutExtension(exePath);
        var existing = _config.FindProfileForProcess(procName);
        if (existing != null)
        {
            _selectedProfile = existing;
            _targetKind = BlueprintTargetKind.ProcessProfile;
            SelectNavPage(0);
            RenderControllerBlueprint();
            return;
        }

        var p = new ProcessProfile
        {
            ProcessNames = [procName],
            Title = procName,
            IconBase64 = IconGenerator.ExtractExeIconAsBase64(exePath),
            Bindings = _config.DesktopBindings.Entries.Values.Select(x => x.Clone()).ToList()
        };
        _config.SaveProcessProfile(p);
        RebuildAppsGrid();
        _selectedProfile = p;
        _targetKind = BlueprintTargetKind.ProcessProfile;
        SelectNavPage(0);
        RenderControllerBlueprint();
    }

    private void OnAddAppRecentClick(object sender, RoutedEventArgs e)
    {
        var recents = _windowTracker.GetRecentProcesses();
        if (recents.Count == 0)
        {
            OnAddAppExeClick(sender, e);
            return;
        }

        var menu = new ContextMenu { Background = new SolidColorBrush(Color.FromRgb(36, 36, 42)), Foreground = ApplePrimaryText };
        foreach (var r in recents)
        {
            var item = new MenuItem { Header = r.ProcessName, Foreground = ApplePrimaryText };
            item.Click += (_, _) =>
            {
                if (r.ExecutablePath != null && File.Exists(r.ExecutablePath))
                {
                    CreateAndSelectProfileFromExe(r.ExecutablePath);
                }
                else
                {
                    var p = new ProcessProfile
                    {
                        ProcessNames = [r.ProcessName],
                        Title = r.ProcessName,
                        Bindings = _config.DesktopBindings.Entries.Values.Select(x => x.Clone()).ToList()
                    };
                    _config.SaveProcessProfile(p);
                    RebuildAppsGrid();
                }
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void OnImportCcpClick(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog { Title = "导入 .ccp 配置文件", Filter = "Profile (*.ccp)|*.ccp" };
        if (ofd.ShowDialog() == true)
        {
            var p = DirectiveParser.ParseCcpFile(ofd.FileName);
            p.FilePath = Path.Combine(_config.ProfilesDirectory, Path.GetFileName(ofd.FileName));
            _config.SaveProcessProfile(p);
            RebuildAppsGrid();
        }
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

        string f = files[0];
        if (f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            CreateAndSelectProfileFromExe(f);
        }
        else if (f.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase))
        {
            var p = DirectiveParser.ParseCcpFile(f);
            p.FilePath = Path.Combine(_config.ProfilesDirectory, Path.GetFileName(f));
            _config.SaveProcessProfile(p);
            RebuildAppsGrid();
        }
    }

    private void OnOpenFullscreenWhitelistClick(object sender, RoutedEventArgs e)
    {
        var dlg = new ProcessListDrawerDialog(
            "全屏不暂停白名单",
            "全屏看视频、浏览器或 Galgame 时保持手柄激活",
            _config.IgnoredFullscreenProcesses,
            _windowTracker.GetRecentProcesses(),
            () =>
            {
                _config.SaveIgnoredFullscreenProcesses();
                UpdateListButtonBadges();
            })
        { Owner = this };
        dlg.ShowDialog();
    }

    private void OnOpenDisabledListClick(object sender, RoutedEventArgs e)
    {
        var dlg = new ProcessListDrawerDialog(
            "按进程禁用名单",
            "开启“按当前进程独立暂停”时自动停用映射的进程",
            _config.DisabledProcesses,
            _windowTracker.GetRecentProcesses(),
            () =>
            {
                _config.SaveDisabledProcesses();
                UpdateListButtonBadges();
            })
        { Owner = this };
        dlg.ShowDialog();
    }

    private void OnAdminToggleClick(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        if (ToggleAdmin.IsChecked == true)
        {
            ToggleStartup.IsChecked = true;
            ToggleStartup.IsEnabled = false;
        }
        else
        {
            ToggleStartup.IsEnabled = true;
        }
        SavePreferencesFromUi();
    }

    private void OnPrefToggleClick(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        SavePreferencesFromUi();
    }

    private void SavePreferencesFromUi()
    {
        var s = _config.Settings;
        s.StartupSettings.RunOnAdminAndLockScreen = ToggleAdmin.IsChecked == true;
        s.StartupSettings.RunOnStartup = s.StartupSettings.RunOnAdminAndLockScreen || ToggleStartup.IsChecked == true;
        s.NotificationSettings.EnableSoundsOnDisable = ToggleSound.IsChecked == true;
        s.DisablerSettings.PerProcessDisable = TogglePerProc.IsChecked == true;
        s.NotificationSettings.ShowBalloonTips = ToggleBalloon.IsChecked == true;
        s.KeyboardSettings.Type = ToggleSpiralRing.IsChecked == true ? VirtualKeyboardType.Spiral : VirtualKeyboardType.Qwerty;
        s.SteamSettings.ShowInAppInSteam = ToggleSteam.IsChecked == true;
        s.ShareVoiceSettings.Enabled = ToggleShareVoice.IsChecked == true;
        s.ShareVoiceSettings.TapToggleMode = ToggleShareTapMode.IsChecked == true;
        _config.SaveSettings();
        StatusToastText.Text = "✓ 已自动保存";
    }

    private async void OnTestShareVoiceClick(object sender, RoutedEventArgs e)
    {
        StatusToastText.Text = "正在发送右 Alt 触发测试…";
        await _shareVoice.TestTriggerAsync();
        UpdateShareVoiceSubtitle();
        StatusToastText.Text = "✓ 已发送右 Alt 测试脉冲";
    }

    private void OnBlockGameBarClick(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        bool block = ToggleBlockGameBar.IsChecked == true;
        StartupManager.SetWindowsGameBarGuideCapture(!block);
        StatusToastText.Text = block ? "✓ 已独占导航键" : "已恢复 Game Bar";
    }

    private void OnRegisterSystemStartupClick(object sender, RoutedEventArgs e)
    {
        SavePreferencesFromUi();
        string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Orbit.exe");
        var (_, msg) = StartupManager.ApplySystemStartup(
            exePath,
            _config.Settings.StartupSettings.RunOnStartup,
            _config.Settings.StartupSettings.RunOnAdminAndLockScreen);
        StatusToastText.Text = msg;
    }

    private void OnSyncOriginalClick(object sender, RoutedEventArgs e)
    {
        var report = CompanionMigrator.ImportFromControllerCompanion(_config.ConfigDirectory);
        _config.ReloadAll();
        SyncUiFromSettings();
        RebuildAppsGrid();
        RenderControllerBlueprint();
        StatusToastText.Text = $"✓ 已同步 {report.CopiedProfiles} 个应用配置";
    }

    private void OnOpenConfigDirClick(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _config.ConfigDirectory,
            UseShellExecute = true
        });
    }
}
