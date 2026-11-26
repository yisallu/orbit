using Microsoft.Win32;
using OpenGamepadCompanion.Core.Models;

namespace OpenGamepadCompanion.Core.Config;

public sealed class MigrationReport
{
    public string SourceDirectory { get; init; } = "";
    public string TargetDirectory { get; init; } = "";
    public int CopiedRootFiles { get; set; }
    public int CopiedProfiles { get; set; }
    public int CopiedWorkshopProfiles { get; set; }
    public bool DetectedSecureDesktopRegistration { get; set; }
    public List<string> Details { get; } = [];
}

public static class CompanionMigrator
{
    public static string GetDefaultControllerCompanionAppData()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerCompanion");

    public static MigrationReport ImportFromControllerCompanion(string targetConfigDir, string? sourceAppDataDir = null)
    {
        string srcDir = sourceAppDataDir ?? GetDefaultControllerCompanionAppData();
        var report = new MigrationReport
        {
            SourceDirectory = srcDir,
            TargetDirectory = targetConfigDir
        };

        if (!Directory.Exists(srcDir))
        {
            report.Details.Add($"未找到源配置目录: {srcDir}");
            return report;
        }

        Directory.CreateDirectory(targetConfigDir);
        Directory.CreateDirectory(Path.Combine(targetConfigDir, "profiles"));
        Directory.CreateDirectory(Path.Combine(targetConfigDir, "workshop"));

        string[] rootFiles =
        [
            "Settings.xml",
            "MouseModeBindings.cfg",
            "KeyboardModeBindings.cfg",
            "DisabledProcesses.txt",
            "IgnoredFullscreenProcesses.txt",
            "WorkshopApprovedProfiles.txt",
            "WorkshopItems.cache"
        ];

        foreach (var fileName in rootFiles)
        {
            string srcFile = Path.Combine(srcDir, fileName);
            if (File.Exists(srcFile))
            {
                string dstFile = Path.Combine(targetConfigDir, fileName);
                File.Copy(srcFile, dstFile, overwrite: true);
                report.CopiedRootFiles++;
                report.Details.Add($"已迁移主配置文件: {fileName}");
            }
        }

        // Detect original Controller Companion system startup / UAC Secure Desktop registration state
        bool hasSecureDesktop = CheckOriginalSecureDesktopRegistration();
        bool hasStartupShortcut = CheckOriginalStartupRegistration() || hasSecureDesktop;
        report.DetectedSecureDesktopRegistration = hasSecureDesktop;

        // Enrich Settings.xml with exact startup/admin flags while preserving all original values
        string settingsPath = Path.Combine(targetConfigDir, "Settings.xml");
        var settings = AppSettings.LoadXml(settingsPath);
        settings.StartupSettings.RunOnStartup = hasStartupShortcut || settings.StartupSettings.RunOnStartup;
        settings.StartupSettings.RunOnAdminAndLockScreen = hasSecureDesktop || settings.StartupSettings.RunOnAdminAndLockScreen;
        settings.SaveXml(settingsPath);

        // Copy all .ccp profiles
        string srcProfiles = Path.Combine(srcDir, "profiles");
        if (Directory.Exists(srcProfiles))
        {
            foreach (var ccp in Directory.GetFiles(srcProfiles, "*.ccp"))
            {
                string name = Path.GetFileName(ccp);
                File.Copy(ccp, Path.Combine(targetConfigDir, "profiles", name), overwrite: true);
                report.CopiedProfiles++;
                report.Details.Add($"已迁移进程配置: profiles\\{name}");
            }
        }

        // Copy local workshop overrides AND snapshot Steam workshop base_profile.ccp so config is 100% self-contained!
        var steamWorkshopPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string cacheFile = Path.Combine(srcDir, "WorkshopItems.cache");
        if (File.Exists(cacheFile))
        {
            foreach (var line in File.ReadAllLines(cacheFile))
            {
                int comma = line.IndexOf(',');
                if (comma > 0)
                {
                    steamWorkshopPaths[line[..comma].Trim()] = line[(comma + 1)..].Trim();
                }
            }
        }

        string srcWorkshop = Path.Combine(srcDir, "workshop");
        if (Directory.Exists(srcWorkshop))
        {
            foreach (var subDir in Directory.GetDirectories(srcWorkshop))
            {
                string id = Path.GetFileName(subDir);
                string dstSub = Path.Combine(targetConfigDir, "workshop", id);
                Directory.CreateDirectory(dstSub);

                string localProfile = Path.Combine(subDir, "profile.ccp");
                if (File.Exists(localProfile))
                {
                    File.Copy(localProfile, Path.Combine(dstSub, "profile.ccp"), overwrite: true);
                }

                if (steamWorkshopPaths.TryGetValue(id, out var steamDir))
                {
                    string baseProfile = Path.Combine(steamDir, "profile.ccp");
                    if (File.Exists(baseProfile))
                    {
                        File.Copy(baseProfile, Path.Combine(dstSub, "base_profile.ccp"), overwrite: true);
                    }
                }

                report.CopiedWorkshopProfiles++;
                report.Details.Add($"已迁移创意工坊配置及覆盖项: workshop\\{id}");
            }
        }

        return report;
    }

    private static bool CheckOriginalSecureDesktopRegistration()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Accessibility");
            var cfg = key?.GetValue("Configuration") as string;
            if (!string.IsNullOrWhiteSpace(cfg) && cfg.Contains("controllercompanion", StringComparison.OrdinalIgnoreCase))
                return true;

            return Directory.Exists(@"C:\Program Files\Controller Companion");
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckOriginalStartupRegistration()
    {
        try
        {
            string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startupFolder) && Directory.GetFiles(startupFolder, "*Controller*Companion*").Length > 0)
                return true;

            using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (runKey != null)
            {
                foreach (var name in runKey.GetValueNames())
                {
                    if (name.Contains("Controller", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch
        {
            // Ignore
        }
        return false;
    }
}
