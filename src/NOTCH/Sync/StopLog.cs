using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NOTCH.Sync
{
    /// <summary>The server's list of settings that had to be stopped. Kept in a small file so it survives a restart. Pure text in, text out.</summary>
    public sealed class StopLog
    {
        public const int Max = 50;
        public const string FileName = "config-sync-stops.txt";

        public uint Next = 1;                       // the number the next entry gets; never reused, so "newer than" stays meaningful
        public readonly List<KeyValuePair<uint, string>> Entries = new List<KeyValuePair<uint, string>>();

        /// <summary>The highest number ever given out. A notice hidden at this number shows again once it grows.</summary>
        public uint Newest => Next - 1;

        public uint Add(string line)
        {
            uint id = Next++;
            Entries.Add(new KeyValuePair<uint, string>(id, OneLine(line)));
            if (Entries.Count > Max) Entries.RemoveRange(0, Entries.Count - Max);
            return id;
        }

        public bool Remove(uint id) => Entries.RemoveAll(e => e.Key == id) > 0;

        public int Clear() { int n = Entries.Count; Entries.Clear(); return n; }

        public string Serialize()
        {
            var sb = new StringBuilder("# NOTCH: synced settings that had to be stopped on players' games. Written by NOTCH; remove entries in the NOTCH menu.\n");
            sb.Append("next: ").Append(Next).Append('\n');
            foreach (KeyValuePair<uint, string> e in Entries) sb.Append(e.Key).Append('|').Append(e.Value).Append('\n');
            return sb.ToString();
        }

        public static StopLog Parse(string text)
        {
            var log = new StopLog();
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("next:", StringComparison.OrdinalIgnoreCase)) { if (uint.TryParse(line.Substring(5).Trim(), out uint next) && next > 0) log.Next = next; continue; }
                int bar = line.IndexOf('|');
                if (bar > 0 && uint.TryParse(line.Substring(0, bar), out uint id) && id > 0 && line.Length > bar + 1) log.Entries.Add(new KeyValuePair<uint, string>(id, line.Substring(bar + 1)));
            }
            if (log.Entries.Count > Max) log.Entries.RemoveRange(0, log.Entries.Count - Max);
            if (log.Entries.Count > 0) log.Next = Math.Max(log.Next, log.Entries.Max(e => e.Key) + 1);   // a hand-edited file can never make numbers repeat
            return log;
        }

        private static string OneLine(string s) => (s ?? "").Replace("\r", " ").Replace("\n", " ");
    }

    /// <summary>Per admin: which notices they asked not to see again, and up to what. Pure text in, text out.</summary>
    public sealed class NoticeSeen
    {
        public const string FileName = "notices-seen.txt";

        public struct Seen { public uint Stops; public string Leftovers; }
        public readonly Dictionary<string, Seen> ByAdmin = new Dictionary<string, Seen>();

        public Seen Of(string admin) => ByAdmin.TryGetValue(admin ?? "", out Seen s) ? s : new Seen { Leftovers = "" };

        /// <summary>Hidden while every entry still listed is one the admin has already hidden. An entry they removed no longer counts.</summary>
        public bool StopsQuiet(string admin, StopLog log) => log.Entries.Count > 0 && log.Entries.All(e => e.Key <= Of(admin).Stops);

        /// <summary>Hidden until the set of leftover files changes.</summary>
        public bool LeftoversQuiet(string admin, IEnumerable<string> leftovers) => (Of(admin).Leftovers ?? "") == Print(leftovers) && Print(leftovers).Length > 0;

        public void QuietStops(string admin, StopLog log) { Seen s = Of(admin); s.Stops = log.Newest; ByAdmin[admin ?? ""] = s; }
        public void QuietLeftovers(string admin, IEnumerable<string> leftovers) { Seen s = Of(admin); s.Leftovers = Print(leftovers); ByAdmin[admin ?? ""] = s; }

        /// <summary>A short fingerprint of a set of lines; "" for none. The same set in any order gives the same fingerprint.</summary>
        public static string Print(IEnumerable<string> lines)
        {
            List<string> sorted = (lines ?? new string[0]).Where(l => !string.IsNullOrEmpty(l)).OrderBy(l => l, StringComparer.Ordinal).ToList();
            if (sorted.Count == 0) return "";
            uint hash = 2166136261;
            foreach (char c in string.Join("\n", sorted)) { hash ^= c; hash *= 16777619; }
            return hash.ToString("x8");
        }

        public string Serialize()
        {
            var sb = new StringBuilder("# NOTCH: which menu notices each admin chose not to see again. Written by NOTCH; safe to delete.\n");
            foreach (KeyValuePair<string, Seen> a in ByAdmin.OrderBy(a => a.Key, StringComparer.Ordinal))
                sb.Append(a.Key.Replace(' ', '_')).Append(" stops=").Append(a.Value.Stops).Append(" leftovers=").Append(a.Value.Leftovers ?? "").Append('\n');
            return sb.ToString();
        }

        public static NoticeSeen Parse(string text)
        {
            var seen = new NoticeSeen();
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string[] parts = raw.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || parts[0][0] == '#') continue;
                var s = new Seen { Leftovers = "" };
                foreach (string p in parts.Skip(1))
                {
                    if (p.StartsWith("stops=")) uint.TryParse(p.Substring(6), out s.Stops);
                    else if (p.StartsWith("leftovers=")) s.Leftovers = p.Substring(10);
                }
                seen.ByAdmin[parts[0].Replace('_', ' ')] = s;
            }
            return seen;
        }
    }
}
