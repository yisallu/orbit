using System.Globalization;
using System.Xml.Linq;

namespace OpenGamepadCompanion.Core.Models;

public enum VirtualKeyboardType
{
    Spiral = 0,
    Qwerty = 1
}

public sealed class MouseSettings
{
    public const float SpeedDefault = 600f;
    public const float AccelerationDefault = 1500f;
    public const float AccelerationThresholdDefault = 0.95f;
    public const float WheelSpeedDefault = 4800f;

    public float Speed { get; set; } = SpeedDefault;
    public float Acceleration { get; set; } = AccelerationDefault;
    public float AccelerationThreshold { get; set; } = AccelerationThresholdDefault;
    public float WheelSpeed { get; set; } = WheelSpeedDefault;

    public MouseSettings Clone() => (MouseSettings)MemberwiseClone();
}

public sealed class NotificationSettings
{
    public bool ShowBalloonTips { get; set; } = true;
    public bool EnableSoundsOnDisable { get; set; } = true;
    public bool AutoDisableHelpShown { get; set; } = false;
    public bool HideNotificationIcon { get; set; } = false;

    public NotificationSettings Clone() => (NotificationSettings)MemberwiseClone();
}

public sealed class SteamSettings
{
    public bool ShowInAppInSteam { get; set; } = false;

    public SteamSettings Clone() => (SteamSettings)MemberwiseClone();
}

public sealed class ControllerSettings
{
    public const int LeftStickDeadZoneDefault = 7849;
    public const int RightStickDeadZoneDefault = 8689;
    public const int StickMaxValue = 32767;

    public int LeftStickDeadZone { get; set; } = LeftStickDeadZoneDefault;
    public int RightStickDeadZone { get; set; } = RightStickDeadZoneDefault;

    public ControllerSettings Clone() => (ControllerSettings)MemberwiseClone();
}

public sealed class DisablerSettings
{
    public bool PerProcessDisable { get; set; } = false;

    public DisablerSettings Clone() => (DisablerSettings)MemberwiseClone();
}

public sealed class KeyboardSettings
{
    public VirtualKeyboardType Type { get; set; } = VirtualKeyboardType.Spiral;

    public KeyboardSettings Clone() => (KeyboardSettings)MemberwiseClone();
}

public sealed class StartupSettings
{
    public bool RunOnStartup { get; set; } = true;
    public bool RunOnAdminAndLockScreen { get; set; } = true;

    public StartupSettings Clone() => (StartupSettings)MemberwiseClone();
}

public sealed class ShareVoiceSettings
{
    public bool Enabled { get; set; } = true;
    public bool TapToggleMode { get; set; } = true;

    public ShareVoiceSettings Clone() => (ShareVoiceSettings)MemberwiseClone();
}

public sealed class AppSettings
{
    public MouseSettings MouseSettings { get; set; } = new();
    public NotificationSettings NotificationSettings { get; set; } = new();
    public SteamSettings SteamSettings { get; set; } = new();
    public ControllerSettings ControllerSettings { get; set; } = new();
    public DisablerSettings DisablerSettings { get; set; } = new();
    public KeyboardSettings KeyboardSettings { get; set; } = new();
    public StartupSettings StartupSettings { get; set; } = new();
    public ShareVoiceSettings ShareVoiceSettings { get; set; } = new();
    public string? ForcedCulture { get; set; }

    public AppSettings Clone() => new()
    {
        MouseSettings = MouseSettings.Clone(),
        NotificationSettings = NotificationSettings.Clone(),
        SteamSettings = SteamSettings.Clone(),
        ControllerSettings = ControllerSettings.Clone(),
        DisablerSettings = DisablerSettings.Clone(),
        KeyboardSettings = KeyboardSettings.Clone(),
        StartupSettings = StartupSettings.Clone(),
        ShareVoiceSettings = ShareVoiceSettings.Clone(),
        ForcedCulture = ForcedCulture
    };

    public static AppSettings LoadXml(string xmlPath)
    {
        var settings = new AppSettings();
        if (!File.Exists(xmlPath))
            return settings;

        var doc = XDocument.Load(xmlPath);
        var root = doc.Root;
        if (root == null)
            return settings;

        var mouse = root.Element("MouseSettings");
        if (mouse != null)
        {
            settings.MouseSettings.Speed = ReadFloat(mouse, "Speed", MouseSettings.SpeedDefault);
            settings.MouseSettings.Acceleration = ReadFloat(mouse, "Acceleration", MouseSettings.AccelerationDefault);
            settings.MouseSettings.AccelerationThreshold = ReadFloat(mouse, "AccelerationThreshold", MouseSettings.AccelerationThresholdDefault);
            settings.MouseSettings.WheelSpeed = ReadFloat(mouse, "WheelSpeed", MouseSettings.WheelSpeedDefault);
        }

        var notif = root.Element("NotificationSettings");
        if (notif != null)
        {
            // Support both original Controller Companion spelling ("ShowBaloonTips") and "ShowBalloonTips"
            var balloonEl = notif.Element("ShowBaloonTips") ?? notif.Element("ShowBalloonTips");
            if (balloonEl != null && bool.TryParse(balloonEl.Value.Trim(), out bool bVal))
                settings.NotificationSettings.ShowBalloonTips = bVal;

            settings.NotificationSettings.EnableSoundsOnDisable = ReadBool(notif, "EnableSoundsOnDisable", true);
            settings.NotificationSettings.AutoDisableHelpShown = ReadBool(notif, "AutoDisableHelpShown", false);
            settings.NotificationSettings.HideNotificationIcon = ReadBool(notif, "HideNotificationIcon", false);
        }

        var steam = root.Element("SteamSettings");
        if (steam != null)
        {
            settings.SteamSettings.ShowInAppInSteam = ReadBool(steam, "ShowInAppInSteam", false);
        }

        var ctrl = root.Element("ControllerSettings");
        if (ctrl != null)
        {
            settings.ControllerSettings.LeftStickDeadZone = ReadInt(ctrl, "LeftStickDeadZone", ControllerSettings.LeftStickDeadZoneDefault);
            settings.ControllerSettings.RightStickDeadZone = ReadInt(ctrl, "RightStickDeadZone", ControllerSettings.RightStickDeadZoneDefault);
        }

        var disabler = root.Element("DisablerSettings");
        if (disabler != null)
        {
            settings.DisablerSettings.PerProcessDisable = ReadBool(disabler, "PerProcessDisable", false);
        }

        var kb = root.Element("KeyboardSettings");
        if (kb != null)
        {
            var typeStr = kb.Element("Type")?.Value.Trim();
            if (!string.IsNullOrEmpty(typeStr) && Enum.TryParse<VirtualKeyboardType>(typeStr, true, out var parsedType))
                settings.KeyboardSettings.Type = parsedType;
        }

        var startup = root.Element("StartupSettings");
        if (startup != null)
        {
            settings.StartupSettings.RunOnStartup = ReadBool(startup, "RunOnStartup", true);
            settings.StartupSettings.RunOnAdminAndLockScreen = ReadBool(startup, "RunOnAdminAndLockScreen", true);
        }

        var shareVoice = root.Element("ShareVoiceSettings");
        if (shareVoice != null)
        {
            settings.ShareVoiceSettings.Enabled = ReadBool(shareVoice, "Enabled", true);
            settings.ShareVoiceSettings.TapToggleMode = ReadBool(shareVoice, "TapToggleMode", true);
        }

        settings.ForcedCulture = root.Element("ForcedCulture")?.Value;
        return settings;
    }

    public void SaveXml(string xmlPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(xmlPath)!);
        var root = new XElement("AppSettings",
            new XElement("MouseSettings",
                new XElement("Speed", MouseSettings.Speed.ToString(CultureInfo.InvariantCulture)),
                new XElement("Acceleration", MouseSettings.Acceleration.ToString(CultureInfo.InvariantCulture)),
                new XElement("AccelerationThreshold", MouseSettings.AccelerationThreshold.ToString(CultureInfo.InvariantCulture)),
                new XElement("WheelSpeed", MouseSettings.WheelSpeed.ToString(CultureInfo.InvariantCulture))
            ),
            new XElement("NotificationSettings",
                new XElement("ShowBaloonTips", NotificationSettings.ShowBalloonTips.ToString().ToLowerInvariant()),
                new XElement("EnableSoundsOnDisable", NotificationSettings.EnableSoundsOnDisable.ToString().ToLowerInvariant()),
                new XElement("AutoDisableHelpShown", NotificationSettings.AutoDisableHelpShown.ToString().ToLowerInvariant()),
                new XElement("HideNotificationIcon", NotificationSettings.HideNotificationIcon.ToString().ToLowerInvariant())
            ),
            new XElement("SteamSettings",
                new XElement("ShowInAppInSteam", SteamSettings.ShowInAppInSteam.ToString().ToLowerInvariant())
            ),
            new XElement("ControllerSettings",
                new XElement("LeftStickDeadZone", ControllerSettings.LeftStickDeadZone),
                new XElement("RightStickDeadZone", ControllerSettings.RightStickDeadZone)
            ),
            new XElement("DisablerSettings",
                new XElement("PerProcessDisable", DisablerSettings.PerProcessDisable.ToString().ToLowerInvariant())
            ),
            new XElement("KeyboardSettings",
                new XElement("Type", KeyboardSettings.Type.ToString())
            ),
            new XElement("StartupSettings",
                new XElement("RunOnStartup", StartupSettings.RunOnStartup.ToString().ToLowerInvariant()),
                new XElement("RunOnAdminAndLockScreen", StartupSettings.RunOnAdminAndLockScreen.ToString().ToLowerInvariant())
            ),
            new XElement("ShareVoiceSettings",
                new XElement("Enabled", ShareVoiceSettings.Enabled.ToString().ToLowerInvariant()),
                new XElement("TapToggleMode", ShareVoiceSettings.TapToggleMode.ToString().ToLowerInvariant())
            )
        );

        if (!string.IsNullOrWhiteSpace(ForcedCulture))
            root.Add(new XElement("ForcedCulture", ForcedCulture));

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        doc.Save(xmlPath);
    }

    private static float ReadFloat(XElement parent, string name, float defaultValue)
    {
        var el = parent.Element(name);
        if (el != null && float.TryParse(el.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            return v;
        return defaultValue;
    }

    private static int ReadInt(XElement parent, string name, int defaultValue)
    {
        var el = parent.Element(name);
        if (el != null && int.TryParse(el.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            return v;
        return defaultValue;
    }

    private static bool ReadBool(XElement parent, string name, bool defaultValue)
    {
        var el = parent.Element(name);
        if (el != null && bool.TryParse(el.Value.Trim(), out bool v))
            return v;
        return defaultValue;
    }
}
