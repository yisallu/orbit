using OpenGamepadCompanion.Core.Models;

namespace OpenGamepadCompanion.Core.Config;

public sealed class ConfigManager
{
    public string ConfigDirectory { get; }

    public AppSettings Settings { get; private set; } = new();
    public BindingProfileMap DesktopBindings { get; private set; } = DefaultBindings.CreateDefaultMouseModeMap();
    public BindingProfileMap KeyboardBindings { get; private set; } = DefaultBindings.CreateDefaultKeyboardModeMap();
    public List<ProcessProfile> Profiles { get; private set; } = [];
    public HashSet<string> DisabledProcesses { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> IgnoredFullscreenProcesses { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> WorkshopApprovedIds { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public event Action? ConfigurationChanged;

    public ConfigManager(string configDirectory)
    {
        ConfigDirectory = configDirectory;
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(Path.Combine(ConfigDirectory, "profiles"));
        Directory.CreateDirectory(Path.Combine(ConfigDirectory, "workshop"));
    }

    public string SettingsPath => Path.Combine(ConfigDirectory, "Settings.xml");
    public string MouseBindingsPath => Path.Combine(ConfigDirectory, "MouseModeBindings.cfg");
    public string KeyboardBindingsPath => Path.Combine(ConfigDirectory, "KeyboardModeBindings.cfg");
    public string DisabledProcessesPath => Path.Combine(ConfigDirectory, "DisabledProcesses.txt");
    public string IgnoredFullscreenProcessesPath => Path.Combine(ConfigDirectory, "IgnoredFullscreenProcesses.txt");
    public string WorkshopApprovedPath => Path.Combine(ConfigDirectory, "WorkshopApprovedProfiles.txt");
    public string WorkshopCachePath => Path.Combine(ConfigDirectory, "WorkshopItems.cache");
    public string ProfilesDirectory => Path.Combine(ConfigDirectory, "profiles");
    public string WorkshopDirectory => Path.Combine(ConfigDirectory, "workshop");

    public void ReloadAll()
    {
        Settings = AppSettings.LoadXml(SettingsPath);

        // 1. Desktop bindings = Built-in MouseMode defaults + MouseModeBindings.cfg overrides
        var desktop = DefaultBindings.CreateDefaultMouseModeMap();
        foreach (var overrideEntry in DirectiveParser.ParseCfgFile(MouseBindingsPath))
        {
            desktop.Set(overrideEntry);
        }
        DesktopBindings = desktop;

        // 2. Virtual Keyboard bindings = Built-in KeyboardMode defaults + KeyboardModeBindings.cfg overrides
        var keyboard = DefaultBindings.CreateDefaultKeyboardModeMap();
        foreach (var overrideEntry in DirectiveParser.ParseCfgFile(KeyboardBindingsPath))
        {
            keyboard.Set(overrideEntry);
        }
        KeyboardBindings = keyboard;

        // 3. Process lists
        DisabledProcesses = ReadLineSet(DisabledProcessesPath);
        IgnoredFullscreenProcesses = ReadLineSet(IgnoredFullscreenProcessesPath);
        WorkshopApprovedIds = ReadLineSet(WorkshopApprovedPath);

        // 4. Local .ccp profiles
        var loadedProfiles = new List<ProcessProfile>();
        if (Directory.Exists(ProfilesDirectory))
        {
            foreach (var ccpPath in Directory.GetFiles(ProfilesDirectory, "*.ccp").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var p = DirectiveParser.ParseCcpFile(ccpPath);
                    p.CanLaunchPrograms = true;
                    loadedProfiles.Add(p);
                }
                catch
                {
                    // Skip malformed file safely
                }
            }
        }

        // 5. Workshop profiles (WorkshopItems.cache + local workshop/<id>/profile.ccp overrides)
        var workshopBaseMap = ReadWorkshopCache(WorkshopCachePath);
        foreach (var (id, workshopFolder) in workshopBaseMap)
        {
            string baseCcp = Path.Combine(workshopFolder, "profile.ccp");
            string mergedLocalCcp = Path.Combine(WorkshopDirectory, id, "base_profile.ccp");
            string sourceBase = File.Exists(mergedLocalCcp) ? mergedLocalCcp : baseCcp;
            if (!File.Exists(sourceBase))
                continue;

            try
            {
                var wp = DirectiveParser.ParseCcpFile(sourceBase);
                wp.IsWorkshop = true;
                wp.WorkshopId = id;
                wp.CanLaunchPrograms = WorkshopApprovedIds.Contains(id);

                string localOverride = Path.Combine(WorkshopDirectory, id, "profile.ccp");
                if (File.Exists(localOverride))
                {
                    var over = DirectiveParser.ParseCcpFile(localOverride);
                    wp.Bindings.AddRange(over.Bindings);
                    wp.FilePath = localOverride;
                }

                // Local explicit profiles take priority over workshop profiles for the same process
                if (!loadedProfiles.Any(lp => wp.ProcessNames.Any(lp.MatchesProcess)))
                {
                    loadedProfiles.Add(wp);
                }
            }
            catch
            {
                // Ignore broken workshop entry
            }
        }

        Profiles = loadedProfiles;
        ConfigurationChanged?.Invoke();
    }

    public void SaveSettings()
    {
        Settings.SaveXml(SettingsPath);
        ConfigurationChanged?.Invoke();
    }

    public void SaveDesktopBindings(BindingProfileMap updatedMap)
    {
        DesktopBindings = updatedMap.Clone();
        SaveDiffAgainstDefaults(DesktopBindings, DefaultBindings.CreateDefaultMouseModeMap(), MouseBindingsPath);
        ConfigurationChanged?.Invoke();
    }

    public void SaveKeyboardBindings(BindingProfileMap updatedMap)
    {
        KeyboardBindings = updatedMap.Clone();
        SaveDiffAgainstDefaults(KeyboardBindings, DefaultBindings.CreateDefaultKeyboardModeMap(), KeyboardBindingsPath);
        ConfigurationChanged?.Invoke();
    }

    public void SaveProcessProfile(ProcessProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.FilePath))
        {
            string safeName = string.Concat(profile.PrimaryProcessName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "profile";
            profile.FilePath = Path.Combine(ProfilesDirectory, $"{safeName}.ccp");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(profile.FilePath)!);
        File.WriteAllText(profile.FilePath, DirectiveParser.SerializeCcp(profile));

        if (!Profiles.Contains(profile))
            Profiles.Add(profile);

        ConfigurationChanged?.Invoke();
    }

    public void DeleteProcessProfile(ProcessProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.FilePath) && File.Exists(profile.FilePath))
            File.Delete(profile.FilePath);
        Profiles.Remove(profile);
        ConfigurationChanged?.Invoke();
    }

    public void SaveDisabledProcesses()
    {
        File.WriteAllLines(DisabledProcessesPath, DisabledProcesses.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        ConfigurationChanged?.Invoke();
    }

    public void SaveIgnoredFullscreenProcesses()
    {
        File.WriteAllLines(IgnoredFullscreenProcessesPath, IgnoredFullscreenProcesses.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        ConfigurationChanged?.Invoke();
    }

    public ProcessProfile? FindProfileForProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return null;

        // Non-workshop profiles first, then workshop profiles
        return Profiles.FirstOrDefault(p => !p.IsWorkshop && p.MatchesProcess(processName))
            ?? Profiles.FirstOrDefault(p => p.IsWorkshop && p.MatchesProcess(processName));
    }

    private static void SaveDiffAgainstDefaults(BindingProfileMap current, BindingProfileMap defaults, string targetCfgPath)
    {
        var diffLines = new List<string>();
        foreach (var (slot, entry) in current.Entries)
        {
            var def = defaults.TryGet(slot);
            if (def == null || !string.Equals(def.RawCommand, entry.RawCommand, StringComparison.OrdinalIgnoreCase))
            {
                diffLines.Add(entry.ToConfigLine());
            }
        }

        // Check if any default slot was removed -> write explicit "null"
        foreach (var (slot, _) in defaults.Entries)
        {
            if (current.TryGet(slot) == null)
            {
                diffLines.Add($"{slot.ToDirectiveString()} @ Nothing: null");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetCfgPath)!);
        File.WriteAllLines(targetCfgPath, diffLines);
    }

    private static HashSet<string> ReadLineSet(string path)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return set;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                set.Add(trimmed);
        }
        return set;
    }

    private static Dictionary<string, string> ReadWorkshopCache(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return dict;

        foreach (var line in File.ReadAllLines(path))
        {
            int comma = line.IndexOf(',');
            if (comma > 0)
            {
                string id = line[..comma].Trim();
                string dir = line[(comma + 1)..].Trim();
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(dir))
                    dict[id] = dir;
            }
        }
        return dict;
    }
}
