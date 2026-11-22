using System;
using Mirage;
using NOTCH.Catalogue;
using NOTCH.Net;
using NOTCH.Panel;
using NuclearOption.ModdingApi;

namespace NOTCH.Consent
{
    /// <summary>Answering a dedicated server's notice from in game: the server only signals, the admin's own NOTCH shows the text it carries.</summary>
    internal static class ConsentRemote
    {
        internal const string OnMachine = "It can now be changed only on the server machine itself.";

        // ---- server side ----

        /// <summary>/notch-notice: asks that admin's game to show the notice. Works only while the notice is unanswered.</summary>
        internal static bool Ask(PlayerHandle admin, out string text)
        {
            if (!Plugin.IsServerProcess) { text = "On a hosted game the host answers the notice in their own main menu."; return false; }
            ConsentStore.Refresh();
            if (ConsentStore.Answer != ConsentAnswer.NotAnswered) { text = "The Network Access Notice has already been answered on this server. " + OnMachine; return false; }
            INetworkPlayer owner = admin.GetInternalPlayer()?.Owner;
            if (owner == null) { text = "Could not reach your game."; return false; }
            owner.Send(new NotchNoticeAskV1
            {
                Protocol = Wire.ProtocolVersion, ServerName = Plugin.ServerName(),
                AgreementVersion = Agreement.Current, PreviousVersion = ConsentStore.Previous,
            });
            text = "Opening the Network Access Notice on your screen.";
            return true;
        }

        /// <summary>The admin's answer. Checked again here: an admin, a real answer, the same notice, and still unanswered.</summary>
        internal static void OnAnswer(INetworkPlayer sender, NotchNoticeAnswerV1 msg)
        {
            try
            {
                if (!Plugin.IsServerProcess) return;
                if (!PanelServer.IsAdmin(sender, out string who)) { Plugin.Log.LogWarning($"[notice] ignored an answer from {who}: not an admin."); return; }
                if (msg.Answer != (byte)ConsentAnswer.Accepted && msg.Answer != (byte)ConsentAnswer.Declined) return;
                if (!ConsentState.SameVersion(msg.AgreementVersion, Agreement.Current)) { Plugin.Log.LogWarning("[notice] ignored an answer given for another version of the notice."); return; }
                ConsentStore.Refresh();
                if (ConsentStore.Answer != ConsentAnswer.NotAnswered) { Plugin.Log.LogInfo("[notice] ignored an answer: the notice was already answered."); return; }
                ConsentStore.Set((ConsentAnswer)msg.Answer);
                Plugin.Log.LogMessage($"Network Access Notice: answered in game by an admin: {(ConsentAnswer)msg.Answer}. " + OnMachine);
                CatalogueRunner.AnswerChanged();
            }
            catch (Exception e) { Plugin.Log.LogWarning("[notice] could not record the answer: " + e.Message); }
        }

        /// <summary>The server machine's own command: shows the answer, or sets it.</summary>
        internal static bool FromMachine(string value, out string text)
        {
            ConsentStore.Refresh();
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "":         text = $"Network Access Notice (version {Agreement.Current}): {ConsentStore.Answer}. Set it with: notch-notice <accept|decline|unanswered>"; return true;
                case "accept":   ConsentStore.Set(ConsentAnswer.Accepted); break;
                case "decline":  ConsentStore.Set(ConsentAnswer.Declined); break;
                case "unanswered": ConsentStore.Set(ConsentAnswer.NotAnswered); break;
                default:         text = "Usage: notch-notice <accept|decline|unanswered>"; return false;
            }
            CatalogueRunner.AnswerChanged();
            text = $"Network Access Notice: now {ConsentStore.Answer}.";
            return true;
        }

        // ---- the admin's game ----

        /// <summary>The server asks this game to show the notice. Shown only if this NOTCH carries the very same version.</summary>
        internal static void OnAsk(INetworkPlayer server, NotchNoticeAskV1 ask)
        {
            if (Plugin.IsServerProcess) return;
            string name = string.IsNullOrEmpty(ask.ServerName) ? "this server" : "'" + ask.ServerName + "'";
            if (!ConsentState.SameVersion(ask.AgreementVersion, Agreement.Current))
            {
                Popup.Show(ask.ServerName, "The server's Network Access Notice cannot be shown on this game.", new[]
                {
                    $"The server's notice is version {ask.AgreementVersion}; this copy of NOTCH carries version {Agreement.Current}.",
                    "Update NOTCH, roll it back to the server's version, or give the answer on the server machine itself.",
                }, showReport: false);
                return;
            }
            ConsentWindow.Show($"Your answer applies to the server {name}, not to your own game.", ask.PreviousVersion, true,
                answer => server.Send(new NotchNoticeAnswerV1 { Protocol = Wire.ProtocolVersion, AgreementVersion = Agreement.Current, Answer = (byte)answer }),
                later: null, inMatchOnly: true);
        }
    }
}
