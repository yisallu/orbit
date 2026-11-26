using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using OpenGamepadCompanion.Core.Config;
using OpenGamepadCompanion.Core.Engine;
using OpenGamepadCompanion.Core.Models;
using Brushes = System.Windows.Media.Brushes;

namespace OpenGamepadCompanion.App.Views;

public partial class HelpOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    private readonly ConfigManager _config;
    private readonly CompanionEngine _engine;

    public HelpOverlayWindow(ConfigManager config, CompanionEngine engine)
    {
        InitializeComponent();
        _config = config;
        _engine = engine;

        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    public void ToggleOverlay()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            RefreshBindings();
            Show();
        }
    }

    private void RefreshBindings()
    {
        var fg = _engine.LastForeground;
        var proc = _config.FindProfileForProcess(fg.ProcessName);
        var map = proc != null ? proc.BuildRuntimeMap() : _config.DesktopBindings;

        HeaderTitle.Text = proc != null
            ? $"{proc.Title} ({proc.PrimaryProcessName})"
            : "桌面";

        PrimaryList.Items.Clear();
        SecondaryList.Items.Clear();

        foreach (var ctrl in PadSlot.AllControls)
        {
            var p = map.TryGet(ModifierLayer.Primary, ctrl);
            if (p != null && p.Action.Kind != ActionKind.None)
                PrimaryList.Items.Add(CreateRow(new PadSlot(ModifierLayer.Primary, ctrl).ToDisplayNameZh(), KeyNameResolver.LocalizeCommandTitle(p.Title)));

            var s = map.TryGet(ModifierLayer.Back, ctrl);
            if (s != null && s.Action.Kind != ActionKind.None)
                SecondaryList.Items.Add(CreateRow(new PadSlot(ModifierLayer.Primary, ctrl).ToDisplayNameZh(), KeyNameResolver.LocalizeCommandTitle(s.Title)));
        }
    }

    private static UIElement CreateRow(string left, string right)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var t1 = new TextBlock { Text = left, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(176, 190, 197)), FontSize = 12 };
        var t2 = new TextBlock { Text = right, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(t1, 0);
        Grid.SetColumn(t2, 1);
        grid.Children.Add(t1);
        grid.Children.Add(t2);
        return grid;
    }
}
