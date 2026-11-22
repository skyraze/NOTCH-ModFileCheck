using System;

namespace NOTCH.Catalogue
{
    /// <summary>The only addresses NOTCH may ever connect to: NOMNOM's catalogue. Everything else is refused.</summary>
    public static class CatalogueGate
    {
        public const string VersionUrl = "https://kopterbuzz.github.io/NOMNOM/manifest/version.json";
        public const string ManifestUrl = "https://kopterbuzz.github.io/NOMNOM/manifest/manifest.json";

        /// <summary>True only for the two fixed addresses, compared character for character.</summary>
        public static bool Allows(string url) =>
            string.Equals(url, VersionUrl, StringComparison.Ordinal) || string.Equals(url, ManifestUrl, StringComparison.Ordinal);

        /// <summary>Every connection NOTCH makes calls this first. Throws for any other address.</summary>
        public static void Require(string url)
        {
            if (!Allows(url)) throw new InvalidOperationException("NOTCH refused to connect: the address is not NOMNOM's catalogue.");
        }
    }
}
