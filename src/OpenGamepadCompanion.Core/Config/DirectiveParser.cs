using System.Globalization;
using System.Windows.Forms;
using OpenGamepadCompanion.Core.Models;

namespace OpenGamepadCompanion.Core.Config;

public static class DirectiveParser
{
    public static BindingAction ParseCommand(string rawCommand)
    {
        if (string.IsNullOrWhiteSpace(rawCommand))
            return BindingAction.None;

        var trimmed = rawCommand.Trim();
        var tokens = Tokenize(trimmed);
        if (tokens.Count == 0)
            return BindingAction.None;

        string head = tokens[0].ToLowerInvariant();
        switch (head)
        {
            case "null" or "none":
                return BindingAction.None;

            case "mouse":
                return ParseMouseCommand(tokens);

            case "key":
                return ParseKeyCommand(tokens);

            case "modifier":
                if (tokens.Count >= 2)
                {
                    var mod = tokens[1].ToLowerInvariant() switch
                    {
                        "back" => ModifierLayer.Back,
                        "alt1" => ModifierLayer.Alt1,
                        "alt2" => ModifierLayer.Alt2,
                        _ => ModifierLayer.Back
                    };
                    return new BindingAction { Kind = ActionKind.ModifierHold, TargetModifier = mod };
                }
                return BindingAction.None;

            case "virtualkeyboard":
                return ParseVirtualKeyboardCommand(tokens);

            case "cmd":
                return ParseCmdCommand(trimmed, tokens);

            default:
                return BindingAction.None;
        }
    }

    private static BindingAction ParseMouseCommand(List<string> tokens)
    {
        if (tokens.Count < 2)
            return BindingAction.None;

        string sub = tokens[1].ToLowerInvariant();
        switch (sub)
        {
            case "left":
                return new BindingAction { Kind = ActionKind.MouseButton, MouseButton = MouseButtonTarget.Left };
            case "right":
                return new BindingAction { Kind = ActionKind.MouseButton, MouseButton = MouseButtonTarget.Right };
            case "middle":
                return new BindingAction { Kind = ActionKind.MouseButton, MouseButton = MouseButtonTarget.Middle };
            case "xbutton1" or "x1" or "fourth":
                return new BindingAction { Kind = ActionKind.MouseButton, MouseButton = MouseButtonTarget.XButton1 };
            case "xbutton2" or "x2" or "fifth":
                return new BindingAction { Kind = ActionKind.MouseButton, MouseButton = MouseButtonTarget.XButton2 };
            case "movement":
            {
                bool inverted = tokens.Skip(2).Any(t => t.Equals("inverted", StringComparison.OrdinalIgnoreCase));
                return new BindingAction { Kind = ActionKind.MouseMovement, Inverted = inverted };
            }
            case "scroll":
            {
                if (tokens.Count == 2)
                    return new BindingAction { Kind = ActionKind.MouseScroll };

                string dir = tokens[2].ToLowerInvariant();
                bool noRepeat = tokens.Skip(3).Any(t => t.Equals("norepeat", StringComparison.OrdinalIgnoreCase));
                int sign = dir == "down" ? -1 : 1;
                return new BindingAction
                {
                    Kind = ActionKind.MouseScrollStep,
                    ScrollDirection = sign,
                    AutoRepeat = !noRepeat
                };
            }
            case "speed":
            {
                float scale = 0.5f;
                if (tokens.Count >= 3 && float.TryParse(tokens[2].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                {
                    scale = v > 5f ? v / 100f : v;
                }
                return new BindingAction { Kind = ActionKind.MouseSpeedScale, SpeedScale = scale };
            }
            default:
                return BindingAction.None;
        }
    }

    private static BindingAction ParseKeyCommand(List<string> tokens)
    {
        if (tokens.Count < 2)
            return BindingAction.None;

        bool noRepeat = tokens.Last().Equals("norepeat", StringComparison.OrdinalIgnoreCase);
        int keyTokenCount = noRepeat ? tokens.Count - 2 : tokens.Count - 1;
        if (keyTokenCount <= 0)
            return BindingAction.None;

        // 4 space-separated keys -> directional stick keys (Up Down Left Right, e.g. "key W S A D norepeat")
        if (keyTokenCount == 4 && !tokens[1].Contains('+'))
        {
            var dirKeys = new List<Keys>(4);
            for (int i = 1; i <= 4; i++)
            {
                if (KeyNameResolver.TryParseKey(tokens[i], out var k))
                    dirKeys.Add(k);
            }
            if (dirKeys.Count == 4)
            {
                return new BindingAction
                {
                    Kind = ActionKind.DirectionalKeys,
                    Keys = dirKeys,
                    AutoRepeat = false
                };
            }
        }

        // Single key or chord separated by '+' (e.g. "key LWin+LShiftKey+Left norepeat")
        var chordParts = tokens[1].Split('+', StringSplitOptions.RemoveEmptyEntries);
        var keys = new List<Keys>();
        foreach (var part in chordParts)
        {
            if (KeyNameResolver.TryParseKey(part, out var k))
                keys.Add(k);
        }

        if (keys.Count == 0)
            return BindingAction.None;

        return new BindingAction
        {
            Kind = ActionKind.KeyChord,
            Keys = keys,
            AutoRepeat = !noRepeat
        };
    }

    private static BindingAction ParseVirtualKeyboardCommand(List<string> tokens)
    {
        if (tokens.Count < 2)
            return BindingAction.None;

        var vk = tokens[1].ToLowerInvariant() switch
        {
            "show" => VirtualKeyboardCommand.Show,
            "hide" => VirtualKeyboardCommand.Hide,
            "select" => VirtualKeyboardCommand.Select,
            "move" => VirtualKeyboardCommand.Move,
            "shift" => VirtualKeyboardCommand.Shift,
            "altgr" => VirtualKeyboardCommand.AltGr,
            "language" => VirtualKeyboardCommand.Language,
            _ => VirtualKeyboardCommand.Show
        };

        return new BindingAction { Kind = ActionKind.VirtualKeyboard, VkCommand = vk };
    }

    private static BindingAction ParseCmdCommand(string fullLine, List<string> tokens)
    {
        if (tokens.Count < 2)
            return BindingAction.None;

        string sub = tokens[1].ToLowerInvariant();
        switch (sub)
        {
            case "turnoffcontroller":
                return new BindingAction { Kind = ActionKind.CommandTurnOffController };

            case "togglehelp":
                return new BindingAction { Kind = ActionKind.CommandToggleHelp };

            case "keycombo":
            {
                var keys = new List<Keys>();
                for (int i = 2; i < tokens.Count; i++)
                {
                    if (KeyNameResolver.TryParseKey(tokens[i], out var k))
                        keys.Add(k);
                }
                return keys.Count > 0
                    ? new BindingAction { Kind = ActionKind.CommandKeyCombo, Keys = keys }
                    : BindingAction.None;
            }

            case "process":
            {
                if (tokens.Count < 3)
                    return BindingAction.None;
                string target = tokens[2];
                string args = tokens.Count > 3 ? string.Join(' ', tokens.Skip(3)) : "";
                return new BindingAction
                {
                    Kind = ActionKind.CommandProcess,
                    ProcessTarget = target,
                    ProcessArguments = args
                };
            }

            default:
                return BindingAction.None;
        }
    }

    public static bool TryParseBindingLine(string line, out BindingEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        int colonIdx = line.IndexOf(':');
        if (colonIdx <= 0)
            return false;

        string left = line[..colonIdx].Trim();
        string right = line[(colonIdx + 1)..].Trim();

        string slotPart = left;
        string titlePart = right;
        int atIdx = left.IndexOf('@');
        if (atIdx > 0)
        {
            slotPart = left[..atIdx].Trim();
            titlePart = left[(atIdx + 1)..].Trim();
        }

        if (!PadSlot.TryParseDirective(slotPart, out var slot))
            return false;

        var action = ParseCommand(right);
        entry = new BindingEntry
        {
            Slot = slot,
            Title = titlePart,
            RawCommand = right,
            Action = action
        };
        return true;
    }

    public static List<BindingEntry> ParseCfgFile(string path)
    {
        var result = new List<BindingEntry>();
        if (!File.Exists(path))
            return result;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            if (TryParseBindingLine(line, out var entry) && entry != null)
                result.Add(entry);
        }
        return result;
    }

    public static ProcessProfile ParseCcpText(string content, string filePath = "")
    {
        var profile = new ProcessProfile { FilePath = filePath };
        using var reader = new StringReader(content);
        string? rawLine;
        while ((rawLine = reader.ReadLine()) != null)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            int colonIdx = line.IndexOf(':');
            if (colonIdx <= 0)
                continue;

            string key = line[..colonIdx].Trim();
            string val = line[(colonIdx + 1)..].Trim();

            if (!key.Contains('@'))
            {
                switch (key.ToLowerInvariant())
                {
                    case "process":
                        foreach (var proc in val.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var clean = proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                ? proc[..^4]
                                : proc;
                            if (!profile.ProcessNames.Contains(clean, StringComparer.OrdinalIgnoreCase))
                                profile.ProcessNames.Add(clean);
                        }
                        continue;

                    case "title":
                        profile.Title = val;
                        continue;

                    case "icon":
                        profile.IconBase64 = val;
                        continue;

                    case "workshop id":
                        profile.WorkshopId = val;
                        continue;

                    case "mouse speed":
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float ms))
                            profile.MouseSpeed = ms;
                        continue;

                    case "mouse acceleration":
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float ma))
                            profile.MouseAcceleration = ma;
                        continue;

                    case "mouse wheelspeed":
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float mw))
                            profile.MouseWheelSpeed = mw;
                        continue;
                }
            }

            if (TryParseBindingLine(line, out var binding) && binding != null)
            {
                profile.Bindings.Add(binding);
            }
        }

        if (string.IsNullOrWhiteSpace(profile.Title))
            profile.Title = profile.PrimaryProcessName;

        return profile;
    }

    public static ProcessProfile ParseCcpFile(string path)
        => ParseCcpText(File.ReadAllText(path), path);

    public static string SerializeCcp(ProcessProfile profile)
    {
        var lines = new List<string>();
        if (profile.ProcessNames.Count > 0)
            lines.Add($"process: {string.Join(' ', profile.ProcessNames)}");
        if (!string.IsNullOrWhiteSpace(profile.Title))
            lines.Add($"title: {profile.Title}");
        if (profile.MouseSpeed.HasValue)
            lines.Add($"mouse speed: {profile.MouseSpeed.Value.ToString(CultureInfo.InvariantCulture)}");
        if (profile.MouseAcceleration.HasValue)
            lines.Add($"mouse acceleration: {profile.MouseAcceleration.Value.ToString(CultureInfo.InvariantCulture)}");
        if (profile.MouseWheelSpeed.HasValue)
            lines.Add($"mouse wheelspeed: {profile.MouseWheelSpeed.Value.ToString(CultureInfo.InvariantCulture)}");

        foreach (var b in profile.Bindings)
            lines.Add(b.ToConfigLine());

        if (!string.IsNullOrWhiteSpace(profile.WorkshopId))
            lines.Add($"workshop id: {profile.WorkshopId}");
        if (!string.IsNullOrWhiteSpace(profile.IconBase64))
            lines.Add($"icon: {profile.IconBase64}");

        return string.Join(Environment.NewLine, lines);
    }

    private static List<string> Tokenize(string input)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }
}
