using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NOTCH
{
    /// <summary>Text of overrides.yaml: the per-mod mismatch rules. Pure text in, text out.</summary>
    public static class RuleOverrides
    {
        public const string FileName = "overrides.yaml";
        public const string Header =
            "# NOTCH per-mod rules: what happens when a player has this mod in a version or file that does not match.\n" +
            "# Set in game with /notch-rule (admins only); edits here apply from the next join.\n" +
            "# kick = the player is refused.  warn = the player joins and is told.  off = this mod is not compared at all.\n" +
            "# A mod that is not listed follows the server's Policy.\n";

        internal static readonly Regex Line = new Regex("^\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*([A-Za-z]+)\\s*(#.*)?$");

        /// <summary>Mod id (lower case) → rule. Lines that can't be read are returned in badLines and skipped.</summary>
        public static Dictionary<string, MismatchRule> Parse(string text, out List<string> badLines)
        {
            var rules = new Dictionary<string, MismatchRule>();
            badLines = new List<string>();
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                Match m = Line.Match(line);
                if (m.Success && Enum.TryParse(m.Groups[2].Value, true, out MismatchRule rule) && Enum.IsDefined(typeof(MismatchRule), rule))
                    rules[Key(m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\"))] = rule;
                else badLines.Add(line);
            }
            return rules;
        }

        /// <summary>The file text for these rules, sorted by id, each with the mod's name as a note.</summary>
        public static string Serialize(IDictionary<string, MismatchRule> rules, Func<string, string> nameOf)
        {
            var sb = new StringBuilder(Header);
            foreach (KeyValuePair<string, MismatchRule> r in rules.OrderBy(r => r.Key, StringComparer.Ordinal))
            {
                string name = nameOf?.Invoke(r.Key);
                name = new string((name ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
                sb.Append(CheckReport.Quote(r.Key)).Append(": ").Append(r.Value.ToString().ToLowerInvariant());
                if (name.Length > 0) sb.Append("   # ").Append(name);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Mod ids are compared without regard to upper/lower case.</summary>
        public static string Key(string guid) => (guid ?? "").Trim().ToLowerInvariant();
    }

    /// <summary>Text of banned.yaml: mods nobody may join with. Pure text.</summary>
    public static class BannedList
    {
        public const string FileName = "banned.yaml";
        public const string Header =
            "# NOTCH: mods no player may join this server with, in any version. Set in game with /notch-ban (admins only);\n" +
            "# edits here apply from the next join. A mod is known by its plugin ID, so renaming its file changes nothing.\n";

        /// <summary>Mod id (lower case) → the mod's name (from the note; "" when there is none).</summary>
        public static Dictionary<string, string> Parse(string text, out List<string> badLines)
        {
            var result = new Dictionary<string, string>();
            badLines = new List<string>();
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                Match m = RuleOverrides.Line.Match(line);
                if (m.Success && m.Groups[2].Value.Equals("banned", StringComparison.OrdinalIgnoreCase))
                    result[RuleOverrides.Key(m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\"))] = m.Groups[3].Success ? m.Groups[3].Value.TrimStart('#').Trim() : "";
                else badLines.Add(line);
            }
            return result;
        }

        public static string Serialize(IDictionary<string, string> banned)
        {
            var sb = new StringBuilder(Header);
            foreach (KeyValuePair<string, string> b in banned.OrderBy(b => b.Key, StringComparer.Ordinal))
            {
                string name = new string((b.Value ?? "").Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Replace("#", "").Trim();
                sb.Append(CheckReport.Quote(b.Key)).Append(": banned");
                if (name.Length > 0) sb.Append("   # ").Append(name);
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }

    /// <summary>Which mods every player must have: the manager's choice, then the author's label, then Blueprinter content.</summary>
    public static class RequiredRules
    {
        public const string FileName = "required.yaml";
        public const string BlueprinterGuid = "com.nikkorap.blueprinter";
        public const string Header =
            "# NOTCH: mods the server manager has switched to required (yes) or not required (no) on every player's game.\n" +
            "# Set in game with /notch-require (admins only); edits here apply from the next join.\n" +
            "# A mod that is not listed follows NOTCH's own rule: Blueprinter and Blueprinter content are required,\n" +
            "# as is any mod its author labelled RequiresAll; nothing else is.\n";

        public static Dictionary<string, bool> Parse(string text, out List<string> badLines)
        {
            var result = new Dictionary<string, bool>();
            badLines = new List<string>();
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                Match m = RuleOverrides.Line.Match(line);
                string word = m.Success ? m.Groups[2].Value.ToLowerInvariant() : "";
                if (word == "yes" || word == "on" || word == "true") result[RuleOverrides.Key(Unescape(m.Groups[1].Value))] = true;
                else if (word == "no" || word == "off" || word == "false") result[RuleOverrides.Key(Unescape(m.Groups[1].Value))] = false;
                else badLines.Add(line);
            }
            return result;
        }

        public static string Serialize(IDictionary<string, bool> choices, Func<string, string> nameOf)
        {
            var sb = new StringBuilder(Header);
            foreach (KeyValuePair<string, bool> c in choices.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                string name = new string(((nameOf?.Invoke(c.Key)) ?? "").Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();
                sb.Append(CheckReport.Quote(c.Key)).Append(": ").Append(c.Value ? "yes" : "no");
                if (name.Length > 0) sb.Append("   # ").Append(name);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Sets Required and RequiredWhy on every mod in the known set.</summary>
        public static void Apply(IEnumerable<ModEntry> known, IDictionary<string, bool> managerChoices)
        {
            foreach (ModEntry e in known ?? Enumerable.Empty<ModEntry>())
            {
                e.Required = false; e.RequiredWhy = "";
                if (managerChoices != null && managerChoices.TryGetValue(RuleOverrides.Key(e.Guid), out bool choice))
                {
                    e.Required = choice; e.RequiredWhy = choice ? "set by the server manager" : "";
                    continue;
                }
                e.Required = DefaultFor(e, out string why);
                e.RequiredWhy = why;
            }
        }

        /// <summary>Steps 2–4: is this mod required when the manager has made no choice? Gives the reason.</summary>
        public static bool DefaultFor(ModEntry e, out string why)
        {
            why = "";
            if (e.Origin != ModOrigin.Loaded) return false;                 // the server only knows it; it does not run it
            if (!string.IsNullOrEmpty(e.ScopeTag))
            {
                if (!string.Equals(e.ScopeTag, "RequiresAll", StringComparison.OrdinalIgnoreCase)) return false;   // any other label: not needed by everyone
                why = "labelled RequiresAll by its author"; return true;
            }
            if (e.Kind == ModKind.Nobp || e.BlueprinterContent) { why = "Blueprinter content"; return true; }
            if (string.Equals(e.Guid, BlueprinterGuid, StringComparison.OrdinalIgnoreCase)) { why = "Blueprinter"; return true; }
            if (e.Dependencies != null && e.Dependencies.Any(d => string.Equals(d, BlueprinterGuid, StringComparison.OrdinalIgnoreCase))) { why = "depends on Blueprinter"; return true; }
            return false;
        }

        private static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    /// <summary>Mod names as chat suggestions: no spaces, and duplicates get _2, _3.</summary>
    public static class ModChoices
    {
        public static List<KeyValuePair<string, ModEntry>> Build(IEnumerable<ModEntry> mods)
        {
            var result = new List<KeyValuePair<string, ModEntry>>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModEntry m in (mods ?? Enumerable.Empty<ModEntry>()).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            {
                string token = Regex.Replace((m.Name ?? "").Trim(), "\\s+", "_").TrimStart('/');
                if (token.Length == 0) token = Regex.Replace((m.Guid ?? "mod").Trim(), "\\s+", "_").TrimStart('/');
                if (token.Length == 0) token = "mod";
                string unique = token;
                for (int n = 2; !used.Add(unique); n++) unique = token + "_" + n;
                result.Add(new KeyValuePair<string, ModEntry>(unique, m));
            }
            return result;
        }
    }
}
