using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace NOTCH.Sync
{
    /// <summary>One setting as it travels: where it lives in the mod's config, its type, and its value as text.</summary>
    public sealed class SyncSetting
    {
        public string Section = "", Key = "", Type = "", Value = "";
    }

    /// <summary>One mod's settings as the server holds them.</summary>
    public sealed class SyncMod
    {
        public string Guid = "", Version = "";
        public List<SyncSetting> Settings = new List<SyncSetting>();
    }

    /// <summary>Turns settings into bytes and back, splits them into parts, and compresses only when that saves packets. Pure logic.</summary>
    public static class ConfigPack
    {
        public const int PacketBytes = 1180;        // what fits in one network packet
        public const int SplitAt = 64000;           // a part never grows beyond this
        public const int MaxParts = 8;              // overall ceiling: what does not fit is left out and reported
        public const int MaxMods = 1000, MaxSettings = 5000, MaxText = 4096;

        public static int Packets(int bytes) => bytes <= 0 ? 0 : (bytes + PacketBytes - 1) / PacketBytes;

        public static byte[] Write(IEnumerable<SyncMod> mods)
        {
            var list = (mods ?? new SyncMod[0]).ToList();
            using (var bytes = new MemoryStream())
            using (var w = new BinaryWriter(bytes, Encoding.UTF8))
            {
                w.Write((ushort)list.Count);
                foreach (SyncMod mod in list)
                {
                    w.Write(mod.Guid ?? ""); w.Write(mod.Version ?? ""); w.Write((ushort)mod.Settings.Count);
                    foreach (SyncSetting s in mod.Settings) { w.Write(s.Section ?? ""); w.Write(s.Key ?? ""); w.Write(s.Type ?? ""); w.Write(s.Value ?? ""); }
                }
                w.Flush();
                return bytes.ToArray();
            }
        }

        /// <summary>Reads what Write produced. Anything out of shape or over a limit throws InvalidDataException.</summary>
        public static List<SyncMod> Read(byte[] data)
        {
            try
            {
                using (var r = new BinaryReader(new MemoryStream(data ?? new byte[0]), Encoding.UTF8))
                {
                    int count = r.ReadUInt16();
                    if (count > MaxMods) throw new InvalidDataException("too many mods");
                    var mods = new List<SyncMod>(count);
                    for (int i = 0; i < count; i++)
                    {
                        var mod = new SyncMod { Guid = Text(r), Version = Text(r) };
                        int settings = r.ReadUInt16();
                        if (settings > MaxSettings) throw new InvalidDataException("too many settings");
                        for (int j = 0; j < settings; j++) mod.Settings.Add(new SyncSetting { Section = Text(r), Key = Text(r), Type = Text(r), Value = Text(r) });
                        mods.Add(mod);
                    }
                    if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("extra bytes");
                    return mods;
                }
            }
            catch (InvalidDataException) { throw; }
            catch (Exception e) { throw new InvalidDataException("not a settings block: " + e.GetType().Name); }
        }

        private static string Text(BinaryReader r)
        {
            string s = r.ReadString();
            if (s.Length > MaxText) throw new InvalidDataException("text too long");
            return s;
        }

        /// <summary>Splits into parts of whole mods. A mod too big for one part, or beyond the ceiling, is named in leftOut.</summary>
        public static List<byte[]> Parts(IEnumerable<SyncMod> mods, out List<string> leftOut)
        {
            leftOut = new List<string>();
            var parts = new List<byte[]>();
            var current = new List<SyncMod>();
            foreach (SyncMod mod in mods ?? new SyncMod[0])
            {
                if (Write(new[] { mod }).Length > SplitAt) { leftOut.Add(mod.Guid); continue; }
                current.Add(mod);
                if (Write(current).Length <= SplitAt) continue;
                current.RemoveAt(current.Count - 1);                // it did not fit: close this part, start the next with it
                if (parts.Count + 1 >= MaxParts) { leftOut.Add(mod.Guid); continue; }
                parts.Add(Write(current));
                current = new List<SyncMod> { mod };
            }
            if (current.Count > 0) parts.Add(Write(current));
            return parts;
        }

        /// <summary>Compresses, and keeps the result only if it needs fewer packets than the plain bytes.</summary>
        public static byte[] Squeeze(byte[] plain, out bool compressed)
        {
            compressed = false;
            if (Packets(plain.Length) <= 1) return plain;           // already one packet: nothing to gain
            using (var packed = new MemoryStream())
            {
                using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(plain, 0, plain.Length);
                if (Packets((int)packed.Length) >= Packets(plain.Length)) return plain;
                compressed = true;
                return packed.ToArray();
            }
        }

        /// <summary>Undoes Squeeze. Stops with InvalidDataException if the data unpacks to more than maxBytes.</summary>
        public static byte[] Unsqueeze(byte[] data, bool compressed, int maxBytes = SplitAt)
        {
            if (!compressed) { if (data.Length > maxBytes) throw new InvalidDataException("part too large"); return data; }
            try
            {
                using (var inflate = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress))
                using (var plain = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    int n;
                    while ((n = inflate.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (plain.Length + n > maxBytes) throw new InvalidDataException("part unpacks too large");
                        plain.Write(buffer, 0, n);
                    }
                    return plain.ToArray();
                }
            }
            catch (InvalidDataException) { throw; }
            catch (Exception e) { throw new InvalidDataException("could not unpack: " + e.GetType().Name); }
        }
    }

    /// <summary>Collects the parts of one send. Nothing is handed over until every part has arrived.</summary>
    public sealed class PartsBin
    {
        private byte[][] parts;
        private int have;

        /// <summary>Adds a part. Returns the complete list of mods once all are in, otherwise null. A part that does not fit the set starts over.</summary>
        public List<SyncMod> Add(int index, int total, byte[] plain)
        {
            if (total < 1 || total > ConfigPack.MaxParts || index < 0 || index >= total) { parts = null; have = 0; return null; }
            if (parts == null || parts.Length != total || index == 0) { parts = new byte[total][]; have = 0; }
            if (parts[index] == null) have++;
            parts[index] = plain;
            if (have < total) return null;
            var all = new List<SyncMod>();
            foreach (byte[] p in parts) all.AddRange(ConfigPack.Read(p));
            parts = null; have = 0;
            return all;
        }
    }

    /// <summary>The rule for text a server may put into a setting: plain characters only, and short. Blocks links and file paths.</summary>
    public static class PlainText
    {
        public const int MaxLength = 64;

        public static bool Ok(string value)
        {
            if (value == null || value.Length > MaxLength) return false;
            foreach (char c in value)
                if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == ',')) return false;
            return true;
        }
    }

    public enum ApplyResult { Apply, NoSuchSetting, TypeDiffers, NotPlainText, ClientOnly }

    /// <summary>Decides what happens to one setting the server sent. Pure logic.</summary>
    public static class ApplyRule
    {
        public const string TextType = "System.String";
        public const string ClientTag = "NotchClient";

        /// <param name="localType">The type of the player's own setting, or null when their mod has no such setting.</param>
        public static ApplyResult Decide(SyncSetting server, string localType, bool localClientTag)
        {
            if (localType == null) return ApplyResult.NoSuchSetting;
            if (!string.Equals(localType, server.Type, StringComparison.Ordinal)) return ApplyResult.TypeDiffers;
            if (localClientTag) return ApplyResult.ClientOnly;
            if (localType == TextType && !PlainText.Ok(server.Value)) return ApplyResult.NotPlainText;
            return ApplyResult.Apply;
        }

        /// <summary>The reason in a few words, for the player's report.</summary>
        public static string Why(ApplyResult result)
        {
            switch (result)
            {
                case ApplyResult.Apply:         return "applied";
                case ApplyResult.NoSuchSetting: return "not applied: your copy of the mod has no such setting";
                case ApplyResult.TypeDiffers:   return "not applied: the setting is a different kind in your copy of the mod";
                case ApplyResult.NotPlainText:  return "not applied: the server's text is not plain letters and digits";
                default:                        return "not applied: the mod's author marked it as yours alone";
            }
        }
    }
}
