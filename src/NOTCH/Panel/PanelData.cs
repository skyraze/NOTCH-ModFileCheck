using System.Collections.Generic;

namespace NOTCH.Panel
{
    // What travels between the server and an admin's NOTCH menu. Plain data and pure logic.

    /// <summary>One row of the Mods tab.</summary>
    public struct PanelMod
    {
        public string Guid, Name, Version;
        public byte Kind;               // ModKind
        public byte Where;              // PanelLogic.OnServer … BannedOnly
        public string ScopeTag;         // the author's "who needs it" label, "" when there is none
        public string CompatibleFrom;   // the author's "oldest version that still works" label on the server's copy
        public byte Required;           // the manager's choice: 0 auto, 1 on, 2 off
        public bool RequiredAuto;       // what "auto" gives (label / Blueprinter rule)
        public string RequiredAutoWhy;
        public byte Rule;               // the manager's mismatch rule: 0 auto, 1 kick, 2 warn, 3 off
        public bool Banned;
    }

    /// <summary>A person in the Admins or Player bans tab, or an online player.</summary>
    public struct PanelPerson
    {
        public ulong Id;
        public string Note;             // admin note, ban reason, or player name
        public bool Online;
        public bool Admin;              // online players only: already has the Admin level (or is the host)
    }

    /// <summary>One change an admin asks the server to make.</summary>
    public struct PanelAction
    {
        public byte Kind;               // PanelLogic.Act…
        public string Key, Value;
    }

    public static class PanelLogic
    {
        // Where a mod in the list comes from.
        public const byte OnServer = 0, NoLoadFolder = 1, SkippedClientOnly = 2, SeenOnPlayer = 3, BannedOnly = 4;

        // Kinds of change.
        public const byte ActRequired = 1, ActRule = 2, ActModBan = 3, ActPolicy = 4, ActUnlisted = 5, ActTimeout = 6,
                          ActAdminAdd = 7, ActAdminRemove = 8, ActPlayerBan = 9, ActPlayerUnban = 10, ActPlayerKick = 11, ActRefresh = 12;

        public static readonly string[] RequiredWords = { "auto", "on", "off" };
        public static readonly string[] RuleWords = { "auto", "kick", "warn", "off" };

        public static string WhereText(byte where)
        {
            switch (where)
            {
                case OnServer:          return "On server";
                case NoLoadFolder:      return "NoLoad folder";
                case SkippedClientOnly: return "Client-only";
                case SeenOnPlayer:      return "Seen on player";
                default:                return "Banned";
            }
        }

        /// <summary>"What happens to a player now" for one row. Built on ModCompare.Blocks.</summary>
        public static string Result(JoinPolicy policy, bool reportUnlisted, byte where, bool required, byte rule, bool banned)
        {
            if (policy == JoinPolicy.Off) return "not checked (policy Off)";
            string Outcome(Problem p) => ModCompare.Blocks(p, policy) ? "refused" : "notice";

            if (banned) return "has it: " + Outcome(new Problem { Kind = ProblemKind.Banned });
            if (where == SeenOnPlayer || where == BannedOnly)
                return reportUnlisted ? "has it: " + Outcome(new Problem { Kind = ProblemKind.Unlisted }) : "ignored (unlisted mods not reported)";

            var parts = new List<string>();
            if (required) parts.Add("missing: " + Outcome(new Problem { Kind = ProblemKind.MissingRequired, Required = true }));
            if (rule == 3) parts.Add("not compared");
            else
            {
                MismatchRule? r = rule == 1 ? MismatchRule.Kick : rule == 2 ? MismatchRule.Warn : (MismatchRule?)null;
                parts.Add("mismatch: " + Outcome(new Problem { Kind = ProblemKind.VersionOlder, Required = required, Rule = r }));
            }
            return string.Join(" · ", parts);
        }
    }
}
