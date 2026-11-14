using System.Collections.Generic;
using System.Linq;

namespace NOTCH
{
    /// <summary>
    /// The text a player reads: the popup after joining, and the refusal reason in the game's disconnect box.
    /// Pure logic (no game types), so it is covered by notch/tests.
    /// </summary>
    public static class PlayerNote
    {
        /// <summary>The disconnect box is small: at most this many problems are listed, the rest become "…and N more".</summary>
        public const int MaxListed = 6;

        /// <summary>First line of the notice, e.g. "3 mod problems on this server:".</summary>
        public static string Header(int n) => $"{n} mod problem{(n == 1 ? "" : "s")} on this server:";

        /// <summary>The refusal reason for the game's disconnect box: only the problems that kept the player out.</summary>
        public static string Refusal(IList<Problem> problems)
        {
            int n = problems.Count;
            var lines = new List<string> { $"[NOTCH] You can't join this server yet. {n} problem{(n == 1 ? "" : "s")} — fix and rejoin:" };
            lines.AddRange(problems.Take(MaxListed).Select(p => "- " + p.Describe()));
            if (problems.Count > MaxListed) lines.Add($"…and {problems.Count - MaxListed} more.");
            return string.Join("\n", lines);
        }

        /// <summary>The refusal reason for a player without NOTCH (policies RequireNotch and RequireMatch).</summary>
        public const string NoNotchRefusal =
            "This server requires the NOTCH mod (Nuclear Option Transponder Check Handshake). Install NOTCH and rejoin.";
    }
}
