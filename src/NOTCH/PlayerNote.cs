using System.Collections.Generic;
using System.Linq;

namespace NOTCH
{
    /// <summary>The text a player reads: the join notice and the refusal reason. Pure logic.</summary>
    public static class PlayerNote
    {
        /// <summary>Problems listed in a refusal. The game's box holds four lines: the header plus these.</summary>
        public const int MaxListed = 3;

        /// <summary>Most serious first: banned, required mod missing, wrong version, different file, unknown mod.</summary>
        public static int Seriousness(Problem p)
        {
            switch (p.Kind)
            {
                case ProblemKind.Banned:          return 0;
                case ProblemKind.MissingRequired: return 1;
                case ProblemKind.HashMismatch:    return 3;
                case ProblemKind.Unlisted:        return 4;
                default:                          return 2;     // the three version kinds
            }
        }

        /// <summary>First line of the notice, e.g. "3 mod problems on this server:".</summary>
        public static string Header(int n) => $"{n} mod problem{(n == 1 ? "" : "s")} on this server:";

        /// <summary>The refusal reason for the game's disconnect box: only the problems that kept the player out.</summary>
        public static string Refusal(IList<Problem> problems)
        {
            int n = problems.Count;
            var lines = new List<string> { $"[NOTCH] Can't join server. {n} problem{(n == 1 ? "" : "s")} detected" + (n > MaxListed ? $", showing {MaxListed}" : "") };
            // OrderBy is stable: problems of the same kind keep the order the comparison found them in.
            lines.AddRange(problems.OrderBy(Seriousness).Take(MaxListed).Select(p => p.Short()));     // short lines: the box does not wrap
            return string.Join("\n", lines);                // never more than 1 + MaxListed = 4 lines
        }

        /// <summary>The refusal reason for a player without NOTCH (policies RequireNotch and RequireMatch).</summary>
        public const string NoNotchRefusal =
            "This server requires the NOTCH mod.\nInstall NOTCH and rejoin.";          // two short lines: the box does not wrap
    }
}
