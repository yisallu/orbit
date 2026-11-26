using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenGamepadCompanion.App.Assets;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;

namespace OpenGamepadCompanion.App.Views;

public partial class ProcessListDrawerDialog : Window
{
    private readonly HashSet<string> _targetSet;
    private readonly Action _saveCallback;

    public ProcessListDrawerDialog(
        string title,
        string subtitle,
        HashSet<string> targetSet,
        IReadOnlyList<(string ProcessName, string? ExecutablePath)> recentProcesses,
        Action saveCallback)
    {
        InitializeComponent();
        TitleBlock.Text = title;
        SubtitleBlock.Text = subtitle;
        _targetSet = targetSet;
        _saveCallback = saveCallback;

        BtnClose.Content = VectorIcons.CreateIcon(VectorIcons.CloseX, 14, new SolidColorBrush(Color.FromRgb(148, 163, 184)));

        foreach (var r in recentProcesses.Take(6))
        {
            if (_targetSet.Contains(r.ProcessName))
                continue;

            var chip = new Button
            {
                Content = $"+ {r.ProcessName}",
                FontSize = 11.5,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 6, 4),
                Style = (Style)FindResource("IconPillButtonStyle")
            };
            chip.Click += (_, _) =>
            {
                _targetSet.Add(r.ProcessName);
                _saveCallback();
                chip.Visibility = Visibility.Collapsed;
                RebuildChips();
            };
            RecentRunningPanel.Children.Add(chip);
        }

        RebuildChips();
    }

    private void RebuildChips()
    {
        ChipsWrap.Children.Clear();
        string filter = SearchOrAddBox.Text.Trim();

        var items = _targetSet
            .Where(x => string.IsNullOrEmpty(filter) || x.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var proc in items)
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 30, 44)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(42, 52, 74)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 5, 6, 5),
                Margin = new Thickness(0, 0, 6, 6)
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = proc,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            var delBtn = new Button
            {
                Content = "×",
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            string captured = proc;
            delBtn.Click += (_, _) =>
            {
                _targetSet.Remove(captured);
                _saveCallback();
                RebuildChips();
            };

            sp.Children.Add(delBtn);
            badge.Child = sp;
            ChipsWrap.Children.Add(badge);
        }
    }

    private void AddCurrentText()
    {
        string raw = SearchOrAddBox.Text.Trim();
        if (raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            raw = raw[..^4];

        if (!string.IsNullOrWhiteSpace(raw))
        {
            _targetSet.Add(raw);
            _saveCallback();
            SearchOrAddBox.Text = "";
            RebuildChips();
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RebuildChips();
    private void OnAddInputClick(object sender, RoutedEventArgs e) => AddCurrentText();
    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddCurrentText();
    }
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    private void OnDragMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
