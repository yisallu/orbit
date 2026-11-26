using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenGamepadCompanion.Core.Models;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace OpenGamepadCompanion.App.Assets;

public static class VectorIcons
{
    // Navigation & Brand (24x24 viewBox)
    public const string OrbitBrand = "M 12,3 A 9,9 0 1 0 21,12 M 12,7.5 A 4.5,4.5 0 1 1 7.5,12 M 18.5,5.5 A 2,2 0 1 1 18.49,5.5 Z";
    public const string Gamepad = "M 6,8 H 18 A 4,4 0 0 1 21.8,13.2 L 20.4,17.8 A 2.4,2.4 0 0 1 16.3,18.5 L 14.6,16 H 9.4 L 7.7,18.5 A 2.4,2.4 0 0 1 3.6,17.8 L 2.2,13.2 A 4,4 0 0 1 6,8 Z M 7.5,10.5 V 13.5 M 6,12 H 9 M 15.5,11.2 A 0.7,0.7 0 1 1 15.49,11.2 M 17.5,12.8 A 0.7,0.7 0 1 1 17.49,12.8";
    public const string MotionCurve = "M 3,19 C 8,19 11,16 14,11 C 16.5,6.8 18.5,5 21,5 M 17,5 H 21 V 9 M 4,5 V 19 H 20";
    public const string AppsGrid = "M 4,4 H 10 V 10 H 4 Z M 14,4 H 20 V 10 H 14 Z M 4,14 H 10 V 20 H 4 Z M 14,14 H 20 V 20 H 14 Z";
    public const string SettingsSliders = "M 4,7 H 14 M 18,7 H 20 M 16,7 A 2,2 0 1 1 12,7 A 2,2 0 1 1 16,7 M 4,17 H 6 M 10,17 H 20 M 10,17 A 2,2 0 1 1 6,17 A 2,2 0 1 1 10,17 M 4,12 H 9 M 13,12 H 20 M 13,12 A 2,2 0 1 1 9,12 A 2,2 0 1 1 13,12";

    // Layers & Targets
    public const string LayerPrimary = "M 12,4 L 20,12 L 12,20 L 4,12 Z";
    public const string LayerSecondary = "M 12,3 L 20,9.5 L 12,16 L 4,9.5 Z M 4,14 L 12,20.5 L 20,14";
    public const string DesktopMonitor = "M 3,5 H 21 V 16 H 3 Z M 8,20 H 16 M 12,16 V 20";
    public const string RadialKeyboard = "M 3,6 H 21 A 2,2 0 0 1 23,8 V 17 A 2,2 0 0 1 21,19 H 3 A 2,2 0 0 1 1,17 V 8 A 2,2 0 0 1 3,6 Z M 5,10 H 7 M 9,10 H 11 M 13,10 H 15 M 17,10 H 19 M 5,13 H 8 M 10,13 H 14 M 16,13 H 19 M 8,16 H 16";
    public const string HelpCircle = "M 12,3 A 9,9 0 1 0 12.01,3 Z M 9.5,9.5 A 2.5,2.5 0 0 1 14.5,10 C 14.5,11.8 12,12.2 12,14 M 12,17.2 V 17.3";

    // Window & Utility Controls
    public const string MinimizeLine = "M 5,12 H 19";
    public const string CloseX = "M 6,6 L 18,18 M 18,6 L 6,18";
    public const string PowerOff = "M 12,3 V 11 M 7.05,6.05 A 7,7 0 1 0 16.95,6.05";
    public const string ResetRotate = "M 4,11 A 8,8 0 1 1 6.3,17.2 M 4,6 V 11 H 9";
    public const string Plus = "M 12,5 V 19 M 5,12 H 19";
    public const string Trash = "M 5,7 H 19 M 9,7 V 4 H 15 V 7 M 7,7 L 8,20 H 16 L 17,7";
    public const string FolderOpen = "M 3,6 H 9 L 11,8 H 21 V 19 H 3 Z";
    public const string SyncRefresh = "M 20,11 A 8,8 0 0 0 5.5,7.5 M 4,4 V 8 H 8 M 4,13 A 8,8 0 0 0 18.5,16.5 M 20,20 V 16 H 16";
    public const string ShieldBolt = "M 12,3 L 19,6 V 11.5 C 19,16.2 16,19.8 12,21 C 8,19.8 5,16.2 5,11.5 V 6 Z M 13,8 L 10,12.5 H 13 L 11,16.5";
    public const string Search = "M 11,4 A 7,7 0 1 0 11.01,4 Z M 16,16 L 20.5,20.5";
    public const string Copy = "M 8,8 H 19 V 20 H 8 Z M 16,8 V 4 H 5 V 16 H 8";
    public const string DownloadImport = "M 12,4 V 15 M 8,11 L 12,15 L 16,11 M 4,19 H 20";
    public const string ExportSave = "M 12,15 V 4 M 8,8 L 12,4 L 16,8 M 4,19 H 20";
    public const string EyeIgnore = "M 2,12 C 4.5,7 8,5 12,5 C 16,5 19.5,7 22,12 C 19.5,17 16,19 12,19 C 8,19 4.5,17 2,12 Z M 12,9 A 3,3 0 1 0 12.01,9 Z";
    public const string BanCircle = "M 12,3 A 9,9 0 1 0 12.01,3 Z M 5.7,5.7 L 18.3,18.3";
    public const string SoundWave = "M 5,9 H 8 L 13,5 V 19 L 8,15 H 5 Z M 16.5,8.5 A 5,5 0 0 1 16.5,15.5 M 19,6 A 8.5,8.5 0 0 1 19,18";
    public const string Bell = "M 18,15 V 10 A 6,6 0 0 0 6,10 V 15 L 4,17 H 20 Z M 10,19 A 2,2 0 0 0 14,19";
    public const string SparkleSteam = "M 12,3 A 9,9 0 1 0 21,12 A 9,9 0 0 0 12,3 Z M 15.5,8 A 2.5,2.5 0 1 0 15.51,8 Z M 9.5,14.5 A 2,2 0 1 0 9.51,14.5 Z M 14,10 L 11,13.5";

    // Action Node Micro-Icons (Rendered inside Controller Blueprint nodes!)
    public const string MouseBody = "M 7,8 A 5,5 0 0 1 17,8 V 15 A 5,5 0 0 1 7,15 Z M 12,4 V 10 M 7,10 H 17";
    public const string MouseLeft = "M 7,8 A 5,5 0 0 1 17,8 V 15 A 5,5 0 0 1 7,15 Z M 12,3.5 V 10 M 7,10 H 17 M 9,6.5 H 10.5 V 8.5 H 9 Z";
    public const string MouseRight = "M 7,8 A 5,5 0 0 1 17,8 V 15 A 5,5 0 0 1 7,15 Z M 12,3.5 V 10 M 7,10 H 17 M 13.5,6.5 H 15 V 8.5 H 13.5 Z";
    public const string MouseScroll = "M 12,4 L 9,7 M 12,4 L 15,7 M 12,20 L 9,17 M 12,20 L 15,17 M 12,8 V 16";
    public const string CursorMove = "M 12,3 V 21 M 3,12 H 21 M 12,3 L 9.5,5.5 M 12,3 L 14.5,5.5 M 12,21 L 9.5,18.5 M 12,21 L 14.5,18.5 M 3,12 L 5.5,9.5 M 3,12 L 5.5,14.5 M 21,12 L 18.5,9.5 M 21,12 L 18.5,14.5";
    public const string KeyCap = "M 4,6 H 20 A 2,2 0 0 1 22,8 V 16 A 2,2 0 0 1 20,18 H 4 A 2,2 0 0 1 2,16 V 8 A 2,2 0 0 1 4,6 Z M 7,14 H 17";
    public const string KeyBackspace = "M 20,12 H 5 M 10,7 L 5,12 L 10,17";
    public const string KeyEnter = "M 18,6 V 13 H 6 M 10,9 L 6,13 L 10,17";
    public const string MediaPlay = "M 7,5 L 18,12 L 7,19 Z";
    public const string MediaPrev = "M 18,6 L 9,12 L 18,18 Z M 6,6 V 18";
    public const string MediaNext = "M 6,6 L 15,12 L 6,18 Z M 18,6 V 18";
    public const string VolumeUp = "M 4,9 H 7 L 12,5 V 19 L 7,15 H 4 Z M 16,12 H 21 M 18.5,9.5 V 14.5";
    public const string VolumeDown = "M 4,9 H 7 L 12,5 V 19 L 7,15 H 4 Z M 16,12 H 21";
    public const string WindowSwitch = "M 3,6 H 14 V 15 H 3 Z M 10,15 V 18 H 21 V 9 H 14";
    public const string ScreenSwitch = "M 2,5 H 14 V 14 H 2 Z M 10,10 H 22 V 19 H 10 Z";
    public const string ArrowUp = "M 12,19 V 5 M 7,10 L 12,5 L 17,10";
    public const string ArrowDown = "M 12,5 V 19 M 7,14 L 12,19 L 17,14";
    public const string ArrowLeft = "M 19,12 H 5 M 10,7 L 5,12 L 10,17";
    public const string ArrowRight = "M 5,12 H 19 M 14,7 L 19,12 L 14,17";
    public const string EmptyDash = "M 8,12 H 16";

    public static UIElement CreateIcon(string pathData, double size = 18, Brush? stroke = null, double thickness = 1.75)
    {
        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = new Canvas
            {
                Width = 24,
                Height = 24,
                Children =
                {
                    new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse(pathData),
                        Stroke = stroke ?? Brushes.White,
                        StrokeThickness = thickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                        Fill = Brushes.Transparent
                    }
                }
            }
        };
    }

    public static string GetIconForBinding(BindingEntry? entry)
    {
        if (entry == null || entry.Action.Kind == ActionKind.None)
            return EmptyDash;

        var a = entry.Action;
        return a.Kind switch
        {
            ActionKind.MouseButton => a.MouseButton switch
            {
                MouseButtonTarget.Left => MouseLeft,
                MouseButtonTarget.Right => MouseRight,
                _ => MouseBody
            },
            ActionKind.MouseMovement => CursorMove,
            ActionKind.MouseScroll or ActionKind.MouseScrollStep => MouseScroll,
            ActionKind.MouseSpeedScale => MotionCurve,
            ActionKind.DirectionalKeys => CursorMove,
            ActionKind.ModifierHold => LayerSecondary,
            ActionKind.VirtualKeyboard => RadialKeyboard,
            ActionKind.CommandTurnOffController => PowerOff,
            ActionKind.CommandToggleHelp => HelpCircle,
            ActionKind.CommandKeyCombo => WindowSwitch,
            ActionKind.CommandProcess => entry.RawCommand.Contains("displayswitch", StringComparison.OrdinalIgnoreCase)
                ? ScreenSwitch
                : SparkleSteam,
            ActionKind.KeyChord => ResolveKeyChordIcon(a.Keys),
            _ => KeyCap
        };
    }

    private static string ResolveKeyChordIcon(IReadOnlyList<Keys> keys)
    {
        if (keys.Count == 1)
        {
            return keys[0] switch
            {
                Keys.Back => KeyBackspace,
                Keys.Return => KeyEnter,
                Keys.Up => ArrowUp,
                Keys.Down => ArrowDown,
                Keys.Left => ArrowLeft,
                Keys.Right => ArrowRight,
                Keys.MediaPlayPause => MediaPlay,
                Keys.MediaPreviousTrack => MediaPrev,
                Keys.MediaNextTrack => MediaNext,
                Keys.VolumeUp => VolumeUp,
                Keys.VolumeDown => VolumeDown,
                _ => KeyCap
            };
        }
        return KeyCap;
    }
}
