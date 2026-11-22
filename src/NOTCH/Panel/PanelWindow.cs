using System;
using System.Collections.Generic;
using System.Linq;
using Mirage;
using NOTCH.Admin;
using NOTCH.Net;
using UnityEngine;

namespace NOTCH.Panel
{
    /// <summary>The admin menu (/notch-menu), Unity IMGUI. It only asks; the server checks and does the work.</summary>
    internal static class PanelWindow
    {
        private struct Edit { public byte Req, Rule; public bool Ban; }

        private static bool visible, awaitingApply, statusHidden;
        private static NotchPanelStateV1 st;
        private static INetworkPlayer server;
        private static int tab, show;
        private static readonly Vector2[] scroll = new Vector2[4];
        private static readonly Dictionary<string, Edit> edits = new Dictionary<string, Edit>();   // mod id → pending choice
        private static int policyEdit = -1, timeoutEdit = -1, unlistedEdit = -1;                  // -1 = not changed
        private static string search = "", adminId = "", adminNote = "", banId = "", banReason = "";
        private static string confirmKey;
        private static float confirmUntil;
        private static Texture2D backdrop, rowDark, rowLight;
        private static GUIStyle text, small, bold, heading, rowA, rowB, button, toolbar, field, good, bad, changed;

        private static readonly string[] Tabs = { "Mods", "Server", "Admins", "Player bans" };
        private static readonly string[] ReqLabels = { "Auto", "On", "Off" };
        private static readonly string[] RuleLabels = { "Auto", "Kick", "Warn", "Off" };
        private static readonly string[] ShowLabels = { "All", "Server's", "Seen on players", "Changed" };
        private static readonly string[] PolicyLabels = { "Off", "WarnOnly", "RequireNotch", "RequireMatch" };
        private static readonly string[] PolicyHelp =
        {
            "Players are not asked and nothing is checked.",
            "Nobody is refused. Players with NOTCH are shown a notice listing every difference. Players without NOTCH join unchecked.",
            "Refused: players without NOTCH, with a banned mod, with a required mod missing or mismatched, or with a mismatch on a mod set to Kick. Everything else only shows a notice.",
            "Refused: players without NOTCH, or with any difference at all — except on mods you have set to Warn or Off.",
        };
        private const string BadId = "That is not a player's Steam ID. Use the long number (SteamID64) from their Steam profile.";

        /// <summary>The server sent the menu's contents (after /notch-menu, or after a change we asked for).</summary>
        internal static void OnState(INetworkPlayer from, NotchPanelStateV1 state)
        {
            if (!state.Open && !visible) return;            // an answer that arrived after the menu was closed
            server = from; st = state; statusHidden = false;
            if (state.Open && !visible) { visible = true; tab = 0; ClearEdits(); }
            if (awaitingApply) { awaitingApply = false; if (state.StatusOk) ClearEdits(); }
        }

        internal static void Close()
        {
            if (!visible) return;
            visible = false; ClearEdits();
            Popup.GuardOff();
        }

        private static void ClearEdits() { edits.Clear(); policyEdit = timeoutEdit = unlistedEdit = -1; confirmKey = null; }

        private static void Send(byte kind, string key, string value) =>
            server?.Send(new NotchPanelActionV1 { Protocol = Wire.ProtocolVersion, Actions = new[] { new PanelAction { Kind = kind, Key = key, Value = value } } });

        /// <summary>A button that needs a second click within three seconds ("Sure?") before it counts.</summary>
        private static bool Confirm(string key, string label, float width)
        {
            bool armed = confirmKey == key && Time.realtimeSinceStartup < confirmUntil;
            if (!GUILayout.Button(armed ? "Sure?" : label, button, GUILayout.Width(width))) return false;
            if (armed) { confirmKey = null; return true; }
            confirmKey = key; confirmUntil = Time.realtimeSinceStartup + 3f;
            return false;
        }

        private static JoinPolicy Policy => (JoinPolicy)(policyEdit >= 0 ? policyEdit : st.Policy);
        private static bool ReportUnlisted => unlistedEdit >= 0 ? unlistedEdit == 0 : st.ReportUnlisted;
        private static int PendingCount => edits.Count + (policyEdit >= 0 ? 1 : 0) + (timeoutEdit >= 0 ? 1 : 0) + (unlistedEdit >= 0 ? 1 : 0);

        /// <summary>Called from the plugin's OnGUI every frame.</summary>
        internal static void Draw()
        {
            if (!visible) return;
            if (GameplayUI.i == null) { Close(); return; }  // left the match
            Popup.GuardOn();                                // no clicks or keys reach the game while the menu is open

            if (backdrop == null)
            {
                backdrop = Solid(new Color(0.08f, 0.09f, 0.10f, 0.98f));
                rowDark = Solid(new Color(0.13f, 0.14f, 0.16f, 1f));
                rowLight = Solid(new Color(0.18f, 0.20f, 0.23f, 1f));
                text = new GUIStyle(GUI.skin.label) { richText = false, fontSize = 13, wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
                small = new GUIStyle(text) { fontSize = 12, wordWrap = true };
                bold = new GUIStyle(text) { fontStyle = FontStyle.Bold };
                heading = new GUIStyle(text) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                good = new GUIStyle(small); good.normal.textColor = new Color(0.60f, 0.90f, 0.60f);
                bad = new GUIStyle(small); bad.normal.textColor = new Color(1f, 0.55f, 0.50f);
                changed = new GUIStyle(text); changed.normal.textColor = new Color(1f, 0.85f, 0.40f);
                rowA = new GUIStyle { padding = new RectOffset(8, 8, 3, 3) }; rowA.normal.background = rowDark;
                rowB = new GUIStyle(rowA); rowB.normal.background = rowLight;
                button = new GUIStyle(GUI.skin.button) { richText = false, fontSize = 12 };
                toolbar = new GUIStyle(GUI.skin.button) { richText = false, fontSize = 11, margin = new RectOffset(0, 0, 2, 2), padding = new RectOffset(2, 2, 3, 3) };
                field = new GUIStyle(GUI.skin.textField) { richText = false, fontSize = 13 };
            }

            float scale = Mathf.Max(1f, Screen.height / 1080f);
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float sw = Screen.width / scale, sh = Screen.height / scale;
            float w = Mathf.Min(1280f, sw - 30f), h = Mathf.Min(700f, sh - 60f);
            var box = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h);
            GUI.DrawTexture(box, backdrop);
            GUILayout.BeginArea(new Rect(box.x + 14f, box.y + 10f, box.width - 28f, box.height - 20f));

            GUILayout.Label($"N.O.T.C.H v{st.Version} — menu" + (string.IsNullOrEmpty(st.ServerName) ? "" : "  ·  " + st.ServerName), heading);
            int newTab = GUILayout.Toolbar(tab, Tabs, button, GUILayout.Height(26f));
            if (newTab != tab) { tab = newTab; confirmKey = null; statusHidden = true; }   // the last message belonged to the other tab
            GUILayout.Space(6f);

            if (tab == 0) DrawModsTop();                    // search row and column titles stay put above the list
            scroll[tab] = GUILayout.BeginScrollView(scroll[tab], GUILayout.ExpandHeight(true));
            switch (tab)
            {
                case 0: DrawMods(); break;
                case 1: DrawServer(); break;
                case 2: DrawAdmins(); break;
                default: DrawBans(); break;
            }
            GUILayout.EndScrollView();

            // Footer: what the server last said, Apply, Close.
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(awaitingApply ? "Saving…" : statusHidden ? "" : (st.Status ?? ""), st.StatusOk ? good : bad, GUILayout.ExpandWidth(true));
            int pending = PendingCount;
            GUI.enabled = pending > 0 && !awaitingApply;
            if (GUILayout.Button(pending > 0 ? $"Apply {pending} change{(pending == 1 ? "" : "s")}" : "Apply", button, GUILayout.Width(150f), GUILayout.Height(28f))) Apply();
            GUI.enabled = true;
            if (pending > 0 ? Confirm("close", "Discard and close", 150f) : GUILayout.Button("Close", button, GUILayout.Width(150f), GUILayout.Height(28f))) Close();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
            GUI.matrix = old;
        }

        // ---- Mods ---------------------------------------------------------------------------------------------

        /// <summary>The part of the Mods tab that does not scroll: the search row and the column titles.</summary>
        private static void DrawModsTop()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Find:", text, GUILayout.Width(36f));
            search = GUILayout.TextField(search, field, GUILayout.Width(220f));
            GUILayout.Space(12f);
            show = GUILayout.Toolbar(show, ShowLabels, toolbar, GUILayout.Width(380f));
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Policy: {Policy}", bold);
            GUILayout.EndHorizontal();

            // The titles sit in their own strip that follows the list's sideways scroll.
            GUILayout.BeginScrollView(new Vector2(scroll[0].x, 0f), false, false, GUIStyle.none, GUIStyle.none, GUILayout.Height(26f));
            GUILayout.BeginHorizontal(rowA);
            GUILayout.Label("Mod", bold, GUILayout.Width(250f));
            GUILayout.Label("Where", bold, GUILayout.Width(105f));
            GUILayout.Label("Author says", bold, GUILayout.Width(130f));
            GUILayout.Space(72f);                           // the Reset button's column
            GUILayout.Label("Required", bold, GUILayout.Width(128f));
            GUILayout.Label("If it doesn't match", bold, GUILayout.Width(184f));
            GUILayout.Label("Ban", bold, GUILayout.Width(40f));
            GUILayout.Label("Result now (for a player)", bold, GUILayout.Width(300f));
            GUILayout.Space(3000f);                         // room to follow the list however far right it scrolls
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        private static void DrawMods()
        {
            int n = 0;
            foreach (PanelMod m in st.Mods ?? new PanelMod[0])
            {
                string key = RuleOverrides.Key(m.Guid);
                bool edited = edits.TryGetValue(key, out Edit e);
                if (!edited) e = new Edit { Req = m.Required, Rule = m.Rule, Ban = m.Banned };

                if (search.Length > 0 && (m.Name ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 && m.Guid.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool known = m.Where <= PanelLogic.SkippedClientOnly;
                if (show == 1 && !known) continue;
                if (show == 2 && known) continue;
                if (show == 3 && !edited) continue;

                GUILayout.BeginHorizontal(n++ % 2 == 0 ? rowB : rowA);
                GUILayout.Label(string.IsNullOrEmpty(m.Version) ? m.Name : m.Name + " " + m.Version, edited ? changed : text, GUILayout.Width(250f));
                GUILayout.Label(PanelLogic.WhereText(m.Where), text, GUILayout.Width(105f));
                string author = string.Join(", ", new[] { m.ScopeTag, string.IsNullOrEmpty(m.CompatibleFrom) ? "" : "from " + m.CompatibleFrom }.Where(x => !string.IsNullOrEmpty(x)));
                GUILayout.Label(author.Length > 0 ? author : "–", text, GUILayout.Width(130f));

                Edit now = e;
                // Reset sits before the choices so it is always in view; only the result text can run off to the right.
                GUI.enabled = !(e.Req == 0 && e.Rule == 0 && !e.Ban);
                bool reset = Confirm("reset:" + key, "Reset", 60f);
                GUILayout.Space(8f);
                GUI.enabled = known && !e.Ban;              // a banned mod, or one the server has no copy of, has nothing to compare
                now.Req = (byte)GUILayout.Toolbar(e.Req, ReqLabels, toolbar, GUILayout.Width(120f));
                GUILayout.Space(8f);
                now.Rule = (byte)GUILayout.Toolbar(e.Rule, RuleLabels, toolbar, GUILayout.Width(176f));
                GUILayout.Space(8f);
                GUI.enabled = m.Where != PanelLogic.OnServer;   // banning a mod this server runs itself makes no sense
                now.Ban = GUILayout.Toggle(e.Ban, "", GUILayout.Width(40f));
                GUI.enabled = true;

                bool required = known && (now.Req == 1 || (now.Req == 0 && m.RequiredAuto));
                string result = PanelLogic.Result(Policy, ReportUnlisted, m.Where, required, now.Rule, now.Ban);
                if (known && !now.Ban && now.Req == 0 && m.RequiredAuto) result += $"   (required: {m.RequiredAutoWhy})";
                GUILayout.Label(result, text, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
                if (reset) now = new Edit();

                if (now.Req != e.Req || now.Rule != e.Rule || now.Ban != e.Ban)
                {
                    if (now.Req == m.Required && now.Rule == m.Rule && now.Ban == m.Banned) edits.Remove(key); else edits[key] = now;
                }
            }
            if (n == 0) GUILayout.Label("No mods to show.", text);
            GUILayout.Space(6f);
            GUILayout.Label("Required: Auto follows the mod author's label, and Blueprinter content is required.  " +
                            "If it doesn't match: Auto follows the policy; Kick refuses; Warn only tells the player; Off does not compare that mod.  " +
                            "Ban: nobody may join with that mod, in any version. Changed rows are shown in yellow; nothing is saved until Apply.", small);
        }

        // ---- Server -------------------------------------------------------------------------------------------

        private static void DrawServer()
        {
            GUILayout.Label("Policy — what happens when a player's mods don't match", bold);
            int policy = GUILayout.Toolbar((int)Policy, PolicyLabels, button, GUILayout.Width(520f), GUILayout.Height(26f));
            if (policy != (int)Policy) policyEdit = policy == st.Policy ? -1 : policy;
            GUILayout.Label(PolicyHelp[Mathf.Clamp((int)Policy, 0, 3)], policyEdit >= 0 ? changed : small);
            GUILayout.Space(14f);

            GUILayout.Label("Mods a player has that this server doesn't know", bold);
            int unlisted = GUILayout.Toolbar(ReportUnlisted ? 0 : 1, new[] { "Report", "Ignore" }, button, GUILayout.Width(260f), GUILayout.Height(26f));
            if (unlisted != (ReportUnlisted ? 0 : 1)) unlistedEdit = (unlisted == 0) == st.ReportUnlisted ? -1 : unlisted;
            GUILayout.Label(ReportUnlisted ? "Report: they count as a difference (a notice, or a refusal under RequireMatch)."
                                           : "Ignore: they are not mentioned. Banned mods are still refused.", unlistedEdit >= 0 ? changed : small);
            GUILayout.Space(14f);

            GUILayout.Label("How long to wait for a joining player's mod list (seconds)", bold);
            int seconds = timeoutEdit >= 0 ? timeoutEdit : st.ReplyTimeout;
            int picked = GUILayout.Toolbar(seconds - 1, new[] { "1", "2", "3", "4", "5", "6" }, button, GUILayout.Width(260f), GUILayout.Height(26f)) + 1;
            if (picked != seconds) timeoutEdit = picked == st.ReplyTimeout ? -1 : picked;
            GUILayout.Label("A player without NOTCH never answers, so they wait this long before the policy is applied. The game allows about 10 seconds for the whole login.", timeoutEdit >= 0 ? changed : small);
            GUILayout.Space(14f);

            GUILayout.Label("About this server", bold);
            int known = (st.Mods ?? new PanelMod[0]).Count(m => m.Where <= PanelLogic.SkippedClientOnly);
            GUILayout.Label($"NOTCH v{st.Version}.  Known mods: {known}.  Admin management: {(st.AdminActive ? "on" : "off")}.  Game ban file: {(st.BanFileOk ? "set" : "not set")}.", small);
            GUILayout.Label("Changes here are saved to the server's config file when you press Apply, and take effect from the next join.", small);
        }

        // ---- Admins -------------------------------------------------------------------------------------------

        private static void DrawAdmins()
        {
            if (!st.AdminActive)
            {
                GUILayout.Label("NOTCH's admin management is off on this server (the AdminManagement setting, or another admin mod is in charge).", small);
                return;
            }
            GUILayout.Label($"Admins ({(st.Admins ?? new PanelPerson[0]).Length}) — players on this list get the game's Admin level when they join.", bold);
            int n = 0;
            foreach (PanelPerson a in st.Admins ?? new PanelPerson[0])
            {
                GUILayout.BeginHorizontal(n++ % 2 == 0 ? rowB : rowA);
                GUILayout.Label(a.Id.ToString(), text, GUILayout.Width(170f));
                GUILayout.Label(string.IsNullOrEmpty(a.Note) ? "–" : a.Note, text, GUILayout.Width(360f));
                GUILayout.Label(a.Online ? "online" : "", good, GUILayout.Width(80f));
                GUILayout.FlexibleSpace();
                if (Confirm("adminremove:" + a.Id, "Remove", 90f)) Send(PanelLogic.ActAdminRemove, a.Id.ToString(), "");
                GUILayout.EndHorizontal();
            }
            if (n == 0) GUILayout.Label("Nobody yet.", text);

            var adminIds = new HashSet<ulong>((st.Admins ?? new PanelPerson[0]).Select(a => a.Id));
            GUILayout.Space(12f);
            GUILayout.Label("Online players", bold);
            n = 0;
            foreach (PanelPerson p in (st.Players ?? new PanelPerson[0]).Where(p => !adminIds.Contains(p.Id)))
            {
                GUILayout.BeginHorizontal(n++ % 2 == 0 ? rowB : rowA);
                GUILayout.Label(p.Note, text, GUILayout.Width(360f));
                GUILayout.Label(p.Id.ToString(), text, GUILayout.Width(170f));
                GUILayout.Label(p.Admin ? "already Admin (not from this list)" : "", small, GUILayout.Width(240f));
                GUILayout.FlexibleSpace();
                if (Confirm("adminadd:" + p.Id, "Add as admin", 110f)) Send(PanelLogic.ActAdminAdd, p.Id.ToString(), p.Note);
                GUILayout.EndHorizontal();
            }
            if (n == 0) GUILayout.Label("No other players online.", text);

            GUILayout.Space(12f);
            GUILayout.Label("Add by Steam ID", bold);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Steam ID:", text, GUILayout.Width(64f));
            adminId = GUILayout.TextField(adminId, 20, field, GUILayout.Width(190f));
            GUILayout.Label("Note:", text, GUILayout.Width(40f));
            adminNote = GUILayout.TextField(adminNote, 60, field, GUILayout.Width(280f));
            bool valid = AdminList.TryParseSteamId(adminId, out ulong id);
            GUI.enabled = valid;
            if (GUILayout.Button("Add", button, GUILayout.Width(90f))) { Send(PanelLogic.ActAdminAdd, id.ToString(), adminNote); adminId = ""; adminNote = ""; }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (adminId.Trim().Length > 0 && !valid) GUILayout.Label(BadId, bad);
        }

        // ---- Player bans --------------------------------------------------------------------------------------

        private static void DrawBans()
        {
            GUILayout.Label("These are the game's own bans: they are kept in the game's ban file and work without NOTCH.", small);
            if (!st.BanFileOk)
                GUILayout.Label("This server has no ban file, so bans can't be saved. Set BanListPaths in DedicatedServerConfig.json and restart. Kick still works.", bad);
            GUILayout.Space(4f);
            GUILayout.Label($"Banned players ({(st.Bans ?? new PanelPerson[0]).Length})", bold);
            int n = 0;
            foreach (PanelPerson b in st.Bans ?? new PanelPerson[0])
            {
                GUILayout.BeginHorizontal(n++ % 2 == 0 ? rowB : rowA);
                GUILayout.Label(b.Id.ToString(), text, GUILayout.Width(170f));
                GUILayout.Label(string.IsNullOrEmpty(b.Note) ? "–" : b.Note, text, GUILayout.Width(520f));
                GUILayout.FlexibleSpace();
                GUI.enabled = st.BanFileOk;
                if (Confirm("unban:" + b.Id, "Unban", 90f)) Send(PanelLogic.ActPlayerUnban, b.Id.ToString(), "");
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            if (n == 0) GUILayout.Label("Nobody is banned.", text);

            GUILayout.Space(12f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Reason (used by Ban, below):", text, GUILayout.Width(190f));
            banReason = GUILayout.TextField(banReason, 120, field, GUILayout.Width(480f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Online players", bold);
            n = 0;
            foreach (PanelPerson p in st.Players ?? new PanelPerson[0])
            {
                GUILayout.BeginHorizontal(n++ % 2 == 0 ? rowB : rowA);
                GUILayout.Label(p.Note, text, GUILayout.Width(360f));
                GUILayout.Label(p.Id.ToString(), text, GUILayout.Width(170f));
                GUILayout.Label(p.Admin ? "admin" : "", small, GUILayout.Width(80f));
                GUILayout.FlexibleSpace();
                if (Confirm("kick:" + p.Id, "Kick", 80f)) Send(PanelLogic.ActPlayerKick, p.Id.ToString(), "");
                GUI.enabled = st.BanFileOk;
                if (Confirm("ban:" + p.Id, "Ban", 80f)) Send(PanelLogic.ActPlayerBan, p.Id.ToString(), banReason);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            if (n == 0) GUILayout.Label("No players online.", text);

            GUILayout.Space(12f);
            GUILayout.Label("Ban by Steam ID", bold);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Steam ID:", text, GUILayout.Width(64f));
            banId = GUILayout.TextField(banId, 20, field, GUILayout.Width(190f));
            bool valid = AdminList.TryParseSteamId(banId, out ulong id);
            GUI.enabled = valid && st.BanFileOk;
            if (GUILayout.Button("Ban", button, GUILayout.Width(90f))) { Send(PanelLogic.ActPlayerBan, id.ToString(), banReason); banId = ""; }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (banId.Trim().Length > 0 && !valid) GUILayout.Label(BadId, bad);
        }

        // ---- Apply --------------------------------------------------------------------------------------------

        private static void Apply()
        {
            var actions = new List<PanelAction>();
            foreach (PanelMod m in st.Mods ?? new PanelMod[0])
            {
                if (!edits.TryGetValue(RuleOverrides.Key(m.Guid), out Edit e)) continue;
                if (e.Ban != m.Banned) actions.Add(new PanelAction { Kind = PanelLogic.ActModBan, Key = m.Guid, Value = e.Ban ? "1" : "0" });
                if (e.Req != m.Required) actions.Add(new PanelAction { Kind = PanelLogic.ActRequired, Key = m.Guid, Value = PanelLogic.RequiredWords[e.Req] });
                if (e.Rule != m.Rule) actions.Add(new PanelAction { Kind = PanelLogic.ActRule, Key = m.Guid, Value = PanelLogic.RuleWords[e.Rule] });
            }
            if (policyEdit >= 0) actions.Add(new PanelAction { Kind = PanelLogic.ActPolicy, Key = "", Value = ((JoinPolicy)policyEdit).ToString() });
            if (unlistedEdit >= 0) actions.Add(new PanelAction { Kind = PanelLogic.ActUnlisted, Key = "", Value = unlistedEdit == 0 ? "Report" : "Ignore" });
            if (timeoutEdit >= 0) actions.Add(new PanelAction { Kind = PanelLogic.ActTimeout, Key = "", Value = timeoutEdit.ToString() });
            if (actions.Count == 0 || server == null) return;
            awaitingApply = true;
            server.Send(new NotchPanelActionV1 { Protocol = Wire.ProtocolVersion, Actions = actions.ToArray() });
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply();
            return t;
        }
    }
}
