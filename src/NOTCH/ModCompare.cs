using System;
using System.Collections.Generic;
using System.Linq;
using NOTCH.Net;

namespace NOTCH
{
    /// <summary>What a server does when a joining player's mods don't match. The numbers travel on the network — don't reorder.</summary>
    public enum JoinPolicy : byte
    {
        /// <summary>Nobody is asked, nothing is checked.</summary>
        Off = 0,
        /// <summary>Nobody is refused. A player with NOTCH is shown a notice listing every difference.</summary>
        WarnOnly = 1,
        /// <summary>Refused: players without NOTCH, and players with a problem on a required mod, a banned mod, or a mod set to "kick". Everything else: joins, shown a notice. The default.</summary>
        RequireNotch = 2,
        /// <summary>Refused: players without NOTCH, or with any difference the server manager has not set to "warn" or "off".</summary>
        RequireMatch = 3,
    }

    /// <summary>What a version or file mismatch on one mod costs. Set with /notch-rule.</summary>
    public enum MismatchRule
    {
        /// <summary>A mismatch refuses the join.</summary>
        Kick,
        /// <summary>A mismatch only shows in the notice, even under RequireMatch.</summary>
        Warn,
        /// <summary>This mod's version and file are not compared at all.</summary>
        Off,
    }

    public enum ProblemKind { Unlisted, VersionOlder, VersionNewer, VersionDiffers, HashMismatch, MissingRequired, Banned }

    /// <summary>One thing that does not match between a joining client and the server.</summary>
    public sealed class Problem
    {
        public ProblemKind Kind;
        public string Guid, Name;
        public string ServerVersion = "", ClientVersion = "";
        public string ServerHash = "", ClientHash = "";      // full hex
        /// <summary>The problem is about a mod the server requires (missing, wrong version or wrong file).</summary>
        public bool Required;
        /// <summary>Server side only: the manager's rule for this mod, if there is one. Not sent to the client.</summary>
        public MismatchRule? Rule;

        public WireProblem ToWire() => new WireProblem
        {
            Kind = (byte)Kind, Guid = Guid, Name = Name, ServerVersion = ServerVersion, ClientVersion = ClientVersion,
            ServerHash = Hex.ToBytes(ServerHash), ClientHash = Hex.ToBytes(ClientHash), Required = Required,
        };

        public static Problem FromWire(WireProblem w) => new Problem
        {
            Kind = (ProblemKind)w.Kind, Guid = w.Guid ?? "", Name = w.Name ?? "", ServerVersion = w.ServerVersion ?? "", ClientVersion = w.ClientVersion ?? "",
            ServerHash = Hex.FromBytes(w.ServerHash), ClientHash = Hex.FromBytes(w.ClientHash), Required = w.Required,
        };

        /// <summary>The kind of problem in two or three words (used in the report file).</summary>
        public string Issue => Kind == ProblemKind.Unlisted ? "not on this server" : Kind == ProblemKind.HashMismatch ? "hash mismatch"
                             : Kind == ProblemKind.MissingRequired ? "missing required mod" : Kind == ProblemKind.Banned ? "not allowed on this server"
                             : "version mismatch";

        /// <summary>What the player should do, or "" when there is nothing they can fix themselves.</summary>
        public string Fix
        {
            get
            {
                switch (Kind)
                {
                    case ProblemKind.VersionOlder:   return $"Update to v{ServerVersion}";
                    case ProblemKind.VersionNewer:   return $"Roll back to v{ServerVersion}";
                    case ProblemKind.VersionDiffers: return $"Use the server's version, v{ServerVersion}";
                    case ProblemKind.HashMismatch:   return "Verify / reinstall your mod files";
                    case ProblemKind.MissingRequired: return ServerVersion.Length > 0 ? $"Install {Name} v{ServerVersion}" : $"Install {Name}";
                    case ProblemKind.Banned:         return "Remove it";
                    default:                         return "";
                }
            }
        }

        /// <summary>One short line for the game's disconnect box, which does not wrap text.</summary>
        public string Short()
        {
            string name = Name != null && Name.Length > 40 ? Name.Substring(0, 39) + "…" : Name;
            switch (Kind)
            {
                case ProblemKind.MissingRequired: return ServerVersion.Length > 0 ? $"Missing {name} v{ServerVersion}" : $"Missing {name}";
                case ProblemKind.Banned:         return $"{name} is not allowed here";
                case ProblemKind.HashMismatch:   return $"{name} v{ServerVersion}: file differs, reinstall it";
                case ProblemKind.Unlisted:       return $"{name} {ClientVersion}: not on this server".Replace("  ", " ");
                default:                         return $"{name}: need v{ServerVersion}, you have v{ClientVersion}";
            }
        }

        /// <summary>What is wrong, server next to client, and what to do about it.</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case ProblemKind.Unlisted:       return $"{Name} {ClientVersion}: not on this server.";
                case ProblemKind.VersionOlder:   return $"{Name}: server has {ServerVersion}, you have {ClientVersion}. Update to v{ServerVersion}.";
                case ProblemKind.VersionNewer:   return $"{Name}: server has {ServerVersion}, you have {ClientVersion}. Roll back to v{ServerVersion}.";
                case ProblemKind.VersionDiffers: return $"{Name}: server has {ServerVersion}, you have {ClientVersion}. Use the server's version, v{ServerVersion}.";
                case ProblemKind.HashMismatch:   return $"{Name} {ServerVersion}: file differs (server {Hex.Short(ServerHash)}, you {Hex.Short(ClientHash)}). Verify / reinstall your mod files.";
                case ProblemKind.MissingRequired: return $"{Name}: required by this server, and you don't have it. {Fix}.";
                case ProblemKind.Banned:         return $"{Name}: not allowed on this server. Remove it.";
                default:                         return Name;
            }
        }
    }

    /// <summary>The comparison: lists in, problems out. Pure logic. Overview in docs/CODE-GUIDE.md.</summary>
    public static class ModCompare
    {
        /// <param name="known">The server's known set.</param>
        /// <param name="client">What the joining client reported.</param>
        /// <param name="checkUnlisted">Report client mods the server does not know at all.</param>
        /// <param name="rules">The manager's per-mod rules: mod id (lower case) → rule. May be null.</param>
        /// <param name="banned">Mod ids (lower case) nobody may join with, in any version. May be null.</param>
        public static List<Problem> Run(IEnumerable<ModEntry> known, IEnumerable<WireMod> client, bool checkUnlisted,
            IDictionary<string, MismatchRule> rules = null, ICollection<string> banned = null)
        {
            var problems = new List<Problem>();
            List<ModEntry> server = (known ?? Enumerable.Empty<ModEntry>()).ToList();
            List<WireMod> theirs = (client ?? Enumerable.Empty<WireMod>()).ToList();

            foreach (WireMod c in theirs)
            {
                string cHash = Hex.FromBytes(c.Sha256);

                // A banned mod: any version, any file, whether the server knows it or not.
                if (banned != null && banned.Contains(RuleOverrides.Key(c.Guid)))
                {
                    problems.Add(new Problem { Kind = ProblemKind.Banned, Guid = c.Guid, Name = c.Name, ClientVersion = c.Version ?? "", ClientHash = cHash });
                    continue;
                }

                // Same mod = same kind (plugin / patcher / .nobp) and same id, ignoring upper/lower case.
                ModEntry s = server.FirstOrDefault(e => (byte)e.Kind == c.Kind && string.Equals(e.Guid, c.Guid, StringComparison.OrdinalIgnoreCase));
                if (s == null)
                {
                    if (checkUnlisted)
                        problems.Add(new Problem { Kind = ProblemKind.Unlisted, Guid = c.Guid, Name = c.Name, ClientVersion = c.Version ?? "", ClientHash = cHash });
                    continue;
                }

                MismatchRule? rule = rules != null && rules.TryGetValue(RuleOverrides.Key(s.Guid), out MismatchRule own) ? own : (MismatchRule?)null;
                if (rule == MismatchRule.Off) continue;         // the manager switched the comparison off for this mod

                int order = CompareVersions(s.Version, c.Version, out bool comparable);   // <0 client older, >0 client newer
                bool sameVersion = comparable ? order == 0 : string.Equals(Clean(s.Version), Clean(c.Version), StringComparison.OrdinalIgnoreCase);

                // The server's copy decides which older versions are accepted. Nothing newer than it is.
                bool accepted = sameVersion
                    || (comparable && order < 0 && Version.TryParse(Clean(s.CompatibleFrom), out Version from)
                        && Version.TryParse(Clean(c.Version), out Version theirVersion) && theirVersion >= from);

                if (!accepted)
                {
                    problems.Add(new Problem
                    {
                        Kind = !comparable ? ProblemKind.VersionDiffers : (order < 0 ? ProblemKind.VersionOlder : ProblemKind.VersionNewer),
                        Guid = s.Guid, Name = s.Name, ServerVersion = s.Version ?? "", ClientVersion = c.Version ?? "",
                        ServerHash = s.Sha256 ?? "", ClientHash = cHash, Required = s.Required, Rule = rule,
                    });
                    continue;
                }

                // The file hash is only meaningful when both sides claim the same version.
                if (sameVersion && !string.IsNullOrEmpty(s.Sha256) && cHash.Length > 0 &&
                    !string.Equals(s.Sha256, cHash, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add(new Problem
                    {
                        Kind = ProblemKind.HashMismatch, Guid = s.Guid, Name = s.Name,
                        ServerVersion = s.Version ?? "", ClientVersion = c.Version ?? "", ServerHash = s.Sha256, ClientHash = cHash,
                        Required = s.Required, Rule = rule,
                    });
                }
            }

            // Required mods the player does not have at all. A rule of "off" does not lift this.
            foreach (ModEntry s in server.Where(e => e.Required))
                if (!theirs.Any(c => c.Kind == (byte)s.Kind && string.Equals(c.Guid, s.Guid, StringComparison.OrdinalIgnoreCase)))
                    problems.Add(new Problem { Kind = ProblemKind.MissingRequired, Guid = s.Guid, Name = s.Name, ServerVersion = s.Version ?? "", ServerHash = s.Sha256 ?? "", Required = true });
            return problems;
        }

        /// <summary>Does this problem keep the player out under this policy?</summary>
        public static bool Blocks(Problem p, JoinPolicy policy)
        {
            if (policy == JoinPolicy.Off || policy == JoinPolicy.WarnOnly) return false;       // WarnOnly never refuses
            switch (p.Kind)
            {
                case ProblemKind.Banned:
                case ProblemKind.MissingRequired:
                    return true;
                case ProblemKind.Unlisted:
                    return policy == JoinPolicy.RequireMatch;
                default:                                                                        // wrong version or wrong file
                    if (p.Rule == MismatchRule.Kick) return true;                               // the manager's choice wins…
                    if (p.Rule == MismatchRule.Warn) return false;                              // …in both directions
                    return policy == JoinPolicy.RequireMatch || p.Required;
            }
        }

        /// <summary>Numeric comparison (1.10 is newer than 1.9). comparable=false when either is not a plain number version.</summary>
        private static int CompareVersions(string serverVersion, string clientVersion, out bool comparable)
        {
            comparable = Version.TryParse(Clean(serverVersion), out Version s) & Version.TryParse(Clean(clientVersion), out Version c);
            return comparable ? c.CompareTo(s) : 0;
        }

        private static string Clean(string v)
        {
            v = (v ?? "").Trim();
            return v.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? v.Substring(1) : v;
        }
    }
}
