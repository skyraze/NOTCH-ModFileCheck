using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NOTCH
{
    /// <summary>Builds the text of the player's report file (notch-checks.yaml). No file access.</summary>
    public static class CheckReport
    {
        public const int MaxChecks = 10;
        public const string FileName = "notch-checks.yaml";
        public const string Header = "# NOTCH (Nuclear Option Transponder Check Handshake) — last 10 join checks, newest first";
        private const string EntryStart = "- check: ";

        /// <summary>One join check as YAML text.</summary>
        /// <param name="filePathOf">Given a mod's id, the path of the player's own file for it (or null).</param>
        public static string Entry(DateTime when, string serverName, string policy, bool refused, IList<Problem> problems, Func<string, string> filePathOf)
        {
            int n = problems.Count;
            string result = n == 0 ? "joined, all mods match"
                          : refused ? $"refused, {n} problem{(n == 1 ? "" : "s")}"
                          : $"joined with {n} problem{(n == 1 ? "" : "s")}";
            var sb = new StringBuilder();
            sb.Append(EntryStart).Append(when.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
            sb.Append("  server: ").Append(Quote(string.IsNullOrEmpty(serverName) ? "(unknown)" : serverName)).Append('\n');
            sb.Append("  policy: ").Append(policy).Append('\n');
            sb.Append("  result: ").Append(result).Append('\n');
            if (n == 0) { sb.Append("  problems: []\n"); return sb.ToString(); }

            sb.Append("  problems:\n");
            foreach (Problem p in problems)
            {
                sb.Append("    - mod: ").Append(Quote($"{p.Name} ({p.Guid})")).Append('\n');
                sb.Append("      issue: ").Append(p.Issue).Append('\n');
                if (p.Required) sb.Append("      required: yes\n");
                switch (p.Kind)
                {
                    case ProblemKind.Unlisted:
                    case ProblemKind.Banned:
                        sb.Append("      client_version: ").Append(Quote(p.ClientVersion)).Append('\n');
                        break;
                    case ProblemKind.MissingRequired:
                        sb.Append("      server_version: ").Append(Quote(p.ServerVersion)).Append('\n');
                        break;
                    case ProblemKind.HashMismatch:
                        sb.Append("      version: ").Append(Quote(p.ServerVersion)).Append('\n');
                        sb.Append("      server_sha256: ").Append(p.ServerHash).Append('\n');
                        sb.Append("      client_sha256: ").Append(p.ClientHash).Append('\n');
                        break;
                    default:
                        sb.Append("      server_version: ").Append(Quote(p.ServerVersion)).Append('\n');
                        sb.Append("      client_version: ").Append(Quote(p.ClientVersion)).Append('\n');
                        break;
                }
                if (p.Fix.Length > 0) sb.Append("      fix: ").Append(p.Fix).Append('\n');
                string path = filePathOf?.Invoke(p.Guid);
                if (!string.IsNullOrEmpty(path)) sb.Append("      file: ").Append(QuotePath(path)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>The new file content: header, the new check, then the newest of the old checks up to 10 in total.</summary>
        public static string Merge(string existingFile, string newEntry)
        {
            var old = new List<string>();
            StringBuilder current = null;
            foreach (string raw in (existingFile ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.StartsWith(EntryStart)) { if (current != null) old.Add(current.ToString()); current = new StringBuilder(); }
                if (current != null && raw.Length > 0) current.Append(raw).Append('\n');   // anything before the first check (the header) is dropped
            }
            if (current != null) old.Add(current.ToString());

            var sb = new StringBuilder(Header).Append("\n\n").Append(newEntry);
            foreach (string e in old.Take(MaxChecks - 1)) sb.Append('\n').Append(e);
            return sb.ToString();
        }

        /// <summary>Double-quoted YAML text; line breaks and control characters become a space.</summary>
        public static string Quote(string s) => "\"" + Flatten(s).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>Single-quoted YAML text for file paths: backslashes stay as they are, an apostrophe is doubled.</summary>
        public static string QuotePath(string s) => "'" + Flatten(s).Replace("'", "''") + "'";

        private static string Flatten(string s) => new string((s ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray());
    }
}
