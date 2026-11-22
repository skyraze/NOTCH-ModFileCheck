using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NOTCH.Consent
{
    /// <summary>The Network Access Notice on screen: the text, a changes view, and Accept / Decline at the end. Unity IMGUI.</summary>
    internal static class ConsentWindow
    {
        private static bool open, changesOnly, closable, dontAsk, laterThisSession, reminded, matchOnly;
        private static string appliesTo = "";
        private static Action<ConsentAnswer> onAnswer;
        private static Action<bool> onLater;                // null = no "Later" button; the flag is "don't ask again"
        private static List<DiffLine> full, changes;        // changes = null when there is nothing to compare with
        private static Vector2 scroll;
        private static GameObject menuSelection;            // the menu button the game had selected, given back on closing
        private static Texture2D backdrop, panel, lineTex;
        private static GUIStyle body, added, removed, centered, heading, small, button, panelStyle, tick, warning;

        internal static bool IsOpen => open;

        /// <summary>Opens the notice. The answer goes to onAnswer; "Later" (when offered) goes to later; closing calls nothing.</summary>
        internal static void Show(string whoItAppliesTo, string previousVersion, bool canClose, Action<ConsentAnswer> answer, Action<bool> later = null, bool inMatchOnly = false)
        {
            appliesTo = whoItAppliesTo ?? ""; closable = canClose; onAnswer = answer; onLater = later; dontAsk = false; matchOnly = inMatchOnly;
            full = Agreement.Diff(Agreement.CurrentText, Agreement.CurrentText);
            changes = Agreement.CanCompare(previousVersion)
                ? Agreement.Diff(Agreement.TextOf(previousVersion), Agreement.CurrentText).Where(l => l.Kind != DiffKind.Same).ToList()
                : null;
            changesOnly = false; scroll = Vector2.zero; open = true;
        }

        private static void Close()
        {
            open = false; onAnswer = null; onLater = null;
            Popup.GuardOff();
            EventSystem ui = EventSystem.current;
            if (ui != null && menuSelection != null && ui.currentSelectedGameObject == null) ui.SetSelectedGameObject(menuSelection);
            menuSelection = null;
        }

        private static void Give(ConsentAnswer answer)
        {
            Action<ConsentAnswer> tell = onAnswer;
            Close();
            try { tell?.Invoke(answer); }
            catch (Exception e) { Plugin.Log.LogWarning("Could not record the answer to the Network Access Notice: " + e.Message); }
        }

        /// <summary>Called from the plugin's OnGUI every frame.</summary>
        internal static void Draw()
        {
            if (!open)
            {
                // A player's own game asks in the main menu while the notice is unanswered, once per start, unless told not to.
                if (Plugin.IsServerProcess || ConsentStore.Answer != ConsentAnswer.NotAnswered) return;
                // Wait until the menu has finished loading (other mods load their content before that).
                if (GameManager.gameState != GameState.Menu || MainMenu.State != MainMenu.LoadingState.Loaded) return;
                if (!reminded) { reminded = true; ConsentReminder.ShowAfterStartup(); }   // the console block, once other mods have finished their start-up lines
                if (laterThisSession || !ConsentStore.AskInGame) return;
                Show("Your answer applies to this copy of NOTCH, on this computer.", ConsentStore.Previous, false, ConsentStore.Set,
                     stop => { laterThisSession = true; if (stop) ConsentStore.StopAsking(); });
            }
            if (matchOnly && GameplayUI.i == null) { Close(); return; }     // a server's notice: gone when the match is left
            Popup.GuardOn();
            // The main menu keeps one of its buttons selected, and a key or controller press would activate it behind this window.
            EventSystem ui = EventSystem.current;
            if (ui != null && ui.currentSelectedGameObject != null) { menuSelection = ui.currentSelectedGameObject; ui.SetSelectedGameObject(null); }

            Event ev = Event.current;
            if (closable && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape) { ev.Use(); Close(); return; }

            if (backdrop == null)
            {
                backdrop = Solid(new Color(0.08f, 0.09f, 0.10f, 0.98f));
                panel = Solid(new Color(0.13f, 0.14f, 0.16f, 1f));
                lineTex = Solid(new Color(0.92f, 0.92f, 0.92f, 1f));
                body = new GUIStyle(GUI.skin.label) { wordWrap = false, fontSize = 14, richText = false, margin = new RectOffset(0, 0, 0, 0), padding = new RectOffset(8, 8, 0, 0) };
                added = new GUIStyle(body); added.normal.textColor = new Color(0.45f, 0.95f, 0.50f);
                removed = new GUIStyle(body); removed.normal.textColor = new Color(1f, 0.50f, 0.45f);
                small = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 13, richText = false };
                centered = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
                heading = new GUIStyle(centered) { fontSize = 19, fontStyle = FontStyle.Bold };
                button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
                panelStyle = new GUIStyle(); panelStyle.normal.background = panel;
                tick = new GUIStyle(GUI.skin.toggle) { fontSize = 14 };
                warning = new GUIStyle(small) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(12, 12, 4, 4) };
                warning.normal.textColor = new Color(1f, 0.82f, 0.25f);
            }

            // Sized for a 1080-pixel-high screen and scaled from there, like the join notice.
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float sw = Screen.width / scale, sh = Screen.height / scale;
            float w = Mathf.Min(780f, sw - 40f), h = Mathf.Min(700f, sh - 80f);
            var box = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h);

            GUI.DrawTexture(box, backdrop);
            GUILayout.BeginArea(new Rect(box.x + 18f, box.y + 14f, box.width - 36f, box.height - 28f));

            // Fixed top: title, who the answer is for, and the full / changes switch.
            var title = new GUIContent(Agreement.Title);
            GUILayout.Label(title, heading);
            Rect r = GUILayoutUtility.GetLastRect();
            float tw = heading.CalcSize(title).x;
            GUI.DrawTexture(new Rect(r.x + (r.width - tw) / 2f, r.yMax - 2f, tw, 1.5f), lineTex);
            GUILayout.Space(4f);
            GUILayout.Label(appliesTo, centered);
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Agreement version " + Agreement.Current, small);
            GUILayout.FlexibleSpace();
            if (changes == null) GUILayout.Label("First version", small);
            else if (GUILayout.Button(changesOnly ? "Show the full notice" : "Show only what changed", button, GUILayout.Width(220f))) { changesOnly = !changesOnly; scroll = Vector2.zero; }
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            // The text scrolls; the answer buttons are at its end.
            GUILayout.BeginVertical(panelStyle, GUILayout.ExpandHeight(true));
            scroll = GUILayout.BeginScrollView(scroll, false, true);
            GUILayout.Space(6f);
            List<DiffLine> lines = changesOnly ? changes : full;
            if (changesOnly && lines.Count == 0) GUILayout.Label("Nothing in the text changed.", body);
            foreach (DiffLine line in lines)
            {
                if (!changesOnly) GUILayout.Label(line.Text.Length == 0 ? " " : line.Text, body);
                else GUILayout.Label(Agreement.Mark(line), line.Kind == DiffKind.Added ? added : removed);
            }
            GUILayout.Space(14f);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Accept", button, GUILayout.Width(170f), GUILayout.Height(36f))) { Finish(ConsentAnswer.Accepted, old); return; }
            GUILayout.Space(24f);
            if (GUILayout.Button("Decline", button, GUILayout.Width(170f), GUILayout.Height(36f))) { Finish(ConsentAnswer.Declined, old); return; }
            if (onLater != null)
            {
                GUILayout.Space(24f);
                if (GUILayout.Button("Later", button, GUILayout.Width(170f), GUILayout.Height(36f))) { FinishLater(old); return; }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (onLater != null)
            {
                GUILayout.Space(8f);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                dontAsk = GUILayout.Toggle(dontAsk, " Don't ask again (applies to \"Later\")", tick);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                if (dontAsk)
                    GUILayout.Label("WARNING: with this ticked, \"Later\" switches this window off for good. The notice stays unanswered and NOTCH contacts nothing. " +
                                    "A reminder is printed in the BepInEx console and log at every start. To answer after that, you must edit BepInEx/config/" + Plugin.Guid + ".cfg yourself.", warning);
            }
            GUILayout.Space(10f);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // Fixed bottom: where the copy is, and (only when allowed) a way out without answering.
            GUILayout.Space(6f);
            GUILayout.Label("Scroll to the end to answer. A copy of this notice: " + ConsentStore.NoticeShown, centered);
            if (closable)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close without answering", button, GUILayout.Width(240f))) { GUILayout.EndHorizontal(); GUILayout.EndArea(); GUI.matrix = old; Close(); return; }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
            GUI.matrix = old;
        }

        /// <summary>Closes the layout groups that are open at the answer buttons, then records the answer.</summary>
        private static void Finish(ConsentAnswer answer, Matrix4x4 old)
        {
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndArea();
            GUI.matrix = old;
            Give(answer);
        }

        /// <summary>"Later": the same clean-up, then tells the caller whether "Don't ask again" was ticked.</summary>
        private static void FinishLater(Matrix4x4 old)
        {
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndArea();
            GUI.matrix = old;
            Action<bool> tell = onLater;
            bool stop = dontAsk;
            Close();
            try { tell?.Invoke(stop); }
            catch (Exception e) { Plugin.Log.LogWarning("Could not record \"Later\" for the Network Access Notice: " + e.Message); }
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply();
            return t;
        }
    }
}
