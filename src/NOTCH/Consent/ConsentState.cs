using System;

namespace NOTCH.Consent
{
    /// <summary>The answer to the Network Access Notice. Stored in NOTCH's config file.</summary>
    public enum ConsentAnswer { NotAnswered = 0, Accepted = 1, Declined = 2 }

    /// <summary>The answer that holds now, and what to write back to the config file.</summary>
    public struct ConsentNow
    {
        public ConsentAnswer Answer;
        public string Version;      // the agreement version to store
        public string Previous;     // the version last answered, kept until the current one is answered; "" when none
        public bool WasReset;       // the stored answer was for another version, so NOTCH must ask again
    }

    /// <summary>Decides whether a stored answer still counts. Pure logic.</summary>
    public static class ConsentState
    {
        public static ConsentNow Resolve(ConsentAnswer stored, string storedVersion, string storedPrevious, string currentVersion)
        {
            if (!Enum.IsDefined(typeof(ConsentAnswer), stored)) stored = ConsentAnswer.NotAnswered;
            if (SameVersion(storedVersion, currentVersion))
                return new ConsentNow { Answer = stored, Version = currentVersion, Previous = stored == ConsentAnswer.NotAnswered ? Clean(storedPrevious) : "" };
            return new ConsentNow
            {
                Answer = ConsentAnswer.NotAnswered,
                Version = currentVersion,
                Previous = stored != ConsentAnswer.NotAnswered ? Clean(storedVersion) : Clean(storedPrevious),
                WasReset = true
            };
        }

        /// <summary>NOTCH may contact the catalogue only after an explicit yes.</summary>
        public static bool MayConnect(ConsentAnswer answer) => answer == ConsentAnswer.Accepted;

        public static bool SameVersion(string a, string b) => string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);

        private static string Clean(string version)
        {
            string v = (version ?? "").Trim();
            return v.Length > 0 && (v[0] == 'v' || v[0] == 'V') ? v.Substring(1) : v;
        }
    }
}
