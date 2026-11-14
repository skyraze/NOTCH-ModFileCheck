using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Rewired;
using UnityEngine;
using UnityEngine.UI;

namespace NOTCH
{
    /// <summary>
    /// The warning a player sees after joining a server where some of their mods don't match.
    /// Drawn with Unity's built-in immediate-mode UI (the same kind Configuration Manager uses): no art assets,
    /// nothing taken from the game's own UI. It appears once the match screen exists, stays until Close is
    /// clicked, and goes away by itself if the player leaves the server. Nothing to tell = no popup.
    /// </summary>
    internal static class Popup
    {
        private static string serverName = "";
        private static string header = "";
        private static List<string> lines;          // null = nothing to show
        private static Vector2 scroll;
        private static bool seenInMatch;
        private static Texture2D backdrop, rowDark, rowLight, lineTex;
        private static GUIStyle text, centered, heading, rowA, rowB;
        private static GameObject blocker;
        private static bool inputHeld, pauseKeyWas;
        private static readonly List<ControllerMap> heldMaps = new List<ControllerMap>();
        private const CursorFlags NoticeCursor = (CursorFlags)(1 << 30);   // our own bit; the game uses the low ones

        internal static void Show(string server, string headerLine, IEnumerable<string> problemLines)
        {
            serverName = server ?? ""; header = headerLine; lines = problemLines.ToList();
            scroll = Vector2.zero; seenInMatch = false;
        }

        internal static void Clear()
        {
            lines = null;
            if (blocker != null) Object.Destroy(blocker);
            blocker = null;
            ReleaseInput();
        }

        /// <summary>
        /// While the notice is open the game must not react to keys or the mouse wheel (the map zooming, M closing
        /// the map and taking the mouse pointer with it). This does what the game's own chat box does while you type:
        /// switch off the keyboard and mouse control maps, keep the mouse pointer visible, and block the pause key.
        /// </summary>
        private static void HoldInput()
        {
            // Runs every frame while something of ours is open. The game's chat box switches every control map
            // back ON a moment after it closes (and a menu command is typed in chat), so one pass is not enough:
            // each frame, anything that is enabled is switched off and remembered.
            bool first = !inputHeld;
            inputHeld = true;
            try
            {
                Player player = ReInput.players.GetPlayer(0);
                foreach (ControllerType type in new[] { ControllerType.Keyboard, ControllerType.Mouse })
                    foreach (ControllerMap map in player.controllers.maps.GetAllMaps(type))
                        if (map.enabled) { map.enabled = false; if (!heldMaps.Contains(map)) heldMaps.Add(map); }   // remember only what WE switched off
                if (first) pauseKeyWas = false;
                pauseKeyWas |= GameplayUI.AllowPauseKeybind;    // if it was (or becomes) allowed, give it back afterwards
                GameplayUI.AllowPauseKeybind = false;
                if (first) CursorManager.SetFlag(NoticeCursor, true);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Could not pause game input for the notice: " + e.Message); }
        }

        private static void ReleaseInput()
        {
            if (!inputHeld) return;
            inputHeld = false;
            try { CursorManager.SetFlag(NoticeCursor, false); } catch { }
            ReleaseSoon().Forget();
        }

        // A short wait (the chat box does the same) so the click or key that closed the notice is not also seen by the game.
        private static async UniTaskVoid ReleaseSoon()
        {
            await UniTask.Delay(100, ignoreTimeScale: true);
            if (inputHeld) return;                          // a new notice opened in the meantime
            foreach (ControllerMap map in heldMaps) map.enabled = true;
            heldMaps.Clear();
            GameplayUI.AllowPauseKeybind = pauseKeyWas;
        }

        /// <summary>
        /// The popup is drawn on top of the game's menus, but on its own it does not stop a click from also
        /// reaching the game button underneath. While the popup is open, an invisible full-screen sheet sits
        /// above all of the game's menus and swallows those clicks; it is removed when the popup closes.
        /// It also dims the screen slightly, so it is clear the popup has to be closed first.
        /// </summary>
        private static void EnsureBlocker()
        {
            if (blocker != null) return;
            blocker = new GameObject("NOTCH notice click blocker");
            var canvas = blocker.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;                    // above the game's own menus
            blocker.AddComponent<GraphicRaycaster>();
            var sheet = new GameObject("sheet");
            sheet.transform.SetParent(blocker.transform, false);
            var image = sheet.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.35f);
            image.raycastTarget = true;
            var rt = image.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        /// <summary>For the NOTCH menu: the same click blocker and input hold the notice uses.</summary>
        internal static void GuardOn() { EnsureBlocker(); HoldInput(); }

        /// <summary>Releases them again, unless the notice itself is still open.</summary>
        internal static void GuardOff()
        {
            if (lines != null) return;
            if (blocker != null) Object.Destroy(blocker);
            blocker = null;
            ReleaseInput();
        }

        /// <summary>Called from the plugin's OnGUI every frame.</summary>
        internal static void Draw()
        {
            if (lines == null) return;
            bool inMatch = GameplayUI.i != null;            // the match screen (side selection, map, HUD) exists
            if (!inMatch) { if (seenInMatch) Clear(); return; }   // still loading: wait. Was in a match and left: drop it.
            seenInMatch = true;
            EnsureBlocker();
            HoldInput();

            // Enter or Esc closes the notice too, so it can always be dismissed without the mouse.
            Event ev = Event.current;
            if (ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.keyCode == KeyCode.Escape))
            {
                ev.Use(); Clear(); return;
            }

            if (backdrop == null)
            {
                backdrop = Solid(new Color(0.08f, 0.09f, 0.10f, 0.97f));
                rowDark = Solid(new Color(0.13f, 0.14f, 0.16f, 1f));
                rowLight = Solid(new Color(0.21f, 0.23f, 0.26f, 1f));
                lineTex = Solid(new Color(0.92f, 0.92f, 0.92f, 1f));
                text = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 15, richText = false };
                centered = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter };
                heading = new GUIStyle(centered) { fontSize = 19, fontStyle = FontStyle.Bold };
                // One issue = one row. Rows alternate dark / light; a wrapped issue keeps one colour for all its lines.
                rowA = new GUIStyle(text) { padding = new RectOffset(10, 10, 7, 7), margin = new RectOffset(0, 0, 0, 0) };
                rowA.normal.background = rowDark;
                rowB = new GUIStyle(rowA);
                rowB.normal.background = rowLight;
            }

            // Sized for a 1080-pixel-high screen and scaled from there, so it is readable at 1440p and 4K.
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float sw = Screen.width / scale, sh = Screen.height / scale;
            // Grows with the number of issues up to a fixed height; beyond that the issue list scrolls.
            float w = Mathf.Min(760f, sw - 40f), h = Mathf.Min(Mathf.Min(245f + 40f * lines.Count, 590f), sh - 120f);
            var box = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h);

            GUI.DrawTexture(box, backdrop);
            GUILayout.BeginArea(new Rect(box.x + 18f, box.y + 14f, box.width - 36f, box.height - 28f));

            // Fixed top: title, server, count.
            Underlined("N.O.T.C.H v" + Plugin.Version);
            if (serverName.Length > 0) GUILayout.Label("Server: " + serverName, centered);
            GUILayout.Space(6f);
            GUILayout.Label(header, text);

            // Only the issues scroll.
            scroll = GUILayout.BeginScrollView(scroll, false, false, GUILayout.ExpandHeight(true));
            for (int i = 0; i < lines.Count; i++) GUILayout.Label(lines[i], i % 2 == 0 ? rowA : rowB);
            GUILayout.EndScrollView();

            // Fixed bottom: where the details are, the notice line and the Close button.
            GUILayout.Space(6f);
            GUILayout.Label("Full details: " + Plugin.ReportPathShown, centered);
            GUILayout.Space(4f);
            Underlined("You have joined — this is only a notice.");
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(140f), GUILayout.Height(32f))) Clear();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            GUI.matrix = old;
        }

        /// <summary>A centred, bold line with a line drawn under the words (this UI kit has no underline of its own).</summary>
        private static void Underlined(string words)
        {
            var content = new GUIContent(words);
            GUILayout.Label(content, heading);
            Rect r = GUILayoutUtility.GetLastRect();
            float tw = heading.CalcSize(content).x;
            GUI.DrawTexture(new Rect(r.x + (r.width - tw) / 2f, r.yMax - 2f, tw, 1.5f), lineTex);
            GUILayout.Space(3f);
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply();
            return t;
        }
    }
}
