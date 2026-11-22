using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// Icons rendered by the Character Factory (CharacterFactory~/icons.py) and kept in Resources/Icons:
    /// blank input glyphs (a key cap, a trigger, a bumper ...) that get the key's name written on top, and item icons.
    /// </summary>
    public static class Glyphs
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        /// <summary>A sprite from Resources/Icons/&lt;name&gt;.png (null if it's missing).</summary>
        public static Sprite Icon(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (cache.TryGetValue(name, out var s) && s != null) return s;
            var tex = Resources.Load<Texture2D>("Icons/" + name);
            if (tex == null) { Debug.LogWarning("[UnderPressure] Missing icon Resources/Icons/" + name + ".png"); return null; }
            tex.wrapMode = TextureWrapMode.Clamp;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            s.name = name;
            cache[name] = s;
            return s;
        }

        public static void Clear() => cache.Clear();
    }

    /// <summary>One input prompt drawn as a little key cap or controller button with its name on it.</summary>
    public class KeyGlyph : MonoBehaviour
    {
        Image img;
        Text label;
        RectTransform rt, labelRt;
        string lastKey;
        bool lastPad;
        float lastH = -1f;

        /// <summary>Width this glyph takes at its current height.</summary>
        public float Width => rt != null ? rt.sizeDelta.x : 0f;

        public static KeyGlyph Create(Transform parent)
        {
            var go = new GameObject("Key", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<KeyGlyph>();
            g.rt = (RectTransform)go.transform;
            g.rt.anchorMin = g.rt.anchorMax = new Vector2(0f, 1f);
            g.rt.pivot = new Vector2(0f, 1f);
            g.img = go.AddComponent<Image>();
            g.img.raycastTarget = false;
            var lgo = new GameObject("Label", typeof(RectTransform));
            lgo.transform.SetParent(go.transform, false);
            g.labelRt = (RectTransform)lgo.transform;
            g.labelRt.anchorMin = Vector2.zero; g.labelRt.anchorMax = Vector2.one;
            g.label = lgo.AddComponent<Text>();
            g.label.font = Popup.Font;
            g.label.fontStyle = FontStyle.Bold;
            g.label.alignment = TextAnchor.MiddleCenter;
            g.label.horizontalOverflow = HorizontalWrapMode.Overflow;
            g.label.verticalOverflow = VerticalWrapMode.Overflow;
            g.label.raycastTarget = false;
            return g;
        }

        static string Short(string key)
        {
            switch (key)
            {
                case "Backspace": return "Bksp";
                case "R-Ctrl": return "RCtrl";
                case "Select": return "VIEW";
                case "Start": return "MENU";
                default: return key;
            }
        }

        /// <summary>Show 'key' (as the game names it, e.g. "1", "PgDn", "RT", "X") at height h.
        /// 'pad' picks controller shapes instead of key caps.</summary>
        public void Set(string key, bool pad, float h)
        {
            if (key == lastKey && pad == lastPad && Mathf.Approximately(h, lastH)) return;
            lastKey = key; lastPad = pad; lastH = h;
            string text = Short(key);
            string sprite;
            Color tc;
            float textUp = 0f, sizeK;
            if (pad)
            {
                tc = Color.white;
                if (key == "RT" || key == "LT") { sprite = "glyph_trigger"; sizeK = 0.38f; }
                else if (key == "RB" || key == "LB") { sprite = "glyph_bumper"; sizeK = 0.4f; }
                else if (key.Length == 1)
                {
                    sprite = "glyph_face"; sizeK = 0.5f;
                    tc = key == "A" ? new Color(0.45f, 0.95f, 0.35f) : key == "B" ? new Color(1f, 0.38f, 0.32f)
                       : key == "X" ? new Color(0.35f, 0.65f, 1f) : key == "Y" ? new Color(1f, 0.85f, 0.25f) : Color.white;
                }
                else { sprite = "glyph_pill"; sizeK = 0.3f; }
            }
            else
            {
                tc = new Color(0.12f, 0.14f, 0.17f);
                textUp = 0.05f; // the cap's top face sits a little above the image's middle
                if (text.Length <= 1) { sprite = "glyph_key"; sizeK = 0.48f; }
                else { sprite = "glyph_key_wide"; sizeK = text.Length <= 3 ? 0.4f : 0.32f; }
            }
            img.sprite = Glyphs.Icon(sprite);
            img.color = img.sprite != null ? Color.white : new Color(0.2f, 0.25f, 0.3f, 0.9f);
            float aspect = Aspect(sprite); // from the art, not the texture (Unity may have resized it to a power of two)
            rt.sizeDelta = new Vector2(h * aspect, h);
            label.text = text;
            label.color = tc;
            label.fontSize = Mathf.Max(8, Mathf.RoundToInt(h * sizeK));
            labelRt.offsetMin = new Vector2(0f, h * textUp);
            labelRt.offsetMax = new Vector2(0f, h * textUp);
        }

        static float Aspect(string sprite)
        {
            switch (sprite)
            {
                case "glyph_key_wide": return 428f / 256f;
                case "glyph_bumper": case "glyph_pill": return 2f;
                default: return 1f;
            }
        }

        public void Place(float x, float y) => rt.anchoredPosition = new Vector2(x, -y);
        public void SetAlpha(float a) { img.color = new Color(img.color.r, img.color.g, img.color.b, a); label.color = new Color(label.color.r, label.color.g, label.color.b, a); }
        public void SetScale(float s) => rt.localScale = Vector3.one * s;
    }
}
