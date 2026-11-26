using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using OpenGamepadCompanion.Core.Config;
using OpenGamepadCompanion.Core.Engine;
using OpenGamepadCompanion.Core.Input;
using OpenGamepadCompanion.Core.Models;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;

namespace OpenGamepadCompanion.App.Views;

public partial class SpiralKeyboardWindow : Window, IVirtualKeyboardController
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    private enum KeyCapAction
    {
        VirtualKey,
        ShiftedVirtualKey,
        UnicodeText,
        ToggleCaps,
        ToggleSymbols,
        ToggleImeShift,
        HideKeyboard
    }

    private sealed record KeyCapSpec(
        string NormalLabel,
        string ShiftLabel,
        string SymbolLabel,
        double WidthWeight,
        Keys Vk = Keys.None,
        KeyCapAction Special = KeyCapAction.VirtualKey
    );

    private static readonly KeyCapSpec[][] Rows =
    [
        // Row 0: Number & Candidate Row (1-9, 0 directly select Chinese Pinyin candidates!)
        [
            new("`", "~", "`", 1.0, Keys.Oemtilde),
            new("1", "!", "¡", 1.0, Keys.D1),
            new("2", "@", "™", 1.0, Keys.D2),
            new("3", "#", "£", 1.0, Keys.D3),
            new("4", "$", "¥", 1.0, Keys.D4),
            new("5", "%", "‰", 1.0, Keys.D5),
            new("6", "^", "§", 1.0, Keys.D6),
            new("7", "&", "¶", 1.0, Keys.D7),
            new("8", "*", "•", 1.0, Keys.D8),
            new("9", "(", "（", 1.0, Keys.D9),
            new("0", ")", "）", 1.0, Keys.D0),
            new("-", "_", "—", 1.0, Keys.OemMinus),
            new("=", "+", "≠", 1.0, Keys.Oemplus),
            new("⌫", "⌫", "⌫", 1.6, Keys.Back)
        ],
        // Row 1: Top QWERTY Row
        [
            new("Tab", "Tab", "Tab", 1.4, Keys.Tab),
            new("q", "Q", "1", 1.0, Keys.Q),
            new("w", "W", "2", 1.0, Keys.W),
            new("e", "E", "3", 1.0, Keys.E),
            new("r", "R", "4", 1.0, Keys.R),
            new("t", "T", "5", 1.0, Keys.T),
            new("y", "Y", "6", 1.0, Keys.Y),
            new("u", "U", "7", 1.0, Keys.U),
            new("i", "I", "8", 1.0, Keys.I),
            new("o", "O", "9", 1.0, Keys.O),
            new("p", "P", "0", 1.0, Keys.P),
            new("[", "{", "【", 1.0, Keys.OemOpenBrackets),
            new("]", "}", "】", 1.0, Keys.OemCloseBrackets),
            new("\\", "|", "、", 1.2, Keys.OemPipe)
        ],
        // Row 2: Home Row (Default cursor starts right on 'g' at [2][5]!)
        [
            new("Caps", "CAPS", "Caps", 1.6, Keys.None, KeyCapAction.ToggleCaps),
            new("a", "A", "@", 1.0, Keys.A),
            new("s", "S", "#", 1.0, Keys.S),
            new("d", "D", "$", 1.0, Keys.D),
            new("f", "F", "%", 1.0, Keys.F),
            new("g", "G", "&", 1.0, Keys.G),
            new("h", "H", "*", 1.0, Keys.H),
            new("j", "J", "-", 1.0, Keys.J),
            new("k", "K", "+", 1.0, Keys.K),
            new("l", "L", "=", 1.0, Keys.L),
            new(";", ":", "；", 1.0, Keys.OemSemicolon),
            new("'", "\"", "“”", 1.0, Keys.OemQuotes),
            new("↵ Enter", "↵ Enter", "↵ Enter", 2.0, Keys.Return)
        ],
        // Row 3: Bottom Letters Row
        [
            new("⇧", "⇧", "⇧", 1.8, Keys.None, KeyCapAction.ToggleCaps),
            new("z", "Z", "~", 1.0, Keys.Z),
            new("x", "X", "`", 1.0, Keys.X),
            new("c", "C", "<", 1.0, Keys.C),
            new("v", "V", ">", 1.0, Keys.V),
            new("b", "B", "《", 1.0, Keys.B),
            new("n", "N", "》", 1.0, Keys.N),
            new("m", "M", "…", 1.0, Keys.M),
            new(",", "<", "，", 1.0, Keys.Oemcomma),
            new(".", ">", "。", 1.0, Keys.OemPeriod),
            new("/", "?", "？", 1.0, Keys.OemQuestion),
            new("中/英", "中/英", "中/英", 1.8, Keys.None, KeyCapAction.ToggleImeShift)
        ],
        // Row 4: Space, Symbols & Cursor Row
        [
            new("#+=", "ABC", "ABC", 1.6, Keys.None, KeyCapAction.ToggleSymbols),
            new("Space 空格", "Space 空格", "Space 空格", 6.8, Keys.Space),
            new("←", "←", "←", 1.1, Keys.Left),
            new("↑", "↑", "↑", 1.1, Keys.Up),
            new("↓", "↓", "↓", 1.1, Keys.Down),
            new("→", "→", "→", 1.1, Keys.Right),
            new("✕", "✕", "✕", 1.4, Keys.None, KeyCapAction.HideKeyboard)
        ]
    ];

    private readonly ConfigManager _config;
    private readonly IInputSimulator _simulator;

    private int _row = 2;
    private int _col = 5; // Starts right in center home row ('g')!
    private double _stickAccumX;
    private double _stickAccumY;

    private bool _triggerShiftHeld;
    private bool _capsLocked;
    private bool _triggerAltGrHeld;
    private bool _symbolsLocked;

    bool IVirtualKeyboardController.IsVisible => Dispatcher.Invoke(() => IsVisible);

    public bool IsShiftActive
    {
        get => _triggerShiftHeld || _capsLocked;
        set
        {
            if (_triggerShiftHeld != value)
            {
                _triggerShiftHeld = value;
                Dispatcher.BeginInvoke(RenderRing);
            }
        }
    }

    public bool IsAltGrActive
    {
        get => _triggerAltGrHeld || _symbolsLocked;
        set
        {
            if (_triggerAltGrHeld != value)
            {
                _triggerAltGrHeld = value;
                Dispatcher.BeginInvoke(RenderRing);
            }
        }
    }

    public SpiralKeyboardWindow(ConfigManager config, IInputSimulator simulator)
    {
        InitializeComponent();
        _config = config;
        _simulator = simulator;

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Bottom - Height - 28;

        Loaded += (_, _) => RenderRing();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    void IVirtualKeyboardController.Show()
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Apply translucency preference
            OuterGlassBorder.Background = _config.Settings.KeyboardSettings.Type == VirtualKeyboardType.Spiral
                ? new SolidColorBrush(Color.FromArgb(238, 13, 16, 23))
                : new SolidColorBrush(Color.FromArgb(252, 13, 16, 23));

            if (!IsVisible)
            {
                // Start at center home row ('g') so any key on screen is <= 4 steps away!
                _row = 2;
                _col = 5;
                _stickAccumX = 0;
                _stickAccumY = 0;
                RenderRing();
                base.Show();
            }
        });
    }

    void IVirtualKeyboardController.Hide()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible) base.Hide();
        });
    }

    public void StepSelection(int dx, int dy)
    {
        Dispatcher.BeginInvoke(() => MoveHighlightByStep(dx, dy));
    }

    public void UpdateStick(StickVector stick, double dtSeconds)
    {
        if (stick.Magnitude < 0.22f)
        {
            _stickAccumX = 0;
            _stickAccumY = 0;
            return;
        }

        // Smooth velocity-proportional grid stepping (gentle push = 3.5 keys/s, full push = 14 keys/s!)
        double speed = 2.5 + Math.Pow(stick.Magnitude, 1.6) * 11.5;
        _stickAccumX += stick.X * speed * dtSeconds;
        _stickAccumY -= stick.Y * speed * dtSeconds; // Stick +Y is up -> row decreases

        int stepX = 0, stepY = 0;
        if (Math.Abs(_stickAccumX) >= 1.0)
        {
            stepX = Math.Sign(_stickAccumX);
            _stickAccumX -= stepX;
        }
        if (Math.Abs(_stickAccumY) >= 1.0)
        {
            stepY = Math.Sign(_stickAccumY);
            _stickAccumY -= stepY;
        }

        if (stepX != 0 || stepY != 0)
        {
            Dispatcher.BeginInvoke(() => MoveHighlightByStep(stepX, stepY));
        }
    }

    private void MoveHighlightByStep(int dx, int dy)
    {
        if (dy != 0)
        {
            double prevCenterRatio = GetHorizontalCenterRatio(_row, _col);
            _row = (_row + dy + Rows.Length) % Rows.Length;
            _col = FindClosestColumnByRatio(_row, prevCenterRatio);
        }

        if (dx != 0)
        {
            int rowLen = Rows[_row].Length;
            _col = (_col + dx + rowLen) % rowLen;
        }

        RenderRing();
    }

    private static double GetHorizontalCenterRatio(int r, int c)
    {
        var row = Rows[r];
        double total = row.Sum(k => k.WidthWeight);
        double before = 0;
        for (int i = 0; i < c; i++) before += row[i].WidthWeight;
        return (before + row[c].WidthWeight * 0.5) / total;
    }

    private static int FindClosestColumnByRatio(int r, double targetRatio)
    {
        var row = Rows[r];
        int bestIdx = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < row.Length; i++)
        {
            double dist = Math.Abs(GetHorizontalCenterRatio(r, i) - targetRatio);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    public void SelectHighlightedKey()
    {
        Dispatcher.BeginInvoke(() => ExecuteKeySpec(Rows[_row][_col]));
    }

    public void CycleLanguage()
    {
        Dispatcher.BeginInvoke(() =>
        {
            TapImeShift();
        });
    }

    private void ExecuteKeySpec(KeyCapSpec spec)
    {
        switch (spec.Special)
        {
            case KeyCapAction.ToggleCaps:
                _capsLocked = !_capsLocked;
                RenderRing();
                return;

            case KeyCapAction.ToggleSymbols:
                _symbolsLocked = !_symbolsLocked;
                RenderRing();
                return;

            case KeyCapAction.ToggleImeShift:
                TapImeShift();
                return;

            case KeyCapAction.HideKeyboard:
                base.Hide();
                return;
        }

        bool sym = IsAltGrActive;
        bool shift = IsShiftActive;

        if (sym)
        {
            // Send exact symbol character cleanly
            if (spec.Vk is Keys.Back or Keys.Tab or Keys.Return or Keys.Space or Keys.Left or Keys.Right or Keys.Up or Keys.Down)
            {
                TapVirtualKey(spec.Vk, withShift: false);
            }
            else
            {
                _simulator.SendUnicodeText(spec.SymbolLabel);
            }
            return;
        }

        // Send REAL hardware-mapped VirtualKey so Windows Chinese Pinyin IME & shortcuts work 100% natively!
        if (spec.Vk != Keys.None)
        {
            TapVirtualKey(spec.Vk, withShift: shift && spec.Vk is not (Keys.Back or Keys.Return or Keys.Space or Keys.Left or Keys.Right or Keys.Up or Keys.Down));
        }
    }

    private void TapVirtualKey(Keys vk, bool withShift)
    {
        if (withShift) _simulator.KeyDown(Keys.LShiftKey);
        _simulator.KeyDown(vk);
        _simulator.KeyUp(vk);
        if (withShift) _simulator.KeyUp(Keys.LShiftKey);
    }

    private void TapImeShift()
    {
        _simulator.KeyDown(Keys.LShiftKey);
        _simulator.KeyUp(Keys.LShiftKey);
    }

    public void RenderRing()
    {
        if (RowsContainer == null) return;
        RowsContainer.Children.Clear();

        bool sym = IsAltGrActive;
        bool shift = IsShiftActive;

        CapsBadge.Visibility = shift ? Visibility.Visible : Visibility.Collapsed;
        SymBadge.Visibility = sym ? Visibility.Visible : Visibility.Collapsed;

        for (int r = 0; r < Rows.Length; r++)
        {
            var rowSpecs = Rows[r];
            var rowGrid = new Grid { Margin = new Thickness(0, 2.5, 0, 2.5) };

            for (int c = 0; c < rowSpecs.Length; c++)
            {
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(rowSpecs[c].WidthWeight, GridUnitType.Star)
                });
            }

            for (int c = 0; c < rowSpecs.Length; c++)
            {
                var spec = rowSpecs[c];
                bool selected = (r == _row && c == _col);
                bool isSpecialActive = (spec.Special == KeyCapAction.ToggleCaps && shift) ||
                                       (spec.Special == KeyCapAction.ToggleSymbols && sym);

                string label = sym ? spec.SymbolLabel : (shift ? spec.ShiftLabel : spec.NormalLabel);
                bool isHomeAnchor = !sym && (spec.Vk is Keys.F or Keys.J);

                var keyBorder = new Border
                {
                    Margin = new Thickness(2.5, 0, 2.5, 0),
                    CornerRadius = new CornerRadius(9),
                    Background = selected
                        ? new SolidColorBrush(Color.FromRgb(56, 189, 248))
                        : (isSpecialActive
                            ? new SolidColorBrush(Color.FromRgb(30, 58, 95))
                            : new SolidColorBrush(Color.FromRgb(22, 27, 39))),
                    BorderBrush = selected
                        ? Brushes.White
                        : (isSpecialActive
                            ? new SolidColorBrush(Color.FromRgb(56, 189, 248))
                            : new SolidColorBrush(Color.FromRgb(38, 47, 66))),
                    BorderThickness = new Thickness(selected ? 1.6 : 1.0),
                    Cursor = Cursors.Hand
                };

                var innerGrid = new Grid();
                innerGrid.Children.Add(new TextBlock
                {
                    Text = label,
                    Foreground = selected
                        ? new SolidColorBrush(Color.FromRgb(11, 15, 23))
                        : (spec.Special != KeyCapAction.VirtualKey || spec.WidthWeight > 1.2
                            ? new SolidColorBrush(Color.FromRgb(186, 200, 218))
                            : Brushes.White),
                    FontSize = label.Length > 2 ? 12.5 : 15.5,
                    FontWeight = selected ? FontWeights.Bold : FontWeights.Medium,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });

                if (isHomeAnchor)
                {
                    innerGrid.Children.Add(new Border
                    {
                        Width = 10,
                        Height = 2,
                        CornerRadius = new CornerRadius(1),
                        Background = selected
                            ? new SolidColorBrush(Color.FromRgb(11, 15, 23))
                            : new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 0, 5)
                    });
                }

                keyBorder.Child = innerGrid;

                int capturedR = r, capturedC = c;
                var capturedSpec = spec;
                keyBorder.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    _row = capturedR;
                    _col = capturedC;
                    ExecuteKeySpec(capturedSpec);
                    RenderRing();
                };

                Grid.SetColumn(keyBorder, c);
                rowGrid.Children.Add(keyBorder);
            }

            RowsContainer.Children.Add(rowGrid);
        }
    }

    private void OnDragHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
