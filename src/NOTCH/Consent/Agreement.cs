using System;
using System.Collections.Generic;

namespace NOTCH.Consent
{
    public enum DiffKind { Same, Added, Removed }

    public struct DiffLine
    {
        public DiffKind Kind;
        public string Text;
    }

    /// <summary>One published text of the notice. NewAgreement = a fresh agreement, not compared with earlier ones.</summary>
    public sealed class AgreementVersion
    {
        public readonly string Version;
        public readonly bool NewAgreement;
        public readonly string Text;
        public AgreementVersion(string version, bool newAgreement, string text) { Version = version; NewAgreement = newAgreement; Text = text; }
    }

    /// <summary>The Network Access Notice: which version is current, its texts, and what changed between two of them.</summary>
    public static partial class Agreement
    {
        public const string Title = "NOTCH Network Access Notice and Consent";
        public const string FileName = "NETWORK-ACCESS-NOTICE.txt";

        /// <summary>The version comes from inside NOTCH, never from a file.</summary>
        public static string Current => Versions[Versions.Length - 1].Version;

        public static string CurrentText => Versions[Versions.Length - 1].Text;

        /// <summary>The text of a version NOTCH carries, or null.</summary>
        public static string TextOf(string version)
        {
            int i = IndexOf(version);
            return i < 0 ? null : Versions[i].Text;
        }

        /// <summary>True when the version last answered belongs to the same agreement as the current one.</summary>
        public static bool CanCompare(string previousVersion)
        {
            int previous = IndexOf(previousVersion), current = Versions.Length - 1;
            if (previous < 0 || previous == current) return false;
            for (int i = previous + 1; i <= current; i++) if (Versions[i].NewAgreement) return false;
            return true;
        }

        private static int IndexOf(string version)
        {
            for (int i = 0; i < Versions.Length; i++) if (ConsentState.SameVersion(Versions[i].Version, version)) return i;
            return -1;
        }

        /// <summary>The lines of the new text, with removed lines of the old text placed where they were.</summary>
        public static List<DiffLine> Diff(string oldText, string newText)
        {
            string[] a = Lines(oldText), b = Lines(newText);
            var common = new int[a.Length + 1, b.Length + 1];      // longest common run of lines from each position
            for (int i = a.Length - 1; i >= 0; i--)
                for (int j = b.Length - 1; j >= 0; j--)
                    common[i, j] = a[i] == b[j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);

            var result = new List<DiffLine>();
            int x = 0, y = 0;
            while (x < a.Length && y < b.Length)
            {
                if (a[x] == b[y]) { result.Add(new DiffLine { Kind = DiffKind.Same, Text = a[x] }); x++; y++; }
                else if (common[x + 1, y] >= common[x, y + 1]) result.Add(new DiffLine { Kind = DiffKind.Removed, Text = a[x++] });
                else result.Add(new DiffLine { Kind = DiffKind.Added, Text = b[y++] });
            }
            while (x < a.Length) result.Add(new DiffLine { Kind = DiffKind.Removed, Text = a[x++] });
            while (y < b.Length) result.Add(new DiffLine { Kind = DiffKind.Added, Text = b[y++] });
            return result;
        }

        /// <summary>A changed line as plain text: "+ " added, "- " removed, two spaces unchanged.</summary>
        public static string Mark(DiffLine line) => (line.Kind == DiffKind.Added ? "+ " : line.Kind == DiffKind.Removed ? "- " : "  ") + line.Text;

        private static string[] Lines(string text) => (text ?? "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
    }
}
