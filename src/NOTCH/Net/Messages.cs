using System;
using System.IO;
using BepInEx.Logging;
using Mirage.Serialization;
using NOTCH.Panel;

namespace NOTCH.Net
{
    // NOTCH's network messages. The type names are part of the protocol: never rename one.
    // A changed layout gets a new struct (…V2).

    /// <summary>Server → joining client: "tell me your mods". Carries what the server knows.</summary>
    public struct NotchInterrogateV1
    {
        public byte Protocol;
        public byte Policy;         // JoinPolicy
        public bool CheckUnlisted;
        public WireMod[] KnownSet;
    }

    /// <summary>Client → server: the answer.</summary>
    public struct NotchTransponderV1
    {
        public byte Protocol;
        public WireMod[] ClientMods;
    }

    /// <summary>Server → client, after the comparison: what the server found. Sent whether or not the player gets in.</summary>
    public struct NotchVerdictV1
    {
        public byte Protocol;
        public byte Policy;         // JoinPolicy
        public bool Refused;        // true = the server is about to turn this player away
        public string ServerName;
        public WireProblem[] Problems;
    }

    /// <summary>Server → an admin's client: everything the NOTCH menu shows. Open = sent because of /notch-menu.</summary>
    public struct NotchPanelStateV1
    {
        public byte Protocol;
        public bool Open;
        public string ServerName, Version;
        public byte Policy;             // JoinPolicy
        public bool ReportUnlisted;
        public byte ReplyTimeout;
        public bool AdminActive, BanFileOk;
        public string Status;           // what the server did with the last request
        public bool StatusOk;
        public PanelMod[] Mods;
        public PanelPerson[] Admins, Bans, Players;
    }

    /// <summary>An admin's client → server: changes to make. The server checks the sender is an admin.</summary>
    public struct NotchPanelActionV1
    {
        public byte Protocol;
        public PanelAction[] Actions;
    }

    /// <summary>Server → an admin's client, when its menu asks: things the manager should know about (leftover mod files).</summary>
    public struct NotchPanelNoteV1
    {
        public byte Protocol;
        public string[] Leftovers;
    }

    /// <summary>Server → an admin's client: "show the notice you carry". No notice text travels, only its version.</summary>
    public struct NotchNoticeAskV1
    {
        public byte Protocol;
        public string ServerName, AgreementVersion, PreviousVersion;
    }

    /// <summary>An admin's client → server: the answer (1 = accepted, 2 = declined). The server checks the sender is an admin.</summary>
    public struct NotchNoticeAnswerV1
    {
        public byte Protocol;
        public string AgreementVersion;
        public byte Answer;
    }

    // Reading: every string goes through SafeText.Clean and every list has a size limit.
    internal static class Wire
    {
        public const byte ProtocolVersion = 1;
        public const int MaxMods = 1000;        // a list longer than this is treated as garbage

        /// <summary>Registers the messages with Mirage. Returns false if an id clashes with a game message.</summary>
        public static bool Register(ManualLogSource log)
        {
            Writer<NotchInterrogateV1>.Write = (w, m) =>
            {
                w.WriteByte(m.Protocol); w.WriteByte(m.Policy); w.WriteBoolean(m.CheckUnlisted); WriteMods(w, m.KnownSet);
            };
            Reader<NotchInterrogateV1>.Read = r => new NotchInterrogateV1
            {
                Protocol = r.ReadByte(), Policy = r.ReadByte(), CheckUnlisted = r.ReadBoolean(), KnownSet = ReadMods(r),
            };
            Writer<NotchTransponderV1>.Write = (w, m) => { w.WriteByte(m.Protocol); WriteMods(w, m.ClientMods); };
            Reader<NotchTransponderV1>.Read = r => new NotchTransponderV1 { Protocol = r.ReadByte(), ClientMods = ReadMods(r) };

            Writer<NotchVerdictV1>.Write = (w, m) =>
            {
                w.WriteByte(m.Protocol); w.WriteByte(m.Policy); w.WriteBoolean(m.Refused); w.WriteString(m.ServerName ?? "");
                WireProblem[] ps = m.Problems ?? new WireProblem[0];
                w.WritePackedUInt32((uint)ps.Length);
                foreach (WireProblem p in ps)
                {
                    w.WriteByte(p.Kind); w.WriteString(p.Guid ?? ""); w.WriteString(p.Name ?? "");
                    w.WriteString(p.ServerVersion ?? ""); w.WriteString(p.ClientVersion ?? "");
                    w.WriteBytesAndSize(p.ServerHash ?? new byte[0]); w.WriteBytesAndSize(p.ClientHash ?? new byte[0]);
                    w.WriteBoolean(p.Required);
                }
            };
            Reader<NotchVerdictV1>.Read = r =>
            {
                var m = new NotchVerdictV1 { Protocol = r.ReadByte(), Policy = r.ReadByte(), Refused = r.ReadBoolean(), ServerName = SafeText.Clean(r.ReadString()) };
                uint count = r.ReadPackedUInt32();
                if (count > MaxMods) throw new InvalidDataException($"NOTCH: problem list of {count} entries refused (limit {MaxMods})");
                m.Problems = new WireProblem[count];
                for (int i = 0; i < count; i++)
                    m.Problems[i] = new WireProblem
                    {
                        Kind = r.ReadByte(), Guid = SafeText.Clean(r.ReadString()), Name = SafeText.Clean(r.ReadString()),
                        ServerVersion = SafeText.Clean(r.ReadString()), ClientVersion = SafeText.Clean(r.ReadString()),
                        ServerHash = r.ReadBytesAndSize() ?? new byte[0], ClientHash = r.ReadBytesAndSize() ?? new byte[0],
                        Required = r.ReadBoolean(),
                    };
                return m;
            };

            Writer<NotchPanelStateV1>.Write = (w, m) =>
            {
                w.WriteByte(m.Protocol); w.WriteBoolean(m.Open); w.WriteString(m.ServerName ?? ""); w.WriteString(m.Version ?? "");
                w.WriteByte(m.Policy); w.WriteBoolean(m.ReportUnlisted); w.WriteByte(m.ReplyTimeout); w.WriteBoolean(m.AdminActive); w.WriteBoolean(m.BanFileOk);
                w.WriteString(m.Status ?? ""); w.WriteBoolean(m.StatusOk);
                PanelMod[] mods = m.Mods ?? new PanelMod[0];
                w.WritePackedUInt32((uint)mods.Length);
                foreach (PanelMod x in mods)
                {
                    w.WriteString(x.Guid ?? ""); w.WriteString(x.Name ?? ""); w.WriteString(x.Version ?? ""); w.WriteByte(x.Kind); w.WriteByte(x.Where);
                    w.WriteString(x.ScopeTag ?? ""); w.WriteString(x.CompatibleFrom ?? ""); w.WriteByte(x.Required); w.WriteBoolean(x.RequiredAuto);
                    w.WriteString(x.RequiredAutoWhy ?? ""); w.WriteByte(x.Rule); w.WriteBoolean(x.Banned);
                }
                WritePeople(w, m.Admins); WritePeople(w, m.Bans); WritePeople(w, m.Players);
            };
            Reader<NotchPanelStateV1>.Read = r =>
            {
                var m = new NotchPanelStateV1
                {
                    Protocol = r.ReadByte(), Open = r.ReadBoolean(), ServerName = SafeText.Clean(r.ReadString()), Version = SafeText.Clean(r.ReadString()),
                    Policy = r.ReadByte(), ReportUnlisted = r.ReadBoolean(), ReplyTimeout = r.ReadByte(), AdminActive = r.ReadBoolean(), BanFileOk = r.ReadBoolean(),
                    Status = SafeText.Clean(r.ReadString()), StatusOk = r.ReadBoolean(),
                };
                m.Mods = new PanelMod[Count(r)];
                for (int i = 0; i < m.Mods.Length; i++)
                    m.Mods[i] = new PanelMod
                    {
                        Guid = SafeText.Clean(r.ReadString()), Name = SafeText.Clean(r.ReadString()), Version = SafeText.Clean(r.ReadString()), Kind = r.ReadByte(), Where = r.ReadByte(),
                        ScopeTag = SafeText.Clean(r.ReadString()), CompatibleFrom = SafeText.Clean(r.ReadString()), Required = r.ReadByte(), RequiredAuto = r.ReadBoolean(),
                        RequiredAutoWhy = SafeText.Clean(r.ReadString()), Rule = r.ReadByte(), Banned = r.ReadBoolean(),
                    };
                m.Admins = ReadPeople(r); m.Bans = ReadPeople(r); m.Players = ReadPeople(r);
                return m;
            };
            Writer<NotchPanelActionV1>.Write = (w, m) =>
            {
                w.WriteByte(m.Protocol);
                PanelAction[] actions = m.Actions ?? new PanelAction[0];
                w.WritePackedUInt32((uint)actions.Length);
                foreach (PanelAction a in actions) { w.WriteByte(a.Kind); w.WriteString(a.Key ?? ""); w.WriteString(a.Value ?? ""); }
            };
            Reader<NotchPanelActionV1>.Read = r =>
            {
                var m = new NotchPanelActionV1 { Protocol = r.ReadByte() };
                m.Actions = new PanelAction[Count(r)];
                for (int i = 0; i < m.Actions.Length; i++) m.Actions[i] = new PanelAction { Kind = r.ReadByte(), Key = SafeText.Clean(r.ReadString()), Value = SafeText.Clean(r.ReadString()) };
                return m;
            };
            Writer<NotchPanelNoteV1>.Write = (w, m) =>
            {
                string[] lines = m.Leftovers ?? new string[0];
                w.WriteByte(m.Protocol); w.WritePackedUInt32((uint)lines.Length);
                foreach (string line in lines) w.WriteString(line.Length > 300 ? line.Substring(0, 300) : line);
            };
            Reader<NotchPanelNoteV1>.Read = r =>
            {
                var m = new NotchPanelNoteV1 { Protocol = r.ReadByte() };
                m.Leftovers = new string[Count(r)];
                for (int i = 0; i < m.Leftovers.Length; i++) m.Leftovers[i] = SafeText.Clean(r.ReadString());
                return m;
            };
            Writer<NotchNoticeAskV1>.Write = (w, m) =>
            {
                w.WriteByte(m.Protocol); w.WriteString(m.ServerName ?? ""); w.WriteString(m.AgreementVersion ?? ""); w.WriteString(m.PreviousVersion ?? "");
            };
            Reader<NotchNoticeAskV1>.Read = r => new NotchNoticeAskV1
            {
                Protocol = r.ReadByte(), ServerName = SafeText.Clean(r.ReadString()), AgreementVersion = SafeText.Clean(r.ReadString()), PreviousVersion = SafeText.Clean(r.ReadString()),
            };
            Writer<NotchNoticeAnswerV1>.Write = (w, m) => { w.WriteByte(m.Protocol); w.WriteString(m.AgreementVersion ?? ""); w.WriteByte(m.Answer); };
            Reader<NotchNoticeAnswerV1>.Read = r => new NotchNoticeAnswerV1 { Protocol = r.ReadByte(), AgreementVersion = SafeText.Clean(r.ReadString()), Answer = r.ReadByte() };

            try
            {
                MessagePacker.RegisterMessage<NotchInterrogateV1>();   // throws if a different message already owns the id
                MessagePacker.RegisterMessage<NotchTransponderV1>();
                MessagePacker.RegisterMessage<NotchVerdictV1>();
                MessagePacker.RegisterMessage<NotchPanelStateV1>();
                MessagePacker.RegisterMessage<NotchPanelActionV1>();
                MessagePacker.RegisterMessage<NotchPanelNoteV1>();
                MessagePacker.RegisterMessage<NotchNoticeAskV1>();
                MessagePacker.RegisterMessage<NotchNoticeAnswerV1>();
            }
            catch (ArgumentException e)
            {
                log.LogError("NOTCH's network message ids clash with another message, so join checks are OFF for this run. " + e.Message);
                return false;
            }
            return true;
        }

        private static int Count(NetworkReader r)
        {
            uint count = r.ReadPackedUInt32();
            if (count > MaxMods) throw new InvalidDataException($"NOTCH: list of {count} entries refused (limit {MaxMods})");
            return (int)count;
        }

        private static void WritePeople(NetworkWriter w, PanelPerson[] people)
        {
            people = people ?? new PanelPerson[0];
            w.WritePackedUInt32((uint)people.Length);
            foreach (PanelPerson p in people) { w.WritePackedUInt64(p.Id); w.WriteString(p.Note ?? ""); w.WriteBoolean(p.Online); w.WriteBoolean(p.Admin); }
        }

        private static PanelPerson[] ReadPeople(NetworkReader r)
        {
            var people = new PanelPerson[Count(r)];
            for (int i = 0; i < people.Length; i++) people[i] = new PanelPerson { Id = r.ReadPackedUInt64(), Note = SafeText.Clean(r.ReadString()), Online = r.ReadBoolean(), Admin = r.ReadBoolean() };
            return people;
        }

        private static void WriteMods(NetworkWriter w, WireMod[] mods)
        {
            mods = mods ?? new WireMod[0];
            w.WritePackedUInt32((uint)mods.Length);
            foreach (WireMod m in mods)
            {
                w.WriteString(m.Guid ?? ""); w.WriteString(m.Name ?? ""); w.WriteString(m.Version ?? "");
                w.WriteByte(m.Scope); w.WriteByte(m.Kind);
                w.WriteBytesAndSize(m.Sha256 ?? new byte[0]);
                w.WriteString(m.Source ?? "");
            }
        }

        private static WireMod[] ReadMods(NetworkReader r)
        {
            uint count = r.ReadPackedUInt32();
            if (count > MaxMods) throw new InvalidDataException($"NOTCH: mod list of {count} entries refused (limit {MaxMods})");
            var mods = new WireMod[count];
            for (int i = 0; i < count; i++)
            {
                mods[i] = new WireMod
                {
                    Guid = SafeText.Clean(r.ReadString()), Name = SafeText.Clean(r.ReadString()), Version = SafeText.Clean(r.ReadString()),
                    Scope = r.ReadByte(), Kind = r.ReadByte(),
                    Sha256 = r.ReadBytesAndSize() ?? new byte[0],
                    Source = SafeText.Clean(r.ReadString()),
                };
            }
            return mods;
        }

        public static WireMod ToWire(ModEntry e) => new WireMod
        {
            Guid = e.Guid, Name = e.Name, Version = e.Version, Scope = (byte)(e.Required ? 3 : 0), Kind = (byte)e.Kind,
            Sha256 = Hex.ToBytes(e.Sha256), Source = "",
        };
    }
}
