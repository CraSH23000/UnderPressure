using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// Pause menu: a framed panel with Resume (default), Controls, Settings, Restart and Quit.
    /// Navigate with stick / D-pad / WASD / arrows or the mouse; confirm with A, Start, Space, Enter or click;
    /// back with B, Esc or the Back option. Esc/Start opens it, and Esc on the main page resumes.
    /// Settings rows have real arrow buttons and, for volumes/shake, a bar you can click or drag.
    /// The Controls page is one scrolling list (keyboard, then controller) so the columns line up.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        enum Page { Closed, Main, Controls, Settings }

        class Item
        {
            public RectTransform rt;
            public Image bg, marker;
            public Text label, value;
            public RectTransform left, right, bar;   // setting widgets (null for plain items)
            public Image fill;
            public Text leftText, rightText;
            public Action submit;
            public Action<int> adjust;
            public Func<string> getValue;
            public Func<float> get01;
            public Action<float> set01;
        }

        Page page = Page.Closed;
        RectTransform overlay, box, content;
        Text title;
        readonly List<Item> items = new List<Item>();
        int sel;
        InputAction nav, submit, cancel, open;
        int navDirY, navDirX;
        float navHoldY, navHoldX;
        Vector2 lastMouse;
        Item dragging;

        // controls page scrolling
        RectTransform scrollView, scrollInner, scrollThumb, scrollTrack;
        float scrollY, scrollMax;
        bool draggingThumb;
        float thumbGrabOffset;

        static readonly Color Panel = new Color(0.15f, 0.16f, 0.19f, 0.92f);
        static readonly Color Accent = new Color(1f, 0.55f, 0.12f);
        static readonly Color ValueColor = new Color(0.75f, 0.9f, 1f);

        public bool IsOpen => page != Page.Closed;

        public void Build(RectTransform root)
        {
            overlay = R("PauseOverlay", root, Vector2.zero, Vector2.one);
            overlay.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            box = R("Box", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            box.gameObject.AddComponent<Image>().color = Panel;
            var bar = R("Accent", box, new Vector2(0, 1), new Vector2(1, 1));
            bar.sizeDelta = new Vector2(0, 8);
            bar.anchoredPosition = new Vector2(0, -4);
            bar.gameObject.AddComponent<Image>().color = Accent;
            var t = R("Title", box, new Vector2(0, 1), new Vector2(1, 1));
            t.sizeDelta = new Vector2(0, 110);
            t.anchoredPosition = new Vector2(0, -62);
            title = Popup.MakeText(t.gameObject, "PAUSED", 72, Color.white);
            content = R("Content", box, Vector2.zero, Vector2.one);
            content.offsetMax = new Vector2(0, -120);
            content.offsetMin = new Vector2(0, 30);
            overlay.gameObject.SetActive(false);

            nav = new InputAction("MenuNav", InputActionType.Value);
            nav.AddBinding("<Gamepad>/leftStick");
            nav.AddBinding("<Gamepad>/dpad");
            nav.AddCompositeBinding("2DVector").With("up", "<Keyboard>/w").With("down", "<Keyboard>/s").With("left", "<Keyboard>/a").With("right", "<Keyboard>/d");
            nav.AddCompositeBinding("2DVector").With("up", "<Keyboard>/upArrow").With("down", "<Keyboard>/downArrow").With("left", "<Keyboard>/leftArrow").With("right", "<Keyboard>/rightArrow");
            submit = new InputAction("MenuSubmit", InputActionType.Button);
            foreach (var b in new[] { "<Gamepad>/buttonSouth", "<Gamepad>/start", "<Keyboard>/space", "<Keyboard>/enter", "<Keyboard>/numpadEnter" }) submit.AddBinding(b);
            cancel = new InputAction("MenuCancel", InputActionType.Button);
            foreach (var b in new[] { "<Gamepad>/buttonEast", "<Keyboard>/escape", "<Keyboard>/backspace" }) cancel.AddBinding(b);
            open = new InputAction("MenuOpen", InputActionType.Button);
            open.AddBinding("<Keyboard>/escape");
            open.AddBinding("<Gamepad>/start");
            nav.Enable(); submit.Enable(); cancel.Enable(); open.Enable();
        }

        void OnDestroy()
        {
            nav?.Dispose(); submit?.Dispose(); cancel?.Dispose(); open?.Dispose();
            if (IsOpen && JuiceHost.Instance != null) JuiceHost.Instance.Paused = false;
        }

        static RectTransform R(string name, Transform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = aMin; r.anchorMax = aMax;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            return r;
        }

        // =================================================================== open / close

        void Open()
        {
            if (JuiceHost.Instance != null) JuiceHost.Instance.Paused = true;
            overlay.gameObject.SetActive(true);
            overlay.SetAsLastSibling();
            Show(Page.Main);
            Sfx.Play(Sfx.I != null ? Sfx.I.tick : null, 0.6f, 0.7f, 0f);
        }

        void Close()
        {
            page = Page.Closed;
            overlay.gameObject.SetActive(false);
            if (JuiceHost.Instance != null) JuiceHost.Instance.Paused = false;
            Sfx.Play(Sfx.I != null ? Sfx.I.tick : null, 0.6f, 1.1f, 0f);
        }

        void Show(Page p)
        {
            page = p;
            foreach (Transform c in content) Destroy(c.gameObject);
            items.Clear();
            sel = 0;
            dragging = null;
            scrollView = null;
            scrollTrack = null;
            draggingThumb = false;
            switch (p)
            {
                case Page.Main: BuildMain(); break;
                case Page.Controls: BuildControls(); break;
                case Page.Settings: BuildSettings(); break;
            }
            Refresh();
        }

        // =================================================================== pages

        void BuildMain()
        {
            title.text = "PAUSED";
            box.sizeDelta = new Vector2(760, 640);
            AddItem("Resume", Close);
            AddItem("Controls", () => Show(Page.Controls));
            AddItem("Settings", () => Show(Page.Settings));
            AddItem("Restart Chamber", () => { Close(); Game.Bootstrap.Restart(); });
            AddItem("Quit Game", Quit);
        }

        void BuildSettings()
        {
            title.text = "SETTINGS";
            box.sizeDelta = new Vector2(1060, 920);
            var res = GameSettings.Resolutions();
            AddSetting("Display Mode", () => GameSettings.DisplayModes[GameSettings.DisplayMode],
                d => { GameSettings.DisplayMode = Wrap(GameSettings.DisplayMode + d, GameSettings.DisplayModes.Length); GameSettings.ApplyDisplay(); });
            AddSetting("Resolution", () => { var r = res[Mathf.Clamp(GameSettings.ResolutionIndex, 0, res.Count - 1)]; return $"{r.x} x {r.y}"; },
                d => { GameSettings.ResolutionIndex = Wrap(GameSettings.ResolutionIndex + d, res.Count); GameSettings.ApplyDisplay(); });
            AddSetting("V-Sync", () => GameSettings.VSync ? "On" : "Off", d => GameSettings.VSync = !GameSettings.VSync);
            AddSetting("Anti-Aliasing", () => GameSettings.AntiAliasNames[GameSettings.AntiAlias],
                d => GameSettings.AntiAlias = Wrap(GameSettings.AntiAlias + d, GameSettings.AntiAliasNames.Length));
            AddSetting("Render Scale", () =>
            {
                string v = $"{GameSettings.RenderScales[GameSettings.RenderScaleIndex] * 100f:0}%";
#if UNITY_EDITOR
                v += " (builds)";
#endif
                return v;
            }, d => GameSettings.RenderScaleIndex = Mathf.Clamp(GameSettings.RenderScaleIndex + d, 0, GameSettings.RenderScales.Length - 1));
            AddSlider("Master Volume", () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v, 1f, 0.1f);
            AddSlider("Music Volume", () => GameSettings.MusicVolume, v => GameSettings.MusicVolume = v, 1f, 0.1f);
            AddSlider("Screen Shake", () => GameSettings.ScreenShake, v => GameSettings.ScreenShake = v, 1.5f, 0.25f);
            AddItem("Back", () => Show(Page.Main));
        }

        /// <summary>One scrolling list: keyboard first, then controller, all in the same aligned columns.</summary>
        void BuildControls()
        {
            title.text = "CONTROLS";
            box.sizeDelta = new Vector2(1240, 880);

            scrollView = R("Viewport", content, new Vector2(0.04f, 0.14f), new Vector2(0.94f, 1f));
            scrollView.gameObject.AddComponent<RectMask2D>();
            scrollInner = R("Inner", scrollView, new Vector2(0, 1), new Vector2(1, 1));
            scrollInner.pivot = new Vector2(0.5f, 1f);

            float[] cols = { 0.44f, 0.28f, 0.28f };
            float y = 0f;
            y = Section(y, "KEYBOARD", new[] { "Action", "WASD side", "Arrows side" }, new[]
            {
                new[] { "Move", "W A S D", "Arrow keys" },
                new[] { "Jump · vault · climb", "Space", "Right Ctrl" },
                new[] { "Use · grab · place · steal", "E", "Enter" },
                new[] { "Unscrew / swap", "hold E", "hold Enter" },
                new[] { "Rotate counter-clockwise", "F", ";" },
                new[] { "Rotate clockwise", "G", "'" },
                new[] { "Sprint", "Left Shift", "Right Shift" },
                new[] { "Join the game", "Space or E", "Enter" },
                new[] { "Menu", "Esc", "Esc" },
            }, cols);
            y -= 40f;
            y = Section(y, "CONTROLLER", new[] { "Action", "Button", "" }, new[]
            {
                new[] { "Move", "Left stick / D-pad", "" },
                new[] { "Jump · vault · climb", "A", "" },
                new[] { "Use · grab · place · steal", "X", "" },
                new[] { "Unscrew / swap", "hold X", "" },
                new[] { "Rotate counter-clockwise", "Y", "" },
                new[] { "Rotate clockwise", "B", "" },
                new[] { "Sprint", "Left Trigger", "" },
                new[] { "Join the game", "A", "" },
                new[] { "Menu", "Start", "" },
            }, cols);
            scrollInner.sizeDelta = new Vector2(0, -y + 20f);
            scrollY = 0f;

            // scrollbar
            var track = R("ScrollTrack", content, new Vector2(0.952f, 0.16f), new Vector2(0.972f, 0.98f));
            scrollTrack = track;
            track.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.08f);
            scrollThumb = R("Thumb", track, new Vector2(0, 1), new Vector2(1, 1));
            scrollThumb.pivot = new Vector2(0.5f, 1f);
            scrollThumb.gameObject.AddComponent<Image>().color = new Color(Accent.r, Accent.g, Accent.b, 0.8f);

            var hint = R("Hint", content, new Vector2(0.04f, 0.115f), new Vector2(0.96f, 0.15f));
            Popup.MakeText(hint.gameObject, "scroll: mouse wheel · stick · D-pad · arrows", 20, new Color(1, 1, 1, 0.45f), TextAnchor.MiddleRight);

            var backArea = R("BackArea", content, new Vector2(0.38f, 0f), new Vector2(0.62f, 0.1f));
            AddItem("Back", () => Show(Page.Main), backArea, true);
        }

        float Section(float y, string heading, string[] header, string[][] rows, float[] widths)
        {
            const float rowH = 56f;
            var h = Row(y, 64f);
            Popup.MakeText(h.gameObject, heading, 40, Accent, TextAnchor.MiddleLeft);
            y -= 64f;
            for (int r = -1; r < rows.Length; r++)
            {
                var cells = r < 0 ? header : rows[r];
                var row = Row(y, rowH);
                if (r >= 0 && (r & 1) == 0) row.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.05f);
                float x = 0f;
                for (int c = 0; c < cells.Length; c++)
                {
                    var cell = R("Cell", row, new Vector2(x, 0), new Vector2(x + widths[c], 1));
                    cell.offsetMin = new Vector2(14, 0);
                    bool head = r < 0;
                    var col = head ? new Color(1, 1, 1, 0.5f) : c == 0 ? new Color(1, 1, 1, 0.85f) : Color.white;
                    var t = Popup.MakeText(cell.gameObject, cells[c], head ? 24 : 30, col, TextAnchor.MiddleLeft);
                    if (c > 0 && !head) t.fontStyle = FontStyle.Bold;
                    x += widths[c];
                }
                y -= rowH;
            }
            return y;
        }

        RectTransform Row(float y, float h)
        {
            var row = R("Row", scrollInner, new Vector2(0, 1), new Vector2(1, 1));
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0, h);
            row.anchoredPosition = new Vector2(0, y);
            return row;
        }

        /// <summary>Grab the thumb (or click the track) and drag to scroll.</summary>
        void DragScrollbar(Mouse mouse)
        {
            if (scrollTrack == null) return;
            Vector2 mp = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame && RectTransformUtility.RectangleContainsScreenPoint(scrollTrack, mp, null))
            {
                draggingThumb = true;
                // grabbing the thumb keeps your grip point; clicking the track centres the thumb on the cursor
                thumbGrabOffset = RectTransformUtility.RectangleContainsScreenPoint(scrollThumb, mp, null)
                    ? ThumbLocalTop(mp) : scrollThumb.rect.height * 0.5f;
            }
            if (!mouse.leftButton.isPressed) draggingThumb = false;
            if (!draggingThumb) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(scrollTrack, mp, null, out var local);
            float trackH = scrollTrack.rect.height, thumbH = scrollThumb.rect.height;
            float fromTop = scrollTrack.rect.yMax - local.y - thumbGrabOffset; // thumb's top, measured down from the track's top
            float f = trackH - thumbH > 1f ? Mathf.Clamp01(fromTop / (trackH - thumbH)) : 0f;
            scrollY = f * scrollMax;
        }

        float ThumbLocalTop(Vector2 mp)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(scrollThumb, mp, null, out var l);
            return scrollThumb.rect.yMax - l.y;
        }

        void UpdateScroll()
        {
            if (scrollView == null) return;
            float viewH = scrollView.rect.height;
            scrollMax = Mathf.Max(0f, scrollInner.sizeDelta.y - viewH);
            scrollY = Mathf.Clamp(scrollY, 0f, scrollMax);
            scrollInner.anchoredPosition = new Vector2(0, scrollY);
            float frac = scrollInner.sizeDelta.y > 0 ? Mathf.Clamp01(viewH / scrollInner.sizeDelta.y) : 1f;
            var track = (RectTransform)scrollThumb.parent;
            float trackH = track.rect.height;
            scrollThumb.sizeDelta = new Vector2(0, trackH * frac);
            scrollThumb.anchoredPosition = new Vector2(0, scrollMax > 0 ? -(trackH - trackH * frac) * (scrollY / scrollMax) : 0f);
        }

        // =================================================================== items

        Item AddItem(string label, Action onSubmit, RectTransform area = null, bool fill = false)
        {
            var it = new Item { submit = onSubmit };
            int i = items.Count;
            it.rt = R("Item_" + label, area != null ? area : content, fill ? Vector2.zero : new Vector2(0.06f, 1f), fill ? Vector2.one : new Vector2(0.94f, 1f));
            if (!fill)
            {
                it.rt.pivot = new Vector2(0.5f, 1f);
                it.rt.sizeDelta = new Vector2(it.rt.sizeDelta.x, 74f);
                it.rt.anchoredPosition = new Vector2(0, -i * 82f);
            }
            it.bg = it.rt.gameObject.AddComponent<Image>();
            var m = R("Marker", it.rt, new Vector2(0, 0), new Vector2(0, 1));
            m.sizeDelta = new Vector2(10, 0);
            m.anchoredPosition = new Vector2(5, 0);
            it.marker = m.gameObject.AddComponent<Image>();
            it.marker.color = Accent;
            var l = R("Label", it.rt, Vector2.zero, fill ? Vector2.one : new Vector2(0.45f, 1f));
            l.offsetMin = new Vector2(fill ? 0 : 34, 0);
            it.label = Popup.MakeText(l.gameObject, label, 40, Color.white, fill ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
            items.Add(it);
            return it;
        }

        /// <summary>A setting with real &lt; and &gt; buttons around its value.</summary>
        Item AddSetting(string label, Func<string> get, Action<int> adjust)
        {
            var it = AddItem(label, () => { adjust(1); AfterChange(); });
            it.label.fontSize = 34;
            it.getValue = get;
            it.adjust = d => { adjust(d); AfterChange(); };
            it.left = R("Left", it.rt, new Vector2(0.47f, 0.1f), new Vector2(0.54f, 0.9f));
            it.leftText = Popup.MakeText(it.left.gameObject, "<", 40, ValueColor);
            it.right = R("Right", it.rt, new Vector2(0.91f, 0.1f), new Vector2(0.98f, 0.9f));
            it.rightText = Popup.MakeText(it.right.gameObject, ">", 40, ValueColor);
            var v = R("Value", it.rt, new Vector2(0.54f, 0), new Vector2(0.91f, 1));
            it.value = Popup.MakeText(v.gameObject, "", 34, ValueColor);
            it.value.supportRichText = false;
            it.leftText.supportRichText = it.rightText.supportRichText = false;
            return it;
        }

        /// <summary>A 0..max value shown as a bar you can click or drag (left/right still step it).</summary>
        void AddSlider(string label, Func<float> get, Action<float> set, float max, float step)
        {
            var it = AddSetting(label, () => $"{get() * 100f:0}%", d => set(Mathf.Clamp(Mathf.Round((get() + d * step) / step) * step, 0f, max)));
            it.get01 = () => get() / max;
            it.set01 = f => set(Mathf.Clamp(Mathf.Round(f * max / 0.05f) * 0.05f, 0f, max));
            // bar on the left part of the value area, percentage on the right
            ((RectTransform)it.value.transform).anchorMin = new Vector2(0.8f, 0f);
            it.value.alignment = TextAnchor.MiddleCenter;
            it.value.fontSize = 28;
            it.bar = R("Bar", it.rt, new Vector2(0.55f, 0.36f), new Vector2(0.79f, 0.64f));
            it.bar.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.12f);
            var f = R("Fill", it.bar, Vector2.zero, new Vector2(0.5f, 1f));
            it.fill = f.gameObject.AddComponent<Image>();
            it.fill.color = ValueColor;
        }

        void AfterChange()
        {
            GameSettings.ApplyLight();
            GameSettings.Save();
            Sfx.Play(Sfx.I != null ? Sfx.I.rotate : null, 0.6f, 1f);
        }

        void Refresh()
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                bool on = i == sel;
                it.bg.color = on ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.02f);
                it.marker.enabled = on;
                it.label.color = on ? Color.white : new Color(1f, 1f, 1f, 0.7f);
                if (it.getValue != null) it.value.text = it.getValue();
                if (it.leftText != null)
                {
                    var c = new Color(ValueColor.r, ValueColor.g, ValueColor.b, on ? 1f : 0.35f);
                    it.leftText.color = c;
                    it.rightText.color = c;
                }
                if (it.fill != null) ((RectTransform)it.fill.transform).anchorMax = new Vector2(Mathf.Clamp01(it.get01()), 1f);
                it.rt.localScale = Vector3.one * (on ? 1.02f : 1f);
            }
        }

        static int Wrap(int v, int n) => n <= 0 ? 0 : ((v % n) + n) % n;

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // =================================================================== input

        void Update()
        {
            if (page == Page.Closed)
            {
                if (open.WasPressedThisFrame() && Game.Bootstrap != null) Open();
                return;
            }

            float udt = Time.unscaledDeltaTime;
            Vector2 n = nav.ReadValue<Vector2>();

            if (page == Page.Controls)
            {
                // up/down scrolls the list (smoothly while held); the only selectable item is Back
                if (Mathf.Abs(n.y) > 0.3f) scrollY -= n.y * 1100f * udt;
                var m0 = Mouse.current;
                if (m0 != null)
                {
                    // Depending on platform/Input System settings a wheel notch reports either ~120 or ~1.
                    float wy = m0.scroll.ReadValue().y;
                    float notches = Mathf.Abs(wy) >= 10f ? wy / 120f : wy;
                    scrollY -= notches * 110f;
                    DragScrollbar(m0);
                }
                UpdateScroll();
            }
            else
            {
                int dy = n.y > 0.5f ? -1 : n.y < -0.5f ? 1 : 0;
                if (Repeat(dy, ref navDirY, ref navHoldY, udt) && items.Count > 0)
                {
                    sel = Wrap(sel + dy, items.Count);
                    Sfx.Play(Sfx.I != null ? Sfx.I.tick : null, 0.4f, 1.3f, 0f);
                    Refresh();
                }
            }
            int dx = n.x > 0.5f ? 1 : n.x < -0.5f ? -1 : 0;
            if (Repeat(dx, ref navDirX, ref navHoldX, udt) && items.Count > 0 && items[sel].adjust != null)
            {
                items[sel].adjust(dx);
                Refresh();
            }

            HandleMouse();

            if (cancel.WasPressedThisFrame())
            {
                if (page == Page.Main) Close();
                else Show(Page.Main);
                return;
            }
            if (submit.WasPressedThisFrame() && items.Count > 0)
            {
                items[sel].submit?.Invoke();
                if (page != Page.Closed) Refresh();
            }
        }

        void HandleMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 mp = mouse.position.ReadValue();
            bool moved = (mp - lastMouse).sqrMagnitude > 1f;
            lastMouse = mp;

            // dragging a slider bar
            if (dragging != null)
            {
                if (!mouse.leftButton.isPressed) { dragging = null; AfterChange(); return; }
                SetFromBar(dragging, mp);
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (!RectTransformUtility.RectangleContainsScreenPoint(it.rt, mp, null)) continue;
                if (moved && sel != i) { sel = i; Refresh(); }
                if (!mouse.leftButton.wasPressedThisFrame) break;
                sel = i;
                if (it.left != null && RectTransformUtility.RectangleContainsScreenPoint(it.left, mp, null)) { it.adjust(-1); Refresh(); }
                else if (it.right != null && RectTransformUtility.RectangleContainsScreenPoint(it.right, mp, null)) { it.adjust(1); Refresh(); }
                else if (it.bar != null && RectTransformUtility.RectangleContainsScreenPoint(it.bar, mp, null)) { dragging = it; SetFromBar(it, mp); }
                else if (it.adjust != null) { it.adjust(1); Refresh(); }   // click the row itself: next option
                else { it.submit?.Invoke(); if (page != Page.Closed) Refresh(); }
                break;
            }
        }

        void SetFromBar(Item it, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(it.bar, screen, null, out var local);
            var r = it.bar.rect;
            it.set01(Mathf.Clamp01((local.x - r.xMin) / Mathf.Max(1f, r.width)));
            GameSettings.ApplyLight();
            Refresh();
        }

        /// <summary>Edge on first push, then auto-repeat while held.</summary>
        static bool Repeat(int dir, ref int lastDir, ref float hold, float dt)
        {
            if (dir == 0) { lastDir = 0; return false; }
            if (dir != lastDir) { lastDir = dir; hold = 0.38f; return true; }
            hold -= dt;
            if (hold <= 0f) { hold = 0.11f; return true; }
            return false;
        }
    }
}
