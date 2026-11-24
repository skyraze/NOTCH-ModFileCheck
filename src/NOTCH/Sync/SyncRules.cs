using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NOTCH.Catalogue;

namespace NOTCH.Sync
{
    /// <summary>The server manager's choice for one mod's config. Auto = no choice made: the defaults decide.</summary>
    public enum SyncChoice : byte { Auto = 0, Sync = 1, Warn = 2, Off = 3 }

    /// <summary>What actually happens to a mod's config. Sync = applied on players' games, Warn = compared and reported, Off = not sent.</summary>
    public enum SyncMode : byte { Off = 0, Warn = 1, Sync = 2 }

    /// <summary>The outcome for one mod: what happens, why in a few words, and whether the manager can change it.</summary>
    public struct SyncVerdict
    {
        public SyncMode Mode;
        public string Why;
        public bool Locked;
    }

    /// <summary>The author's label: [assembly: AssemblyMetadata("NotchConfigSync", "Self" | "Notch" | "Off")].</summary>
    public enum SyncLabel : byte { None = 0, Self = 1, Notch = 2, Off = 3 }

    /// <summary>Reads the label's word and spots sync libraries by name. Pure logic.</summary>
    public static class SyncLabels
    {
        public const string Name = "NotchConfigSync";

        // Libraries that sync a mod's config by themselves. Matched on a referenced file's name or a namespace inside the mod.
        private static readonly string[] Known = { "ServerSync", "Jotunn", "CSync" };

        public static SyncLabel Parse(string word)
        {
            switch ((word ?? "").Trim().ToLowerInvariant())
            {
                case "self": return SyncLabel.Self;
                case "notch": return SyncLabel.Notch;
                case "off": return SyncLabel.Off;
                default: return SyncLabel.None;
            }
        }

        /// <summary>The sync library found, or "". names = the mod's referenced files and the namespaces of its own types.</summary>
        public static string OwnSync(IEnumerable<string> names)
        {
            foreach (string raw in names ?? new string[0])
            {
                string first = (raw ?? "").Split('.')[0];
                string last = (raw ?? "").Substring((raw ?? "").LastIndexOf('.') + 1);
                foreach (string known in Known)
                    if (string.Equals(first, known, StringComparison.OrdinalIgnoreCase) || string.Equals(last, known, StringComparison.OrdinalIgnoreCase)) return known;
            }
            return "";
        }
    }

    /// <summary>Decides whether a mod's config is synced. Pure logic; the order of the checks is the order of authority.</summary>
    public static class SyncDecision
    {
        public static SyncVerdict Decide(bool neverSend, SyncLabel label, string ownSync, CatalogueSide side, bool required, SyncChoice choice)
        {
            if (neverSend) return new SyncVerdict { Mode = SyncMode.Off, Why = "never sent", Locked = true };
            if (label == SyncLabel.Self) return new SyncVerdict { Mode = SyncMode.Off, Why = "author: syncs itself", Locked = true };
            if (label == SyncLabel.Off) return new SyncVerdict { Mode = SyncMode.Off, Why = "author: never sync", Locked = true };
            if (label == SyncLabel.Notch)                       // the author asked for NOTCH's sync: outranks detection and the catalogue, not the manager
            {
                if (choice != SyncChoice.Auto) return new SyncVerdict { Mode = choice == SyncChoice.Sync ? SyncMode.Sync : choice == SyncChoice.Warn ? SyncMode.Warn : SyncMode.Off, Why = "set by you" };
                return new SyncVerdict { Mode = SyncMode.Sync, Why = "author asked for it" };
            }
            if (!string.IsNullOrEmpty(ownSync)) return new SyncVerdict { Mode = SyncMode.Off, Why = "has its own sync (" + ownSync + ")", Locked = true };
            if (side == CatalogueSide.Client) return new SyncVerdict { Mode = SyncMode.Off, Why = "client-only mod (NOMNOM)", Locked = true };
            if (side == CatalogueSide.Server) return new SyncVerdict { Mode = SyncMode.Off, Why = "server-only mod (NOMNOM)", Locked = true };
            switch (choice)
            {
                case SyncChoice.Sync: return new SyncVerdict { Mode = SyncMode.Sync, Why = "set by you" };
                case SyncChoice.Warn: return new SyncVerdict { Mode = SyncMode.Warn, Why = "set by you" };
                case SyncChoice.Off:  return new SyncVerdict { Mode = SyncMode.Off, Why = "set by you" };
            }
            return required ? new SyncVerdict { Mode = SyncMode.Sync, Why = "required mod" } : new SyncVerdict { Mode = SyncMode.Off, Why = "not a required mod" };
        }

        /// <summary>The mode as the manager reads it.</summary>
        public static string Word(SyncMode mode) => mode == SyncMode.Sync ? "sync" : mode == SyncMode.Warn ? "warn" : "off";
    }

    /// <summary>Text of config-sync.yaml: the manager's per-mod choices. Pure text in, text out.</summary>
    public static class SyncChoices
    {
        public const string FileName = "config-sync.yaml";
        public const string Header =
            "# NOTCH config sync: what happens to each mod's settings on players' games while they are connected.\n" +
            "# Set in game with /notch-config or the NOTCH menu (admins only); edits here apply from the next join.\n" +
            "# sync = players get this server's values for the session.  warn = differences are reported, nothing is changed.\n" +
            "# off = this mod's settings are not sent.  A mod that is not listed follows the default: required mods sync, others do not.\n";

        private static readonly Regex Line = new Regex("^\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*([A-Za-z]+)\\s*(#.*)?$");

        /// <summary>Mod id (lower case) → choice. Lines that can't be read are returned in badLines and skipped.</summary>
        public static Dictionary<string, SyncChoice> Parse(string text, out List<string> badLines)
        {
            var choices = new Dictionary<string, SyncChoice>();
            badLines = new List<string>();
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                Match m = Line.Match(line);
                SyncChoice choice = SyncChoice.Auto;
                if (m.Success && TryWord(m.Groups[2].Value, out choice) && choice != SyncChoice.Auto)
                    choices[Key(m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\"))] = choice;
                else badLines.Add(line);
            }
            return choices;
        }

        /// <summary>The file text, sorted by id, each line with the mod's name as a note.</summary>
        public static string Serialize(IDictionary<string, SyncChoice> choices, Func<string, string> nameOf)
        {
            var sb = new StringBuilder(Header);
            foreach (KeyValuePair<string, SyncChoice> c in choices.Where(c => c.Value != SyncChoice.Auto).OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                string name = new string((nameOf?.Invoke(c.Key) ?? "").Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();
                sb.Append('"').Append(c.Key.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\": ").Append(c.Value.ToString().ToLowerInvariant());
                if (name.Length > 0) sb.Append("   # ").Append(name);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>sync, warn, off, or reset/auto (no choice). Any capitals.</summary>
        public static bool TryWord(string word, out SyncChoice choice)
        {
            switch ((word ?? "").Trim().ToLowerInvariant())
            {
                case "sync": choice = SyncChoice.Sync; return true;
                case "warn": choice = SyncChoice.Warn; return true;
                case "off":  choice = SyncChoice.Off; return true;
                case "reset": case "auto": choice = SyncChoice.Auto; return true;
                default: choice = SyncChoice.Auto; return false;
            }
        }

        public static string Key(string guid) => (guid ?? "").Trim().ToLowerInvariant();
    }
}
