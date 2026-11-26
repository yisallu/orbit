using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace OpenGamepadCompanion.Core.SystemIntegration;

public static class StartupManager
{
    public const string RunRegistryValueName = "Orbit";
    public const string ScheduledTaskName = "Orbit_ElevatedStartup";

    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsOriginalControllerCompanionRunning()
    {
        try
        {
            return Process.GetProcessesByName("ControllerCompanion").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsWindowsGameBarCapturingGuideButton()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
            if (key == null)
                return false;
            var val = key.GetValue("UseNexusForGameBarEnabled");
            return val is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetWindowsGameBarGuideCapture(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar");
            key.SetValue("UseNexusForGameBarEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsWindowsControllerToVkDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Input\Settings\ControllerProcessor\ControllerToVKMapping");
            var val = key?.GetValue("Enabled");
            return val is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    public static (bool Success, string Message) SetWindowsControllerToVkDisabled(bool disabled)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Input\Settings\ControllerProcessor\ControllerToVKMapping");
            key.SetValue("Enabled", disabled ? 0 : 1, RegistryValueKind.DWord);
            return disabled
                ? (true, "✓ 已写入屏蔽系统摇杆方向键映射（重启电脑后完全生效）")
                : (true, "已恢复 Windows 默认手柄系统导航（重启电脑后生效）");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "需要管理员权限写入系统注册表，请以管理员身份运行 Orbit 后重试");
        }
        catch (Exception ex)
        {
            return (false, $"修改系统手柄导航设置失败: {ex.Message}");
        }
    }

    public static void RetireLegacyControllerCompanion()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("ControllerCompanion"))
            {
                try { p.Kill(); } catch { }
            }

            // Remove "controllercompanion" from HKCU Accessibility Configuration so atbroker.exe doesn't auto-launch it on login
            using (var accKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Accessibility", writable: true))
            {
                if (accKey?.GetValue("Configuration") is string cfg &&
                    cfg.Contains("controllercompanion", StringComparison.OrdinalIgnoreCase))
                {
                    var remaining = cfg
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(item => !item.Equals("controllercompanion", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    accKey.SetValue("Configuration", string.Join(',', remaining), RegistryValueKind.String);
                }
            }

            using (var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
            {
                if (runKey != null)
                {
                    foreach (var name in runKey.GetValueNames())
                    {
                        if (name.Contains("ControllerCompanion", StringComparison.OrdinalIgnoreCase))
                            runKey.DeleteValue(name, throwOnMissingValue: false);
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }
    }

    public static void RetireLegacyXboxDoubaoVoice()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("XboxDoubaoVoice"))
            {
                try { p.Kill(); } catch { }
            }
            using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            runKey?.DeleteValue("XboxDoubaoVoice", throwOnMissingValue: false);
            RunSchtasks("/Change /TN \"XboxDoubaoVoice\" /Disable");
        }
        catch
        {
            // Ignore
        }
    }

    public static bool IsElevatedTaskInstalled()
    {
        return RunSchtasks($"/Query /TN \"{ScheduledTaskName}\"") == 0;
    }

    public static bool TryTriggerElevatedTask()
    {
        return RunSchtasks($"/Run /TN \"{ScheduledTaskName}\"") == 0;
    }

    public static bool TryRelaunchAsAdmin(string exePath, string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas"
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Applies user startup registration.
    /// - If runOnAdminPopups is true, installs an elevated logon Scheduled Task (HIGHEST privileges) so SendInput works across UAC / admin windows!
    /// - Otherwise uses standard HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
    /// </summary>
    public static (bool Success, string Message) ApplySystemStartup(string exePath, bool runOnStartup, bool runOnAdminPopups)
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);

            if (!runOnStartup && !runOnAdminPopups)
            {
                runKey?.DeleteValue(RunRegistryValueName, throwOnMissingValue: false);
                RunSchtasks($"/Delete /TN \"{ScheduledTaskName}\" /F");
                return (true, "已取消开机自启动注册。");
            }

            if (runOnAdminPopups)
            {
                // Register high-privilege logon scheduled task so mouse/keyboard simulation works over Administrator windows
                runKey?.DeleteValue(RunRegistryValueName, throwOnMissingValue: false);
                int code = RunSchtasks($"/Create /TN \"{ScheduledTaskName}\" /TR \"\\\"{exePath}\\\" --minimized\" /SC ONLOGON /RL HIGHEST /F");
                if (code == 0)
                    return (true, "已注册管理员级登录自启计划任务（支持跨管理员窗口控制）。");

                // Fallback to HKCU Run if non-elevated process couldn't create RL HIGHEST task
                runKey?.SetValue(RunRegistryValueName, $"\"{exePath}\" --minimized");
                return (false, "当前非管理员权限，已先注册普通开机自启；如需注册管理员窗口自启计划任务，请以管理员身份运行本程序再点应用。");
            }
            else
            {
                RunSchtasks($"/Delete /TN \"{ScheduledTaskName}\" /F");
                runKey?.SetValue(RunRegistryValueName, $"\"{exePath}\" --minimized");
                return (true, "已注册开机自动运行。");
            }
        }
        catch (Exception ex)
        {
            return (false, $"设置启动项失败: {ex.Message}");
        }
    }

    private static int RunSchtasks(string arguments)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            proc?.WaitForExit(4000);
            return proc?.ExitCode ?? -1;
        }
        catch
        {
            return -1;
        }
    }
}
