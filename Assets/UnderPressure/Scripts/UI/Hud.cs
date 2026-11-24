using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>Code-built overlay HUD: money, countdown, distance goal, unscrew progress ring, end screen.</summary>
    public class Hud : MonoBehaviour
    {
        public Transform PopupLayer { get; private set; }
        public PauseMenu Menu { get; private set; }
        public float UiScale => canvas != null ? canvas.scaleFactor : 1f;

        Canvas canvas;
        Text timer, timerSub, distance, hints;
        RectTransform distanceFill, timerRect;
        HoloMoney holoMoney;
        Image distanceFillImg;
        readonly Image[] rings = new Image[JoinManager.MaxPlayers];
        readonly RectTransform[] ringRects = new RectTransform[JoinManager.MaxPlayers];
        Text roster;
        GameObject distanceRoot;
        bool joinOpen;
        System.Collections.Generic.IList<PlayerController> rosterPlayers;
        GameObject endPanel, pausePanel;
        Image flashImg;
        float flashAmount;
        Color flashColor = Color.white;

        // valve guide
        RectTransform valveGuide, valvePrompt, valveSlow, valveFast;
        Text valveSpeed, valveSlowKey, valveFastKey, valveDone, valvePromptText;
        Image valveAccent;
        Image[] valvePips;
        int guideFrame = -1, promptFrame = -1;
        float punchSlow, punchFast, punchMid;
        Text endTitle, endStats, endPrompt;
        float timerPunch, distancePunch;
        int lastDistance = -1;

        static readonly Color Ink = new Color(0.1f, 0.08f, 0.15f, 0.85f);

        public void Build()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var root = (RectTransform)transform;

            // Money (top-left): EARNINGS + LAB FUNDING hologram, counting digit by digit
            holoMoney = HoloMoney.Create(root);

            // Countdown (top-centre)
            timerRect = Rect("Timer", root, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(700, 90));
            timer = Popup.MakeText(timerRect.gameObject, "", 70, Color.white);
            var ts = Rect("TimerSub", root, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -105), new Vector2(700, 40));
            timerSub = Popup.MakeText(ts.gameObject, "", 28, new Color(1f, 1f, 1f, 0.85f));

            // Distance (top-right)
            var dg = Rect("Distance", root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -30), new Vector2(420, 110));
            var dt = Rect("Label", dg, new Vector2(0, 0.45f), new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            distance = Popup.MakeText(dt.gameObject, "", 46, Color.white, TextAnchor.MiddleRight);
            var bar = Rect("Bar", dg, new Vector2(0, 0), new Vector2(1, 0.38f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            bar.gameObject.AddComponent<Image>().color = Ink;
            distanceFill = Rect("Fill", bar, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            distanceFill.offsetMin = new Vector2(5, 5);
            distanceFill.offsetMax = new Vector2(-5, -5);
            distanceFillImg = distanceFill.gameObject.AddComponent<Image>();
            distanceFillImg.color = Mats.Liquid;
            distanceRoot = dg.gameObject; // the round's goal (hidden at the base, where there's no round)

            // Controls (bottom-left)
            var ht = Rect("Hints", root, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 24), new Vector2(1840, 150));
            hints = Popup.MakeText(ht.gameObject, "<b>Esc / Start</b>  menu · controls", 22, new Color(1f, 1f, 1f, 0.6f), TextAnchor.LowerLeft);
            hints.supportRichText = true;

            // Version (bottom-right), faint, so playtesters can quote it and it shows in screenshots
            var vr = Rect("Version", root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 24), new Vector2(400, 30));
            Popup.MakeText(vr.gameObject, BuildInfo.Label, 20, new Color(1f, 1f, 1f, 0.45f), TextAnchor.LowerRight);

            // Unscrew progress rings (one per player, follows them)
            var ringSprite = Mats.MakeSprite(Mats.RingTexture);
            for (int i = 0; i < JoinManager.MaxPlayers; i++)
            {
                var rr = Rect("Ring" + i, root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(84, 84));
                var bg = rr.gameObject.AddComponent<Image>();
                bg.sprite = ringSprite;
                bg.color = Ink;
                var rf = Rect("Fill", rr, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                var ring = rf.gameObject.AddComponent<Image>();
                ring.sprite = ringSprite;
                ring.type = Image.Type.Filled;
                ring.fillMethod = Image.FillMethod.Radial360;
                ring.fillOrigin = (int)Image.Origin360.Top;
                ring.fillClockwise = true;
                ring.color = JoinManager.PlayerColors[i];
                rr.gameObject.SetActive(false);
                rings[i] = ring;
                ringRects[i] = rr;
            }

            // Player roster under the money: one player per line in slot order, and "P2 join" on the next free line
            var ro = Rect("Roster", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -140), new Vector2(700, 240));
            roster = Popup.MakeText(ro.gameObject, "", 30, Color.white, TextAnchor.UpperLeft);
            roster.supportRichText = true;
            roster.lineSpacing = 1.1f;

            // Popups go above everything else
            PopupLayer = Rect("Popups", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            // End panel
            var ep = Rect("End", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            ep.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.04f, 0.1f, 0.6f);
            endPanel = ep.gameObject;
            var et = Rect("Title", ep, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(1400, 160));
            endTitle = Popup.MakeText(et.gameObject, "", 120, Color.white);
            var es = Rect("Stats", ep, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(1200, 200));
            endStats = Popup.MakeText(es.gameObject, "", 44, Color.white);
            var epr = Rect("Prompt", ep, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -190), new Vector2(1200, 60));
            endPrompt = Popup.MakeText(epr.gameObject, "Press Space / (A) to play again", 36, Mats.Money);
            endPanel.SetActive(false);

            // Pause panel
            var pp = Rect("Pause", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            pp.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.03f, 0.05f, 0.72f);
            pausePanel = pp.gameObject;
            var pt = Rect("Title", pp, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1200, 160));
            Popup.MakeText(pt.gameObject, "PAUSED", 120, Color.white);
            var ps = Rect("Help", pp, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(1500, 260));
            var help = Popup.MakeText(ps.gameObject,
                "Esc / Start  —  resume\nR / Select  —  restart\n\n" +
                "<size=28>PAD: stick move · (A) jump · (X) use · (Y)/(B) rotate · LT dash\n" +
                "WASD: Space jump · E use · F/G rotate · L-Shift dash\n" +
                "ARROWS: R-Ctrl jump · Enter use · ; / ' rotate · R-Shift dash</size>", 44, Color.white);
            help.supportRichText = true;
            pausePanel.SetActive(false);
            BuildValveGuide(root);
            Menu = gameObject.AddComponent<PauseMenu>();
            Menu.Build(root);
            var fl = Rect("Flash", root, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            flashImg = fl.gameObject.AddComponent<Image>();
            flashImg.raycastTarget = false;
            flashImg.color = new Color(1, 1, 1, 0);
            PopupLayer.SetAsLastSibling();
        }

        void BuildValveGuide(RectTransform root)
        {
            var panelColor = new Color(0.05f, 0.05f, 0.08f, 0.78f);
            valveGuide = Rect("ValveGuide", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(560, 170));
            valveGuide.gameObject.AddComponent<Image>().color = panelColor;
            var accent = Rect("Accent", valveGuide, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0, 8));
            valveAccent = accent.gameObject.AddComponent<Image>();

            valveSlow = Rect("Slow", valveGuide, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 12), new Vector2(150, 110));
            Popup.MakeText(Rect("Label", valveSlow, new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject,
                "< SLOWER", 28, Theme.AccentBlue);
            valveSlowKey = Popup.MakeText(Rect("Key", valveSlow, new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject,
                "[Y]", 40, Color.white);

            valveFast = Rect("Fast", valveGuide, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16, 12), new Vector2(150, 110));
            Popup.MakeText(Rect("Label", valveFast, new Vector2(0, 0.5f), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject,
                "FASTER >", 28, Mats.Danger);
            valveFastKey = Popup.MakeText(Rect("Key", valveFast, new Vector2(0, 0), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject,
                "[B]", 40, Color.white);

            var mid = Rect("Speed", valveGuide, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 32), new Vector2(240, 70));
            valveSpeed = Popup.MakeText(mid.gameObject, "x1", 56, Color.white);

            valvePips = new Image[8];
            for (int i = 0; i < valvePips.Length; i++)
            {
                var pip = Rect("Pip" + i, valveGuide, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18, 18));
                valvePips[i] = pip.gameObject.AddComponent<Image>();
            }

            var done = Rect("Done", valveGuide, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), new Vector2(0, 8), new Vector2(0, 34));
            valveDone = Popup.MakeText(done.gameObject, "", 22, new Color(1f, 1f, 1f, 0.75f));
            valveGuide.gameObject.SetActive(false);

            valvePrompt = Rect("ValvePrompt", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(300, 54));
            valvePrompt.gameObject.AddComponent<Image>().color = panelColor;
            valvePromptText = Popup.MakeText(Rect("Text", valvePrompt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject,
                "", 28, Color.white);
            valvePromptText.supportRichText = true;
            valvePrompt.gameObject.SetActive(false);
        }

        Vector3 ScreenAt(Vector3 world)
        {
            var cam = Game.Cam != null ? Game.Cam.Cam : Camera.main;
            return cam != null ? cam.WorldToScreenPoint(world) : Vector3.zero;
        }

        /// <summary>Call every frame while a player is on the valve (hides itself when no longer called).</summary>
        public void ShowValveGuide(Vector3 world, float speed, int index, int count, string slowKey, string fastKey, string useKey, Color player,
            string title = "", string footer = null)
        {
            guideFrame = Time.frameCount;
            valveGuide.gameObject.SetActive(true);
            valveGuide.position = ScreenAt(world);
            valveAccent.color = player;
            valveSpeed.text = string.IsNullOrEmpty(title) ? $"x{speed:0.##}" : $"<size=30>{title}</size> x{speed:0.##}";
            valveSpeed.supportRichText = true;
            valveSpeed.color = speed > 1.01f ? Color.Lerp(Color.white, Mats.Danger, Mathf.InverseLerp(1f, 3f, speed)) :
                speed < 0.99f ? Theme.AccentBlue : Color.white;
            valveSlowKey.text = $"[{slowKey}]";
            valveFastKey.text = $"[{fastKey}]";
            valveDone.text = $"[{useKey}] done  ·  move to let go" + (footer ?? (Game.Config.moneyScalesWithFlowSpeed ? "  ·  faster pays more" : ""));
            float spacing = 26f;
            for (int i = 0; i < valvePips.Length; i++)
            {
                bool on = i < count;
                valvePips[i].gameObject.SetActive(on);
                if (!on) continue;
                ((RectTransform)valvePips[i].transform).anchoredPosition = new Vector2((i - (count - 1) * 0.5f) * spacing, -18f);
                valvePips[i].color = i == index ? Color.white : (i < index ? new Color(1f, 1f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.15f));
                valvePips[i].transform.localScale = Vector3.one * (i == index ? 1.3f : 1f);
            }
        }

        public void ShowValvePrompt(Vector3 world, string useKey, Color player, string label = "FLOW VALVE")
        {
            promptFrame = Time.frameCount;
            valvePrompt.gameObject.SetActive(true);
            valvePrompt.position = ScreenAt(world);
            valvePromptText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(player)}>[{useKey}]</color> {label}";
        }

        /// <summary>-1 = slower pressed, +1 = faster pressed, 0 = grabbed the valve.</summary>
        public void PunchValve(int dir)
        {
            if (dir < 0) punchSlow = 1f;
            else if (dir > 0) punchFast = 1f;
            punchMid = 1f;
        }

        void LateUpdate()
        {
            if (flashImg != null)
            {
                flashAmount = Mathf.MoveTowards(flashAmount, 0f, Time.unscaledDeltaTime * 3f);
                flashImg.color = new Color(flashColor.r, flashColor.g, flashColor.b, flashAmount * flashAmount);
            }
            UpdateClosetPanels();
            if (valveGuide == null) return;
            if (guideFrame != Time.frameCount && valveGuide.gameObject.activeSelf) valveGuide.gameObject.SetActive(false);
            if ((promptFrame != Time.frameCount || valveGuide.gameObject.activeSelf) && valvePrompt.gameObject.activeSelf) valvePrompt.gameObject.SetActive(false);
            float dt = Time.unscaledDeltaTime;
            punchSlow = Mathf.MoveTowards(punchSlow, 0f, dt * 5f);
            punchFast = Mathf.MoveTowards(punchFast, 0f, dt * 5f);
            punchMid = Mathf.MoveTowards(punchMid, 0f, dt * 4f);
            valveSlow.localScale = Vector3.one * (1f + punchSlow * 0.3f);
            valveFast.localScale = Vector3.one * (1f + punchFast * 0.3f);
            valveSpeed.transform.localScale = Vector3.one * (1f + punchMid * 0.35f);
            // the hint breathes gently so it reads as a "ghost" prompt
            float a = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
            valvePrompt.localScale = Vector3.one * (0.97f + 0.03f * a);
        }

        // ------------------------------------------------------------------ closet panels (docked along the bottom, one per player)

        class ClosetPanel
        {
            public RectTransform root;
            public Image accent;
            public Text title, hint;
            public RectTransform[] rows;
            public Image[] rowBg, swatch;
            public Text[] label, value;
            public Image[] badge;        // NEW! stickers on rows with something new
            public bool[] wasValueNew;   // to burst confetti the moment you land on the new item
            public float[] badgePop;
            public int frame = -1;
            public float punch, punchDir;
        }
        readonly ClosetPanel[] closetPanels = new ClosetPanel[JoinManager.MaxPlayers];
        const int ClosetMaxRows = 9; // a base locker has every row

        ClosetPanel GetClosetPanel(int player)
        {
            var cp = closetPanels[player];
            if (cp != null) return cp;
            cp = closetPanels[player] = new ClosetPanel();
            float w = 440f, rowH = 54f;
            float h = 70f + ClosetMaxRows * rowH + 44f;
            float x = (player + 0.5f) / JoinManager.MaxPlayers;
            cp.root = Rect("ClosetPanel" + player, transform, new Vector2(x, 0), new Vector2(x, 0), new Vector2(0.5f, 0f), new Vector2(0, 180), new Vector2(w, h));
            cp.root.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.86f);
            cp.accent = Rect("Accent", cp.root, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0, 8)).gameObject.AddComponent<Image>();
            cp.title = Popup.MakeText(Rect("Title", cp.root, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), new Vector2(0, -12), new Vector2(-20, 50)).gameObject,
                "", 32, Color.white);
            cp.rows = new RectTransform[ClosetMaxRows];
            cp.rowBg = new Image[ClosetMaxRows];
            cp.swatch = new Image[ClosetMaxRows];
            cp.label = new Text[ClosetMaxRows];
            cp.value = new Text[ClosetMaxRows];
            for (int i = 0; i < ClosetMaxRows; i++)
            {
                var r = Rect("Row" + i, cp.root, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), new Vector2(0, -70 - i * rowH), new Vector2(-24, rowH - 6));
                cp.rows[i] = r;
                cp.rowBg[i] = r.gameObject.AddComponent<Image>();
                cp.label[i] = Popup.MakeText(Rect("Label", r, new Vector2(0, 0), new Vector2(0.32f, 1), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(-14, 0)).gameObject,
                    "", 22, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft);
                cp.value[i] = Popup.MakeText(Rect("Value", r, new Vector2(0.32f, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject,
                    "", 28, Color.white);
                cp.value[i].supportRichText = false;
                cp.swatch[i] = Rect("Swatch", r, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-54, 0), new Vector2(30, 30)).gameObject.AddComponent<Image>();
                cp.swatch[i].sprite = Mats.MakeSprite(Mats.CircleTexture);
                // the NEW! sticker sits half over the row's right end, like it was slapped on
                var b = Rect("NewBadge", r, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-6, 4), new Vector2(66, 66)).gameObject.AddComponent<Image>();
                b.sprite = Glyphs.Icon("badge_new");
                b.preserveAspect = true;
                b.raycastTarget = false;
                b.gameObject.SetActive(false);
                if (cp.badge == null) { cp.badge = new Image[ClosetMaxRows]; cp.wasValueNew = new bool[ClosetMaxRows]; cp.badgePop = new float[ClosetMaxRows]; }
                cp.badge[i] = b;
            }
            cp.hint = Popup.MakeText(Rect("Hint", cp.root, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), new Vector2(0, 8), new Vector2(-16, 34)).gameObject,
                "", 19, new Color(1f, 1f, 1f, 0.7f));
            return cp;
        }

        /// <summary>Call every frame while a player is inside a closet (hides itself when no longer called).</summary>
        public void ShowCloset(int player, Color pc, string title, string[] labels, string[] values, Color[] swatches, int selected, string hint,
            bool[] rowNew = null, bool[] valueNew = null)
        {
            if (player < 0 || player >= closetPanels.Length) return;
            var cp = GetClosetPanel(player);
            cp.frame = Time.frameCount;
            if (!cp.root.gameObject.activeSelf) { cp.root.gameObject.SetActive(true); cp.punch = 1f; }
            cp.accent.color = pc;
            cp.title.text = title;
            cp.hint.text = hint;
            int n = Mathf.Min(labels.Length, ClosetMaxRows);
            float rowH = 54f;
            cp.root.sizeDelta = new Vector2(440f, 70f + n * rowH + 44f);
            for (int i = 0; i < ClosetMaxRows; i++)
            {
                bool on = i < n;
                cp.rows[i].gameObject.SetActive(on);
                if (!on) continue;
                bool sel = i == selected;
                cp.rowBg[i].color = sel ? new Color(pc.r, pc.g, pc.b, 0.35f) : new Color(1f, 1f, 1f, 0.05f);
                cp.label[i].text = labels[i];
                cp.value[i].text = sel ? "<  " + values[i] + "  >" : values[i];
                // a color row is written in that color (a swatch dot used to cover the name)
                bool tinted = swatches != null && swatches[i].a > 0f;
                Color vc = tinted ? new Color(swatches[i].r, swatches[i].g, swatches[i].b, 1f) : Color.white;
                cp.value[i].color = sel ? vc : new Color(vc.r, vc.g, vc.b, tinted ? 0.85f : 0.6f);
                cp.swatch[i].gameObject.SetActive(false);
                float s = sel ? 1f + cp.punch * 0.12f : 1f;
                cp.value[i].transform.localScale = Vector3.one * s;
                cp.value[i].rectTransform.anchoredPosition = new Vector2(sel ? cp.punchDir * cp.punch * 14f : 0f, 0f);

                // NEW! sticker: on every row that has something new (so you know where to look); when the item
                // showing is the new one it grows, wobbles and throws confetti
                bool rn = rowNew != null && i < rowNew.Length && rowNew[i];
                bool vn = valueNew != null && i < valueNew.Length && valueNew[i];
                var badge = cp.badge[i];
                if (badge.gameObject.activeSelf != rn) { badge.gameObject.SetActive(rn); if (rn) cp.badgePop[i] = 1f; }
                cp.label[i].color = rn ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 1f, 1f, 0.7f);
                if (rn)
                {
                    float t = Time.unscaledTime;
                    if (vn && !cp.wasValueNew[i]) { cp.badgePop[i] = 1.4f; UiConfetti(badge.rectTransform, 26); }
                    else if (vn && Random.value < Time.unscaledDeltaTime * 3f) UiConfetti(badge.rectTransform, 2);
                    cp.badgePop[i] = Mathf.MoveTowards(cp.badgePop[i], 0f, Time.unscaledDeltaTime * 3f);
                    float size = (vn ? 1.25f : 0.9f) + Juice.OutBack(Mathf.Clamp01(cp.badgePop[i])) * 0.35f + Mathf.Sin(t * 5f + i) * 0.04f;
                    badge.rectTransform.localScale = Vector3.one * size;
                    badge.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * (vn ? 7f : 2.5f) + i) * (vn ? 14f : 6f));
                }
                cp.wasValueNew[i] = vn;
            }
        }

        public void PunchCloset(int player, int dir)
        {
            if (player < 0 || player >= closetPanels.Length || closetPanels[player] == null) return;
            closetPanels[player].punch = 1f;
            closetPanels[player].punchDir = dir;
        }

        // ------------------------------------------------------------------ UI confetti (for NEW! stickers)

        class Bit { public RectTransform rt; public Image img; public Vector2 vel; public float life, spin, angle; }
        readonly System.Collections.Generic.List<Bit> bits = new System.Collections.Generic.List<Bit>();
        static readonly Color[] ConfettiColors =
        {
            new Color(1f, 0.85f, 0.2f), new Color(1f, 0.35f, 0.4f), new Color(0.35f, 0.8f, 1f), new Color(0.45f, 1f, 0.5f), new Color(0.85f, 0.5f, 1f),
        };

        /// <summary>Little paper bits bursting from a UI element.</summary>
        public void UiConfetti(RectTransform from, int count)
        {
            if (from == null) return;
            for (int k = 0; k < count; k++)
            {
                var rt = Rect("Confetti", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 6));
                rt.position = from.position;
                var img = rt.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                img.color = ConfettiColors[Random.Range(0, ConfettiColors.Length)];
                float a = Random.Range(0f, Mathf.PI * 2f);
                bits.Add(new Bit { rt = rt, img = img, vel = new Vector2(Mathf.Cos(a), Mathf.Abs(Mathf.Sin(a)) + 0.3f) * Random.Range(180f, 420f), life = Random.Range(0.8f, 1.4f), spin = Random.Range(-720f, 720f) });
            }
        }

        void UpdateConfetti()
        {
            float dt = Time.unscaledDeltaTime;
            for (int k = bits.Count - 1; k >= 0; k--)
            {
                var b = bits[k];
                b.life -= dt;
                if (b.life <= 0f || b.rt == null) { if (b.rt != null) Destroy(b.rt.gameObject); bits.RemoveAt(k); continue; }
                b.vel += new Vector2(0f, -900f) * dt;
                b.vel *= 1f - dt * 1.5f;
                b.rt.anchoredPosition += b.vel * dt;
                b.angle += b.spin * dt;
                b.rt.localRotation = Quaternion.Euler(0, 0, b.angle);
                var c = b.img.color; c.a = Mathf.Clamp01(b.life * 2f); b.img.color = c;
            }
        }

        void UpdateClosetPanels()
        {
            UpdateConfetti();
            foreach (var cp in closetPanels)
            {
                if (cp == null) continue;
                if (cp.frame != Time.frameCount && cp.root.gameObject.activeSelf) cp.root.gameObject.SetActive(false);
                cp.punch = Mathf.MoveTowards(cp.punch, 0f, Time.unscaledDeltaTime * 5f);
            }
        }

        static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        // ------------------------------------------------------------------ API

        public void PunchMoney(float amount = 1f) => holoMoney?.Punch(amount);

        public void SetTimer(string main, string sub, Color color)
        {
            timer.text = main;
            timer.color = color;
            timerSub.text = sub;
        }
        public void PunchTimer(float amount = 1f) => timerPunch = Mathf.Max(timerPunch, amount);

        public void SetDistance(int value, int goal)
        {
            bool done = value >= goal;
            distance.text = done ? $"{value} PIPES  <color=#FFD640>GOAL!</color>" : $"{value} / {goal} PIPES";
            distance.supportRichText = true;
            distanceFill.anchorMax = new Vector2(Mathf.Clamp01(value / (float)Mathf.Max(1, goal)), 1f);
            distanceFillImg.color = done ? Mats.Money : Mats.Liquid;
            if (value != lastDistance && lastDistance >= 0) distancePunch = 1f;
            lastDistance = value;
        }

        public void ShowProgress(int player, Vector3 world, float value)
        {
            var cam = Game.Cam != null ? Game.Cam.Cam : Camera.main;
            if (cam == null) return;
            var rr = ringRects[Mathf.Clamp(player, 0, ringRects.Length - 1)];
            rr.gameObject.SetActive(true);
            rr.position = cam.WorldToScreenPoint(world);
            var fill = rings[Mathf.Clamp(player, 0, rings.Length - 1)];
            fill.fillAmount = value;
            foreach (var pl in Game.Players) if (pl != null && pl.Index == player) fill.color = pl.Color; // players can change color
            rr.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(value * Mathf.PI * 6f) * (1f - value) + (value > 0.97f ? 0.3f : 0f));
        }

        public void HideProgress(int player)
        {
            var rr = ringRects[Mathf.Clamp(player, 0, ringRects.Length - 1)];
            if (rr.gameObject.activeSelf) rr.gameObject.SetActive(false);
        }

        /// <summary>Whether to show the "Pn join" line under the roster (there's room, and someone's already playing).</summary>
        public void SetJoinPrompt(bool show)
        {
            if (show == joinOpen) return;
            joinOpen = show;
            RebuildRoster();
        }

        /// <summary>The distance goal (top-right); hidden at the base.</summary>
        public void ShowDistance(bool on)
        {
            if (distanceRoot != null && distanceRoot.activeSelf != on) distanceRoot.SetActive(on);
        }

        /// <summary>Full-screen flash (lightning strike) that fades out.</summary>
        public void Flash(Color c, float amount = 1f)
        {
            flashColor = c;
            flashAmount = Mathf.Max(flashAmount, amount);
        }

        public void ShowPause(bool on)
        {
            pausePanel.SetActive(on);
            pausePanel.transform.SetAsLastSibling();
        }

        public void SetRoster(System.Collections.Generic.IList<PlayerController> players)
        {
            rosterPlayers = players;
            RebuildRoster();
        }

        void RebuildRoster()
        {
            var sb = new System.Text.StringBuilder();
            bool joinShown = false;
            for (int i = 0; i < JoinManager.MaxPlayers; i++)
            {
                PlayerController p = null;
                if (rosterPlayers != null) foreach (var pl in rosterPlayers) if (pl != null && pl.Index == i) p = pl;
                if (p != null)
                {
                    string hex = ColorUtility.ToHtmlStringRGB(p.Color);
                    sb.Append($"<color=#{hex}>P{i + 1}</color> <size=20>{JoinManager.SchemeLabel(p.Scheme)}</size>\n");
                }
                else if (joinOpen && !joinShown)
                {
                    joinShown = true;
                    sb.Append($"<color=#FFFFFF99>P{i + 1}</color> <size=20><color=#FFFFFF99>JOIN</color></size>\n");
                }
            }
            roster.text = sb.ToString();
        }

        public void ShowEnd(bool won, int dist, int goal, int loops, int cash, string extra = null)
        {
            endPanel.SetActive(true);
            endTitle.text = won ? "PLUMBING COMPLETE!" : "SPILLED!";
            endTitle.color = won ? Mats.Money : Mats.Danger;
            endStats.text = $"Distance {dist} / {goal}      Loops {loops}\nEarned ${cash:N0}" + (string.IsNullOrEmpty(extra) ? "" : "\n<size=34>" + extra + "</size>");
            endStats.supportRichText = true;
            endPanel.transform.localScale = Vector3.one;
            Juice.ScaleTo(endTitle.transform, Vector3.one * 2.5f, Vector3.one, 0.5f, Juice.OutElastic);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            timerPunch = Mathf.MoveTowards(timerPunch, 0f, dt * 5f);
            distancePunch = Mathf.MoveTowards(distancePunch, 0f, dt * 4f);
            timerRect.localScale = Vector3.one * (1f + timerPunch * 0.3f);
            distance.transform.localScale = Vector3.one * (1f + distancePunch * 0.2f);
            if (endPanel.activeSelf)
                endPrompt.color = new Color(Mats.Money.r, Mats.Money.g, Mats.Money.b, 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 5f));
        }
    }
}
