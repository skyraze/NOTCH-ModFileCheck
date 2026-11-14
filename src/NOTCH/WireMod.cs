using System;

namespace NOTCH.Net
{
    /// <summary>One mod as it travels between server and client. Plain data; no game or Mirage types in here.</summary>
    public struct WireMod
    {
        public string Guid;
        public string Name;
        public string Version;
        public byte Scope;      // 0 ClientOnly, 1 ServerOnly, 2 RequiresHost, 3 RequiresAll (0 and 3 are used so far)
        public byte Kind;       // ModKind
        public byte[] Sha256;   // 32 bytes, or empty when unknown
        public string Source;   // "provider:id" for a future installer; empty for now
    }

    /// <summary>One problem as the server tells it to the client (same fields as <see cref="NOTCH.Problem"/>).</summary>
    public struct WireProblem
    {
        public byte Kind;                       // ProblemKind
        public string Guid, Name, ServerVersion, ClientVersion;
        public byte[] ServerHash, ClientHash;   // 32 bytes each, or empty
        public bool Required;                   // the problem is about a mod the server requires
    }

    /// <summary>
    /// Every piece of text that arrives over the network goes through Clean before NOTCH does anything with it.
    /// NOTCH never runs, loads or downloads anything it is sent — text is only compared, shown and written to its
    /// own files — but text can still be used to mislead. So on arrival:
    ///   - control characters and line breaks become a space (no fake lines in a log, a file or the notice);
    ///   - characters that reorder text on screen are dropped (a name can't be made to read backwards);
    ///   - &lt; and &gt; become look-alikes (the game's text boxes treat &lt;tags&gt; as formatting);
    ///   - anything longer than MaxLength is cut.
    /// </summary>
    public static class SafeText
    {
        public const int MaxLength = 160;

        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(Math.Min(s.Length, MaxLength));
            foreach (char c in s)
            {
                if (sb.Length >= MaxLength) break;
                if (c == '\u2028' || c == '\u2029' || char.IsControl(c)) { sb.Append(' '); continue; }
                if ((c >= '\u202A' && c <= '\u202E') || (c >= '\u2066' && c <= '\u2069') || c == '\u200E' || c == '\u200F') continue;
                sb.Append(c == '<' ? '\u2039' : c == '>' ? '\u203A' : c);
            }
            return sb.ToString().Trim();
        }
    }

    /// <summary>Hashes are hex text in files and logs, raw bytes on the wire.</summary>
    public static class Hex
    {
        public static byte[] ToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0) return new byte[0];
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        public static string FromBytes(byte[] b) => b == null ? "" : BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();

        /// <summary>First 12 characters plus "…", so it is obvious the hash is cut short.</summary>
        public static string Short(string hex) => string.IsNullOrEmpty(hex) ? "(none)" : (hex.Length <= 12 ? hex : hex.Substring(0, 12) + "…");
    }
}
