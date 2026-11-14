using System.Collections.Generic;
using System.Linq;

namespace NOTCH.Admin
{
    /// <summary>
    /// Reads and edits the text of admins.txt: one Steam ID per line, "#" starts a comment (whole line or after
    /// the ID), blank lines ignored — the same shape as the game's ban list. Pure text in, text out (no file
    /// access), so it is covered by notch/tests.
    /// </summary>
    public static class AdminList
    {
        public const string FileName = "admins.txt";

        public const string Template =
            "# NOTCH admin list. Players listed here get the game's Admin permission level when they join\n" +
            "# (which is what /adminkick and other Admin commands check).\n" +
            "# One Steam ID (the long number, SteamID64) per line. Text after a # is a note for yourself.\n" +
            "# Example:\n" +
            "# 76561198000000000  # PlayerName\n";

        /// <summary>The Steam IDs in the file, plus any lines that are neither an ID, a comment nor blank.</summary>
        public static HashSet<ulong> Parse(string text, out List<string> badLines)
        {
            var ids = new HashSet<ulong>();
            badLines = new List<string>();
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = StripComment(raw);
                if (line.Length == 0) continue;
                if (TryParseSteamId(line, out ulong id)) ids.Add(id); else badLines.Add(raw.Trim());
            }
            return ids;
        }

        // A SteamID64 is a number with fixed parts: which Steam it belongs to, what kind of account it is, and the
        // account's own number. A player's account always falls in this range (public Steam, individual account).
        // Checking the range — not the leading digits — keeps newer accounts valid: their IDs run past 7656119…
        // into 7656120…. Game-server and group IDs fall outside it, and are not people who can be admins.
        public const ulong FirstPlayerId = 76561197960265728;      // account number 0
        public const ulong LastPlayerId = 76561202255233023;       // account number 4,294,967,295

        /// <summary>Every ID in the file with its note (the text after the #), in file order.</summary>
        public static List<KeyValuePair<ulong, string>> Entries(string text)
        {
            var list = new List<KeyValuePair<ulong, string>>();
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (!TryParseSteamId(StripComment(raw), out ulong id) || list.Any(e => e.Key == id)) continue;
                int hash = raw.IndexOf('#');
                list.Add(new KeyValuePair<ulong, string>(id, hash >= 0 ? raw.Substring(hash + 1).Trim() : ""));
            }
            return list;
        }

        /// <summary>A SteamID64 of a player's own account.</summary>
        public static bool TryParseSteamId(string s, out ulong id)
        {
            id = 0;
            s = (s ?? "").Trim();
            return s.Length > 0 && s.All(char.IsDigit) && ulong.TryParse(s, out id) && id >= FirstPlayerId && id <= LastPlayerId;
        }

        /// <summary>The file text with this ID added on a new line (unchanged if it is already there).</summary>
        public static string Add(string text, ulong id, string note)
        {
            if (Parse(text, out _).Contains(id)) return text ?? "";
            string t = (text ?? "").Replace("\r\n", "\n");
            if (t.Length > 0 && !t.EndsWith("\n")) t += "\n";
            note = new string((note ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Replace("#", "").Trim();
            return t + id + (note.Length > 0 ? "  # " + note : "") + "\n";
        }

        /// <summary>The file text without any line holding this ID. Comments and other lines are kept as they are.</summary>
        public static string Remove(string text, ulong id)
        {
            var kept = (text ?? "").Replace("\r\n", "\n").Split('\n')
                .Where(raw => !(TryParseSteamId(StripComment(raw), out ulong found) && found == id));
            return string.Join("\n", kept);
        }

        private static string StripComment(string raw)
        {
            int hash = raw.IndexOf('#');
            return (hash >= 0 ? raw.Substring(0, hash) : raw).Trim();
        }
    }
}
