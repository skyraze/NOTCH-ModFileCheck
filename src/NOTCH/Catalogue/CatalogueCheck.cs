using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NOTCH.Catalogue
{
    /// <summary>How a catalogue check ended.</summary>
    public enum CatalogueVerdict { Accepted, TooLarge, TooSmall, NotCatalogue, TooDifferent, CouldNotConnect, TookTooLong }

    /// <summary>What NOTCH remembers between checks so it can judge the next download. A few kilobytes.</summary>
    public sealed class CatalogueMemory
    {
        public long LastGoodSize;
        public List<string> KnownIds = new List<string>();
        public string Rejected = "";        // fingerprint of the last download that was turned away, or ""

        public bool First => LastGoodSize == 0 && KnownIds.Count == 0;

        /// <summary>The memory as text, one fact per line.</summary>
        public string Serialize()
        {
            var sb = new StringBuilder("# NOTCH: what the last accepted NOMNOM catalogue looked like. Written by NOTCH; safe to delete.\n");
            sb.Append("size: ").Append(LastGoodSize).Append('\n');
            if (Rejected.Length > 0) sb.Append("rejected: ").Append(Rejected).Append('\n');
            foreach (string id in KnownIds) sb.Append("id: ").Append(id).Append('\n');
            return sb.ToString();
        }

        /// <summary>Reads that text back. Lines it does not understand are skipped.</summary>
        public static CatalogueMemory Parse(string text)
        {
            var memory = new CatalogueMemory();
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                int colon = line.IndexOf(':');
                if (line.Length == 0 || line[0] == '#' || colon < 1) continue;
                string key = line.Substring(0, colon), value = line.Substring(colon + 1).Trim();
                if (key == "size" && long.TryParse(value, out long size) && size >= 0) memory.LastGoodSize = size;
                else if (key == "rejected") memory.Rejected = value;
                else if (key == "id" && value.Length > 0 && value.Length <= CatalogueReader.MaxIdLength) memory.KnownIds.Add(value);
            }
            return memory;
        }
    }

    /// <summary>The rules that decide whether a download is kept. Pure logic.</summary>
    public static class CatalogueCheck
    {
        public const long MB = 1024L * 1024L;
        public const long DefaultCeiling = 8 * MB;
        public const long MinCeiling = 1 * MB;
        public const long WarnCeiling = 64 * MB;
        public const long MaxCeiling = 1024 * MB;

        /// <summary>The size limit an admin entered, brought inside 1 MB to 1 GB.</summary>
        public static long ClampCeiling(long bytes) => Math.Max(MinCeiling, Math.Min(MaxCeiling, bytes));

        /// <summary>True from 64 MB up: the admin is warned what a file that size could do.</summary>
        public static bool CeilingIsLarge(long bytes) => bytes >= WarnCeiling;

        public static bool TooSmall(long size, long lastGoodSize) => lastGoodSize > 0 && size < lastGoodSize / 4;

        /// <summary>True when fewer than half of the previously known mod IDs are in the new list. Additions never count.</summary>
        public static bool TooDifferent(IEnumerable<string> ids, ICollection<string> knownIds)
        {
            if (knownIds == null || knownIds.Count == 0) return false;
            var now = new HashSet<string>(ids ?? new string[0], StringComparer.OrdinalIgnoreCase);
            int stillThere = knownIds.Count(now.Contains);
            return stillThere * 2 < knownIds.Count;
        }

        /// <summary>A short code for a list of IDs, the same whatever their order or capitals.</summary>
        public static string Fingerprint(IEnumerable<string> ids)
        {
            string joined = string.Join("\n", (ids ?? new string[0]).Select(i => i.ToLowerInvariant()).Distinct().OrderBy(i => i, StringComparer.Ordinal));
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(joined))).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Judges a download that already read as the catalogue, and returns what to remember for next time.</summary>
        public static CatalogueVerdict Judge(long size, IList<string> ids, CatalogueMemory memory, out CatalogueMemory next)
        {
            memory = memory ?? new CatalogueMemory();
            ids = ids ?? new string[0];
            CatalogueVerdict verdict = CatalogueVerdict.Accepted;
            if (!memory.First)      // a first download has nothing to be compared with
            {
                if (TooSmall(size, memory.LastGoodSize)) verdict = CatalogueVerdict.TooSmall;
                else if (TooDifferent(ids, memory.KnownIds)) verdict = CatalogueVerdict.TooDifferent;
            }
            string print = Fingerprint(ids);
            if (verdict != CatalogueVerdict.Accepted && memory.Rejected == print) verdict = CatalogueVerdict.Accepted;   // the same again: a real change
            next = verdict == CatalogueVerdict.Accepted
                ? new CatalogueMemory { LastGoodSize = size, KnownIds = ids.ToList() }
                : new CatalogueMemory { LastGoodSize = memory.LastGoodSize, KnownIds = memory.KnownIds, Rejected = print };
            return verdict;
        }

        /// <summary>Reads a download, stopping the moment it passes the limit or runs out of time. Null = stopped.</summary>
        public static byte[] ReadCapped(Stream stream, long ceiling, Func<bool> outOfTime, out CatalogueVerdict verdict)
        {
            verdict = CatalogueVerdict.Accepted;
            var kept = new MemoryStream();
            var buffer = new byte[16384];
            int n;
            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (kept.Length + n > ceiling) { verdict = CatalogueVerdict.TooLarge; return null; }
                if (outOfTime != null && outOfTime()) { verdict = CatalogueVerdict.TookTooLong; return null; }
                kept.Write(buffer, 0, n);
            }
            return kept.ToArray();
        }

        /// <summary>The catalogue's version as plain text, or "" when the file does not hold a version.</summary>
        public static string CleanVersion(string text)
        {
            string v = (text ?? "").Trim().Trim('"').Trim();
            if (v.Length == 0 || v.Length > 40) return "";
            foreach (char c in v) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '.' || c == '-')) return "";
            return v;
        }

        /// <summary>The verdict in a few plain words, for the log, the menu and the status command.</summary>
        public static string Reason(CatalogueVerdict verdict)
        {
            switch (verdict)
            {
                case CatalogueVerdict.Accepted:        return "validated";
                case CatalogueVerdict.TooLarge:        return "rejected: larger than the size limit";
                case CatalogueVerdict.TooSmall:        return "rejected: far smaller than the last good copy";
                case CatalogueVerdict.NotCatalogue:    return "rejected: not the catalogue's structure";
                case CatalogueVerdict.TooDifferent:    return "rejected: too different from the last good copy";
                case CatalogueVerdict.CouldNotConnect: return "not checked: could not connect";
                default:                               return "not checked: took too long";
            }
        }
    }

    /// <summary>When the next check is due: no sooner than one hour after the last, no later than two.</summary>
    public static class CatalogueSchedule
    {
        public static readonly TimeSpan Earliest = TimeSpan.FromHours(1);
        public static readonly TimeSpan Latest = TimeSpan.FromHours(2);

        /// <param name="lastCheckUtc">DateTime.MinValue when no check has run since the server started.</param>
        public static bool Due(DateTime nowUtc, DateTime lastCheckUtc, bool missionLoading)
        {
            if (lastCheckUtc == DateTime.MinValue) return true;
            TimeSpan since = nowUtc - lastCheckUtc;
            if (since < TimeSpan.Zero || since >= Latest) return true;      // the clock went backwards, or two hours are up
            return missionLoading && since >= Earliest;
        }
    }
}
