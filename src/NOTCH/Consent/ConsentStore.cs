using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace NOTCH.Consent
{
    /// <summary>Keeps the answer to the Network Access Notice in NOTCH's config file, and writes the notice out as text.</summary>
    internal static class ConsentStore
    {
        private const string Section = "Network Access Notice";
        internal const string NoticeShown = "BepInEx/config/NOTCH/" + Agreement.FileName;
        internal const string PreviousFileName = "NETWORK-ACCESS-NOTICE-previous.txt";

        private static ConfigFile config;
        private static ConfigEntry<ConsentAnswer> answer;
        private static ConfigEntry<string> version, previous;
        private static ConfigEntry<bool> askInGame;
        private static DateTime stamp;

        internal static ConsentAnswer Answer => answer.Value;
        internal static bool MayConnect => ConsentState.MayConnect(answer.Value);
        /// <summary>The agreement version last answered, while the current one is unanswered. "" when none.</summary>
        internal static string Previous => previous.Value;
        /// <summary>False after "Don't ask again": no pop-up, only the reminder in the console.</summary>
        internal static bool AskInGame => askInGame.Value;

        /// <summary>Called once from Plugin.Awake.</summary>
        internal static void Init(ConfigFile file)
        {
            config = file;
            answer = file.Bind(Section, "Answer", ConsentAnswer.NotAnswered,
                "Your answer to the notice in " + NoticeShown + ". " +
                "NotAnswered = no answer yet: NOTCH contacts nothing and asks (a pop-up in the main menu of a game; a block in the console at every start of a dedicated server). " +
                "Accepted = you have read the notice and agree: when this NOTCH runs a server or hosts a game, it may read NOMNOM's public mod catalogue. " +
                "Declined = NOTCH contacts nothing and does not ask again. " +
                "NOTCH works either way, and you can change this at any time.");
            version = file.Bind(Section, "AgreementVersion", "",
                "Written by NOTCH: the version of the notice the answer above belongs to. Leave it alone. " +
                "If it differs from the notice this NOTCH carries, the answer is reset once and asked again.");
            previous = file.Bind(Section, "PreviousAgreementVersion", "",
                "Written by NOTCH: the version of the notice last answered, kept so the changes can be shown. Leave it alone.");
            askInGame = file.Bind(Section, "AskInGame", true,
                "Game only. true = while the notice is unanswered, NOTCH shows it as a pop-up in the main menu at every start. " +
                "false = no pop-up (set by \"Don't ask again\"): the notice stays unanswered, NOTCH contacts nothing, a reminder is printed in the BepInEx console and log at every start, " +
                "and you answer by setting Answer above. NOTCH sets this back to true if the notice itself changes.");
            Settle();
            WriteTexts();
        }

        /// <summary>Picks up a hand edit of the config file. Looks only at the file's date, so it costs next to nothing.</summary>
        internal static void Refresh()
        {
            try
            {
                if (config == null || File.GetLastWriteTimeUtc(config.ConfigFilePath) == stamp) return;
                ConsentAnswer before = answer.Value;
                config.Reload();
                Settle();
                if (answer.Value != before) Plugin.Log.LogInfo($"Network Access Notice: the answer in the config file is now {answer.Value}.");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not re-read NOTCH's config file: " + e.Message); }
        }

        /// <summary>Records an answer given in the pop-up or by command, and stamps the current agreement version.</summary>
        internal static void Set(ConsentAnswer given) => Store(given, Agreement.Current, "", true);

        /// <summary>"Don't ask again": the pop-up stays away; the notice is still unanswered.</summary>
        internal static void StopAsking() => Store(answer.Value, version.Value, previous.Value, false);

        /// <summary>Applies the version rule to what the file holds, and writes back only if something changed.</summary>
        private static void Settle()
        {
            ConsentNow now = ConsentState.Resolve(answer.Value, version.Value, previous.Value, Agreement.Current);
            if (now.WasReset && answer.Value != ConsentAnswer.NotAnswered)
                Plugin.Log.LogWarning($"The Network Access Notice has changed (version {now.Version}). Your earlier answer no longer counts; NOTCH contacts nothing until you answer again.");
            bool ask = now.WasReset || askInGame.Value;      // a changed notice asks again, pop-up included
            if (answer.Value != now.Answer || version.Value != now.Version || previous.Value != now.Previous || askInGame.Value != ask) Store(now.Answer, now.Version, now.Previous, ask);
            else stamp = File.GetLastWriteTimeUtc(config.ConfigFilePath);
        }

        private static void Store(ConsentAnswer a, string v, string p, bool ask)
        {
            bool saveEach = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;                 // one write to the file, not four
            try { answer.Value = a; version.Value = v; previous.Value = p; askInGame.Value = ask; }
            finally { config.SaveOnConfigSet = saveEach; }
            config.Save();
            stamp = File.GetLastWriteTimeUtc(config.ConfigFilePath);
        }

        /// <summary>Writes the notice (and the previous one, when there is one to compare with) beside NOTCH's other files.</summary>
        private static void WriteTexts()
        {
            try
            {
                string dir = Path.Combine(Paths.ConfigPath, Plugin.Name);
                Directory.CreateDirectory(dir);
                WriteIfDifferent(Path.Combine(dir, Agreement.FileName), Agreement.CurrentText);
                string old = Agreement.CanCompare(previous.Value) ? Agreement.TextOf(previous.Value) : null;
                if (old != null) WriteIfDifferent(Path.Combine(dir, PreviousFileName), old);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not write " + NoticeShown + ": " + e.Message); }
        }

        private static void WriteIfDifferent(string path, string text)
        {
            text = text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            if (File.Exists(path) && File.ReadAllText(path) == text) return;
            File.WriteAllText(path, text);
        }

        /// <summary>The reminder in the console and log: shown once per start while the notice is unanswered.</summary>
        internal static void PrintBlock(bool server)
        {
            string bar = new string('#', 78);
            var lines = new System.Collections.Generic.List<string>
            {
                bar,
                "#  NOTCH: NETWORK ACCESS NOTICE - PLEASE READ AND ANSWER",
                "#",
                "#  NOTCH will not contact anything on the internet until you accept.",
                "#  Read the notice:  " + NoticeShown,
            };
            if (!server) lines.Add(askInGame.Value ? "#  Answer in the pop-up in the game's main menu, or" : "#  The pop-up is switched off (AskInGame = false), so");
            lines.Add("#  " + (server ? "Then answer in:   " : "answer in:        ") + "BepInEx/config/" + Plugin.Guid + ".cfg");
            lines.Add("#                    section [" + Section + "], Answer = Accepted  or  Answer = Declined");
            if (server) lines.Add("#  or, as an admin in the game's chat, once:  /notch-notice");
            lines.Add("#");
            lines.Add("#  " + (server ? "The server runs" : "The game runs") + " normally either way. This block is shown at every start until you answer.");
            lines.Add(bar);
            foreach (string line in lines) Plugin.Log.LogWarning(line);
        }
    }
}
