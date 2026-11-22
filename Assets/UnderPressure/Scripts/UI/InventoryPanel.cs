using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// A player's inventory, drawn on the screen overlay (like the valve guide) so it stays big and readable at any
    /// camera distance, and pinned above their critter. Two pages share one panel: the item list, and (after Use) the
    /// highlighted item's details. Under both sits the button bar: one slot per item button, showing which item is on
    /// it, with that button's key glyph underneath. Prompts sit on what they act on (close in the corner, details on
    /// the highlighted row) instead of in a line of text. Sizes come from GameConfig and update live.
    /// </summary>
    public class InventoryPanel : MonoBehaviour
    {
        const int MaxRows = 5, Slots = 3;

        public struct Row { public string name, right, icon; public bool selected; }
        public struct Slot { public string key, icon; public bool highlight; }

        /// <summary>Everything the panel shows this frame.</summary>
        public class View
        {
            public string title;
            public Row[] rows = new Row[0];
            public Slot[] slots = new Slot[Slots];
            public bool pad;                 // controller glyphs instead of key caps
            public string closeKey, detailsKey;
            public bool assignHint;          // the highlighted item isn't on a button yet
            // details page (null name = list page)
            public string detailName, detailIcon, detailBody, detailUseKey, detailNote;
        }

        RectTransform root, panel;
        Image bg, accent;
        Text title, closeX, empty, assignHint, arrowUp, arrowDown;
        KeyGlyph closeKey;

        // list page
        RectTransform[] rowR = new RectTransform[MaxRows];
        Image[] rowBg = new Image[MaxRows], rowIcon = new Image[MaxRows];
        Text[] rowName = new Text[MaxRows], rowRight = new Text[MaxRows], rowChevron = new Text[MaxRows];
        KeyGlyph[] rowKey = new KeyGlyph[MaxRows];

        // details page
        Image detIcon;
        Text detBody, detUse, detNote;
        KeyGlyph detUseKey, backKey;
        Text backText;

        // button bar
        Image[] slotBg = new Image[Slots], slotEdge = new Image[Slots], slotIcon = new Image[Slots];
        KeyGlyph[] slotKey = new KeyGlyph[Slots];
        string[] slotWas = new string[Slots];
        float[] slotPop = new float[Slots];

        float pop, t;
        bool wasDetails;
        Color color = new Color(0.4f, 1f, 0.95f);

        static GameConfig Cfg => Game.Config;
        float W => Cfg != null ? Mathf.Max(300f, Cfg.inventoryWidth) : 860f;
        float RowH => Cfg != null ? Mathf.Max(30f, Cfg.inventoryRowHeight) : 84f;
        float TS => Cfg != null ? Cfg.inventoryTextScale : 1.1f;
        float Pad => Cfg != null ? Mathf.Max(0f, Cfg.inventoryPadding) : 28f;
        float Opacity => Cfg != null ? Cfg.inventoryOpacity : 0.74f;
        int F(float size) => Mathf.Max(8, Mathf.RoundToInt(size * TS));

        public static InventoryPanel Create(Color playerColor)
        {
            var go = new GameObject("InventoryPanel", typeof(RectTransform));
            if (Game.Hud != null) go.transform.SetParent(Game.Hud.transform, false);
            var p = go.AddComponent<InventoryPanel>();
            p.color = playerColor;
            p.Build();
            go.SetActive(false);
            return p;
        }

        // ------------------------------------------------------------------ building

        static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            return r;
        }

        /// <summary>Place a top-left anchored rect at (x, y) from the parent's top-left, y growing down.</summary>
        static void At(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        static Image Img(string name, Transform parent)
        {
            var i = Node(name, parent).gameObject.AddComponent<Image>();
            i.raycastTarget = false;
            return i;
        }

        static Text Txt(string name, Transform parent, int size, Color c, TextAnchor a)
        {
            var t = Popup.MakeText(Node(name, parent).gameObject, "", size, c, a);
            t.supportRichText = true;
            return t;
        }

        void Build()
        {
            root = (RectTransform)transform;
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(0.5f, 0f);

            panel = (RectTransform)new GameObject("Panel", typeof(RectTransform)).transform;
            panel.SetParent(root, false);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            bg = panel.gameObject.AddComponent<Image>();
            bg.raycastTarget = false;
            accent = Img("Accent", panel);
            accent.rectTransform.anchorMin = new Vector2(0, 1); accent.rectTransform.anchorMax = new Vector2(1, 1);
            accent.rectTransform.offsetMin = new Vector2(0, -8); accent.rectTransform.offsetMax = Vector2.zero;
            for (int i = 0; i < 12; i++)
            {
                var line = Img("Scan", panel).rectTransform;
                line.anchorMin = new Vector2(0, i / 12f); line.anchorMax = new Vector2(1, i / 12f);
                line.offsetMin = new Vector2(0, -1); line.offsetMax = new Vector2(0, 1);
                line.GetComponent<Image>().color = new Color(0.45f, 1f, 0.95f, 0.05f);
            }

            title = Txt("Title", panel, 42, Color.white, TextAnchor.MiddleLeft);
            closeKey = KeyGlyph.Create(panel);
            closeX = Txt("CloseX", panel, 40, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter);
            closeX.text = "×";

            for (int i = 0; i < MaxRows; i++)
            {
                rowR[i] = Node("Row" + i, panel);
                rowBg[i] = rowR[i].gameObject.AddComponent<Image>();
                rowBg[i].raycastTarget = false;
                rowIcon[i] = Img("Icon", rowR[i]);
                rowIcon[i].preserveAspect = true;
                rowName[i] = Txt("Name", rowR[i], 34, Color.white, TextAnchor.MiddleLeft);
                rowRight[i] = Txt("Right", rowR[i], 32, Color.white, TextAnchor.MiddleRight);
                rowKey[i] = KeyGlyph.Create(rowR[i]);
                rowChevron[i] = Txt("Chevron", rowR[i], 40, Color.white, TextAnchor.MiddleCenter);
                rowChevron[i].text = "›";
            }
            arrowUp = Txt("Up", panel, 22, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter); arrowUp.text = "▲";
            arrowDown = Txt("Down", panel, 22, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter); arrowDown.text = "▼";
            empty = Txt("Empty", panel, 32, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter);
            empty.text = "Nothing yet.\nVending machines sell items: the base has a row of them.";
            empty.horizontalOverflow = HorizontalWrapMode.Wrap;

            detIcon = Img("DetIcon", panel);
            detIcon.preserveAspect = true;
            detBody = Txt("DetBody", panel, 32, new Color(0.88f, 1f, 0.98f), TextAnchor.UpperLeft);
            detBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            detUse = Txt("DetUse", panel, 32, new Color(0.88f, 1f, 0.98f), TextAnchor.MiddleLeft);
            detUseKey = KeyGlyph.Create(panel);
            detNote = Txt("DetNote", panel, 30, new Color(1f, 0.87f, 0.4f), TextAnchor.MiddleLeft);
            backKey = KeyGlyph.Create(panel);
            backText = Txt("Back", panel, 26, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft);
            backText.text = "BACK";

            assignHint = Txt("Assign", panel, 24, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter);
            assignHint.text = "PRESS A BUTTON TO PUT IT THERE";
            for (int i = 0; i < Slots; i++)
            {
                slotEdge[i] = Img("SlotEdge" + i, panel);
                slotBg[i] = Img("Slot" + i, slotEdge[i].transform);
                slotBg[i].rectTransform.anchorMin = Vector2.zero; slotBg[i].rectTransform.anchorMax = Vector2.one;
                slotBg[i].rectTransform.offsetMin = new Vector2(4, 4); slotBg[i].rectTransform.offsetMax = new Vector2(-4, -4);
                slotIcon[i] = Img("Icon", slotEdge[i].transform);
                slotIcon[i].preserveAspect = true;
                slotIcon[i].rectTransform.anchorMin = Vector2.zero; slotIcon[i].rectTransform.anchorMax = Vector2.one;
                slotIcon[i].rectTransform.offsetMin = new Vector2(10, 10); slotIcon[i].rectTransform.offsetMax = new Vector2(-10, -10);
                slotKey[i] = KeyGlyph.Create(panel);
            }
        }

        // ------------------------------------------------------------------ every frame

        /// <summary>Call every frame while open. 'anchor' is the world point just above the critter's head.</summary>
        public void Show(Vector3 anchor, View v)
        {
            bool det = v.detailName != null;
            if (!gameObject.activeSelf) { gameObject.SetActive(true); pop = 1f; Sfx.Play(Sfx.I != null ? Sfx.I.tick : null, 0.5f, 1.4f); }
            else if (det != wasDetails) { pop = 0.6f; Sfx.Play(Sfx.I != null ? Sfx.I.tick : null, 0.5f, det ? 1.7f : 1.2f); }
            wasDetails = det;

            float w = W, rowH = RowH, pad = Pad, inner = w - pad * 2f;
            float glyphH = rowH * 0.62f;
            panel.sizeDelta = new Vector2(w, Mathf.Max(panel.sizeDelta.y, 100f)); // width first, so wrapped text measures right

            // ---- header: title, and close in the corner
            float titleH = F(42) + 30f;
            float y = 14f;
            title.fontSize = F(42);
            title.text = det ? v.detailName : v.title;
            closeKey.Set(v.closeKey, v.pad, glyphH);
            float closeXW = F(40) * 0.8f;
            closeKey.Place(w - pad - closeXW - 6f - closeKey.Width, y + (titleH - glyphH) * 0.5f);
            closeX.fontSize = F(44);
            At(closeX.rectTransform, w - pad - closeXW, y, closeXW, titleH);
            At(title.rectTransform, pad, y, inner - closeKey.Width - closeXW - 12f, titleH);
            y += titleH + 8f;

            // ---- list page
            int n = det ? 0 : Mathf.Min(v.rows.Length, MaxRows);
            for (int i = 0; i < MaxRows; i++)
            {
                bool on = i < n;
                rowR[i].gameObject.SetActive(on);
                if (!on) continue;
                var row = v.rows[i];
                At(rowR[i], pad, y + i * rowH, inner, rowH - 8f);
                float ih = rowH - 8f, iconS = ih * 0.86f;
                rowIcon[i].sprite = Glyphs.Icon(row.icon);
                rowIcon[i].enabled = rowIcon[i].sprite != null;
                At(rowIcon[i].rectTransform, 10f, (ih - iconS) * 0.5f, iconS, iconS);
                float nameX = 10f + (rowIcon[i].enabled ? iconS + 10f : 8f);
                // highlighted row: [E] › at the right edge, so it reads as "open this one"
                float rightEdge = inner - 14f;
                bool sel = row.selected;
                rowKey[i].gameObject.SetActive(sel);
                rowChevron[i].gameObject.SetActive(sel);
                if (sel)
                {
                    float chevW = F(40) * 0.6f;
                    rowChevron[i].fontSize = F(44);
                    At(rowChevron[i].rectTransform, rightEdge - chevW, 0f, chevW, ih);
                    rowKey[i].Set(v.detailsKey, v.pad, glyphH * 0.9f);
                    rowKey[i].Place(rightEdge - chevW - 4f - rowKey[i].Width, (ih - glyphH * 0.9f) * 0.5f);
                    rightEdge -= chevW + 4f + rowKey[i].Width + 18f;
                }
                rowRight[i].fontSize = F(32);
                rowRight[i].text = row.right;
                At(rowRight[i].rectTransform, rightEdge - 200f, 0f, 200f, ih);
                rowName[i].fontSize = F(34);
                rowName[i].text = row.name;
                At(rowName[i].rectTransform, nameX, 0f, Mathf.Max(10f, rightEdge - 200f - nameX), ih);
                rowBg[i].color = sel ? new Color(color.r, color.g, color.b, 0.42f) : new Color(1f, 1f, 1f, 0.05f);
                rowName[i].color = sel ? Color.white : new Color(1f, 1f, 1f, 0.7f);
            }
            // ▲▼ only when there's something to scroll to
            bool arrows = !det && n > 1;
            arrowUp.gameObject.SetActive(arrows);
            arrowDown.gameObject.SetActive(arrows);
            if (arrows)
            {
                arrowUp.fontSize = arrowDown.fontSize = F(20);
                At(arrowUp.rectTransform, 0f, y - 4f, pad, 26f);
                At(arrowDown.rectTransform, 0f, y + n * rowH - 30f, pad, 26f);
            }
            bool showEmpty = !det && v.rows.Length == 0;
            empty.gameObject.SetActive(showEmpty);
            if (showEmpty)
            {
                empty.fontSize = F(32);
                At(empty.rectTransform, pad, y, inner, rowH * 2f);
                y += rowH * 2f;
            }
            else if (!det) y += n * rowH;

            // ---- details page: big icon on the left, words on the right
            detIcon.gameObject.SetActive(det);
            detBody.gameObject.SetActive(det);
            bool useLine = det && v.detailUseKey != null;
            bool noteLine = det && !string.IsNullOrEmpty(v.detailNote);
            detUse.gameObject.SetActive(useLine);
            detUseKey.gameObject.SetActive(useLine);
            detNote.gameObject.SetActive(noteLine);
            backKey.gameObject.SetActive(det);
            backText.gameObject.SetActive(det);
            if (det)
            {
                float iconS = rowH * 1.7f;
                detIcon.sprite = Glyphs.Icon(v.detailIcon);
                detIcon.enabled = detIcon.sprite != null;
                At(detIcon.rectTransform, pad, y + 4f, iconS, iconS);
                float tx = pad + (detIcon.enabled ? iconS + 20f : 6f), tw = w - pad - tx;
                detBody.fontSize = F(32);
                detBody.text = v.detailBody;
                At(detBody.rectTransform, tx, y + 6f, tw, 40f);
                float bodyH = detBody.preferredHeight + 6f;
                At(detBody.rectTransform, tx, y + 6f, tw, bodyH);
                float ty = y + 6f + bodyH + 10f;
                if (useLine)
                {
                    detUse.fontSize = F(32);
                    detUse.text = "Use with";
                    float uw = detUse.preferredWidth + 14f;
                    At(detUse.rectTransform, tx, ty, uw, glyphH);
                    detUseKey.Set(v.detailUseKey, v.pad, glyphH);
                    detUseKey.Place(tx + uw, ty);
                    ty += glyphH + 8f;
                }
                if (noteLine)
                {
                    detNote.fontSize = F(30);
                    detNote.text = v.detailNote;
                    At(detNote.rectTransform, tx, ty, tw, glyphH);
                    ty += glyphH + 8f;
                }
                // back prompt under the icon
                float by = Mathf.Max(ty, y + iconS + 14f);
                float bh = glyphH * 0.8f;
                backKey.Set(v.detailsKey, v.pad, bh);
                backKey.Place(pad, by);
                backText.fontSize = F(24);
                At(backText.rectTransform, pad + backKey.Width + 10f, by, 200f, bh);
                y = by + bh;
                // never shorter than the list page was, so flipping pages doesn't jump
                y = Mathf.Max(y, 14f + titleH + 8f + Mathf.Max(Mathf.Min(v.rows.Length, MaxRows), 1) * rowH);
            }

            // ---- button bar: what's on each item button
            y += 12f;
            bool hasItems = v.rows.Length > 0;
            assignHint.gameObject.SetActive(hasItems && v.assignHint && !det);
            if (assignHint.gameObject.activeSelf)
            {
                assignHint.fontSize = F(22);
                At(assignHint.rectTransform, pad, y, inner, F(22) + 10f);
            }
            if (hasItems && !det) y += F(22) + 14f;
            float slotS = rowH * 1.15f, gap = slotS * 0.45f;
            float barW = Slots * slotS + (Slots - 1) * gap, bx = (w - barW) * 0.5f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 6f);
            for (int i = 0; i < Slots; i++)
            {
                var s = v.slots[i];
                if (s.icon != slotWas[i]) { if (s.icon != null) { slotPop[i] = 1f; } slotWas[i] = s.icon; }
                float sx = bx + i * (slotS + gap);
                At(slotEdge[i].rectTransform, sx, y, slotS, slotS);
                slotEdge[i].rectTransform.pivot = new Vector2(0.5f, 0.5f);
                slotEdge[i].rectTransform.anchoredPosition = new Vector2(sx + slotS * 0.5f, -y - slotS * 0.5f);
                slotEdge[i].rectTransform.localScale = Vector3.one * (1f + Juice.OutBack(slotPop[i]) * 0.25f);
                bool lit = s.highlight;
                bool waiting = v.assignHint && !det && hasItems && s.icon == null;
                slotEdge[i].color = lit ? color : waiting ? new Color(color.r, color.g, color.b, 0.25f + 0.35f * pulse) : new Color(1f, 1f, 1f, 0.18f);
                slotBg[i].color = new Color(0.01f, 0.05f, 0.06f, 0.85f);
                slotIcon[i].sprite = Glyphs.Icon(s.icon);
                slotIcon[i].enabled = slotIcon[i].sprite != null;
                slotKey[i].Set(s.key, v.pad, glyphH);
                slotKey[i].Place(sx + (slotS - slotKey[i].Width) * 0.5f, y + slotS + 8f);
                slotKey[i].SetScale(waiting ? 1f + 0.06f * pulse : 1f);
            }
            y += slotS + 8f + glyphH + 18f;

            float h = y;
            panel.sizeDelta = new Vector2(w, h);
            root.sizeDelta = new Vector2(w, h);
            panel.anchoredPosition = Vector2.zero;

            // pin above the critter, then keep it on screen
            var cam = Game.Cam != null ? Game.Cam.Cam : Camera.main;
            Vector3 sp = cam != null ? cam.WorldToScreenPoint(anchor) : new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 1f);
            float scale = root.lossyScale.x > 0f ? root.lossyScale.x : 1f; // canvas units -> pixels
            float halfW = w * 0.5f * scale, hPx = h * scale;
            float x = Mathf.Clamp(sp.x, 12f + halfW, Mathf.Max(12f + halfW, Screen.width - 12f - halfW));
            float py = Mathf.Clamp(sp.y + 24f * scale, 12f, Mathf.Max(12f, Screen.height - 12f - hPx));
            root.position = new Vector3(x, py, 0f);
        }

        public void SetColor(Color c) => color = c;

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
            wasDetails = false;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            pop = Mathf.MoveTowards(pop, 0f, dt * 4f);
            for (int i = 0; i < Slots; i++) slotPop[i] = Mathf.MoveTowards(slotPop[i], 0f, dt * 4f);
            panel.localScale = Vector3.one * (1f + Juice.OutBack(pop) * 0.08f);
            float g = Theme.Grime;
            float flicker = g > 0.05f && Random.value < g * 0.03f ? 0.6f : 1f;
            bg.color = new Color(0.02f, 0.1f, 0.12f, Opacity * flicker);
            accent.color = color;
            title.color = new Color(0.6f, 1f, 0.95f, (0.92f + 0.08f * Mathf.Sin(t * 20f)) * flicker);
        }
    }
}
