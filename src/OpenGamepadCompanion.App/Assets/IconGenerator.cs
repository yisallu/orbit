using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenGamepadCompanion.Core.Engine;

namespace OpenGamepadCompanion.App.Assets;

public static class IconGenerator
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon CreateTrayIcon(CompanionStatus status)
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);

        System.Drawing.Color accent = status switch
        {
            CompanionStatus.Enabled => System.Drawing.Color.FromArgb(255, 56, 189, 248),     // Sky Cyan
            CompanionStatus.Disconnected => System.Drawing.Color.FromArgb(255, 100, 116, 139), // Slate
            _ => System.Drawing.Color.FromArgb(255, 245, 158, 11)                             // Amber
        };

        // Dark Obsidian circular badge
        using var bgBrush = new SolidBrush(System.Drawing.Color.FromArgb(255, 15, 23, 42));
        g.FillEllipse(bgBrush, 1, 1, 30, 30);

        // Orbital outer arc
        using var orbitPen = new System.Drawing.Pen(accent, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(orbitPen, 4, 4, 24, 24, 35, 290);

        // Center luminous core
        using var coreBrush = new SolidBrush(System.Drawing.Color.White);
        g.FillEllipse(coreBrush, 12, 12, 8, 8);

        // Satellite dot on orbit
        using var satBrush = new SolidBrush(accent);
        g.FillEllipse(satBrush, 22, 6, 5, 5);

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    public static ImageSource? DecodeBase64Icon(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return null;

        try
        {
            byte[] bytes = Convert.FromBase64String(base64.Trim());
            using var ms = new MemoryStream(bytes);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }

    public static string? ExtractExeIconAsBase64(string exePath)
    {
        try
        {
            if (!File.Exists(exePath))
                return null;
            using var ico = Icon.ExtractAssociatedIcon(exePath);
            if (ico == null)
                return null;
            using var bmp = ico.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }
}
