using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NOTCH.Catalogue
{
    /// <summary>What NOMNOM says a mod is for. None = the catalogue does not say.</summary>
    public enum CatalogueSide : byte { None = 0, Client = 1, Server = 2, Both = 3 }

    /// <summary>The few facts NOTCH reads from one catalogue entry. The rest of the entry is ignored.</summary>
    public sealed class CatalogueEntry
    {
        public string Id = "";
        public CatalogueSide Side;
        public bool Addon;
        public string Extends = "";                             // the catalogue ID of the mod this one extends, or ""
        public List<string> FileNames = new List<string>();
        public List<string> Hashes = new List<string>();        // lower-case hex, without the "sha256:" prefix
    }

    /// <summary>The outcome of reading a download: the entries, or why it was not the catalogue.</summary>
    public sealed class CatalogueRead
    {
        public bool Ok;
        public string Why = "";
        public List<CatalogueEntry> Entries = new List<CatalogueEntry>();
    }

    /// <summary>Reads the catalogue's text one entry at a time. Anything that is not the catalogue's shape is refused.</summary>
    public static class CatalogueReader
    {
        public const int MaxEntries = 100000;
        public const int MaxIdLength = 200;
        public const int MaxFileNameLength = 260;

        public static CatalogueRead Read(TextReader text)
        {
            var result = new CatalogueRead();
            int bad = 0;
            try
            {
                using (var json = new JsonTextReader(text) { MaxDepth = 32, CloseInput = false, DateParseHandling = DateParseHandling.None })
                {
                    if (!json.Read() || json.TokenType != JsonToken.StartArray) return Fail(result, "it is not a list of mods");
                    while (json.Read() && json.TokenType != JsonToken.EndArray)
                    {
                        if (json.TokenType != JsonToken.StartObject) return Fail(result, "it is not a list of mods");
                        CatalogueEntry entry = ToEntry(JObject.Load(json));     // one entry in memory at a time
                        if (entry == null) bad++; else result.Entries.Add(entry);
                        if (result.Entries.Count + bad > MaxEntries) return Fail(result, "it lists too many mods");
                    }
                }
            }
            catch (JsonException) { return Fail(result, "it could not be read"); }
            if (result.Entries.Count == 0 || bad > result.Entries.Count) return Fail(result, "its entries are not catalogue entries");
            result.Ok = true;
            return result;
        }

        private static CatalogueRead Fail(CatalogueRead result, string why)
        {
            result.Ok = false; result.Why = why; result.Entries.Clear();
            return result;
        }

        /// <summary>Null when the object is not a catalogue entry: no ID, or no release with a file name.</summary>
        private static CatalogueEntry ToEntry(JObject o)
        {
            string id = Text(o["id"]).Trim();
            if (id.Length == 0 || id.Length > MaxIdLength) return null;
            if (!(o["artifacts"] is JArray releases)) return null;
            var entry = new CatalogueEntry { Id = id, Side = ParseSide(Text(o["isClientOrServer"])) };
            foreach (JToken token in releases)
            {
                if (!(token is JObject release)) continue;
                string file = Text(release["fileName"]).Trim();
                if (file.Length > 0 && file.Length <= MaxFileNameLength && !entry.FileNames.Contains(file)) entry.FileNames.Add(file);
                string hash = Text(release["hash"]).Trim().ToLowerInvariant();
                if (hash.StartsWith("sha256:", StringComparison.Ordinal)) hash = hash.Substring(7);
                if (IsSha256(hash) && !entry.Hashes.Contains(hash)) entry.Hashes.Add(hash);
                if (string.Equals(Text(release["type"]).Trim(), "addon", StringComparison.OrdinalIgnoreCase)) entry.Addon = true;
                if (entry.Extends.Length == 0 && release["extends"] is JObject parent)
                {
                    string parentId = Text(parent["id"]).Trim();
                    if (parentId.Length <= MaxIdLength) entry.Extends = parentId;
                }
            }
            return entry.FileNames.Count == 0 ? null : entry;
        }

        public static CatalogueSide ParseSide(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "client": return CatalogueSide.Client;
                case "server": return CatalogueSide.Server;
                case "both":   return CatalogueSide.Both;
                default:       return CatalogueSide.None;
            }
        }

        private static string Text(JToken token) => token != null && token.Type == JTokenType.String ? (string)token : "";

        private static bool IsSha256(string hex)
        {
            if (hex.Length != 64) return false;
            foreach (char c in hex) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }
}
