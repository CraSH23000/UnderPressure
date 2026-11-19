using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>Floating "+$10" / "LOOP!" text that tracks a world position, springs in, rises and fades.</summary>
    public class Popup : MonoBehaviour
    {
        Vector3 world;
        Text text;
        RectTransform rt;
        float age, life, rise;
        Color color;
        float scaleBase;

        static Font font;
        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static void Spawn(Vector3 worldPos, string msg, Color c, int size = 40, float life = 0.9f, float rise = 80f)
        {
            if (Game.Hud == null) return;
            var go = new GameObject("Popup");
            go.transform.SetParent(Game.Hud.PopupLayer, false);
            var p = go.AddComponent<Popup>();
            p.world = worldPos; p.life = life; p.rise = rise; p.color = c;
            p.text = MakeText(go, msg, size, c);
            p.rt = (RectTransform)go.transform;
            p.rt.sizeDelta = new Vector2(600, 120);
            p.scaleBase = 1f;
            p.UpdatePos();
        }

        public static Text MakeText(GameObject go, string msg, int size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var t = go.GetComponent<Text>();
            if (t == null) t = go.AddComponent<Text>();
            t.font = Font;
            t.text = msg;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = anchor;
            t.color = c;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var o = go.AddComponent<Outline>();
            o.effectColor = new Color(0.1f, 0.08f, 0.15f, 0.9f);
            o.effectDistance = new Vector2(3, -3);
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.4f);
            sh.effectDistance = new Vector2(0, -6);
            return t;
        }

        void UpdatePos()
        {
            var cam = Game.Cam != null ? Game.Cam.Cam : Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(world);
            float k = Juice.OutCubic(Mathf.Clamp01(age / life));
            rt.position = new Vector3(sp.x, sp.y + rise * k * Game.Hud.UiScale, 0f);
        }

        void Update()
        {
            age += Time.unscaledDeltaTime;
            if (age >= life) { Destroy(gameObject); return; }
            UpdatePos();
            float t = age / life;
            float pop = t < 0.18f ? Juice.OutBack(t / 0.18f) : 1f;
            float s = Mathf.Lerp(0.2f, 1f, pop) * scaleBase;
            rt.localScale = Vector3.one * s;
            var c = color;
            c.a = t > 0.7f ? 1f - (t - 0.7f) / 0.3f : 1f;
            text.color = c;
        }
    }

    /// <summary>Big centre-screen slam text ("LOOP! x3", "GOAL!").</summary>
    public class Banner : MonoBehaviour
    {
        Text text;
        RectTransform rt;
        float age, life;
        Color color;

        public static void Show(string msg, Color c, int size = 110, float life = 1.3f, float yOffset = 120f)
        {
            if (Game.Hud == null) return;
            var go = new GameObject("Banner");
            go.transform.SetParent(Game.Hud.PopupLayer, false);
            var b = go.AddComponent<Banner>();
            b.text = Popup.MakeText(go, msg, size, c);
            b.rt = (RectTransform)go.transform;
            b.rt.anchorMin = b.rt.anchorMax = new Vector2(0.5f, 0.5f);
            b.rt.anchoredPosition = new Vector2(0, yOffset);
            b.rt.sizeDelta = new Vector2(1400, 200);
            b.life = life;
            b.color = c;
            b.rt.localScale = Vector3.one * 3f;
        }

        void Update()
        {
            age += Time.unscaledDeltaTime;
            if (age >= life) { Destroy(gameObject); return; }
            float t = age / life;
            // slam in from big, settle with elastic, then shrink away
            float s = t < 0.25f ? Mathf.LerpUnclamped(3f, 1f, Juice.OutElastic(t / 0.25f)) : (t > 0.8f ? Mathf.Lerp(1f, 0.6f, (t - 0.8f) / 0.2f) : 1f);
            rt.localScale = Vector3.one * s;
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(age * 9f) * 3f * (1f - t));
            var c = color;
            c.a = t > 0.8f ? 1f - (t - 0.8f) / 0.2f : 1f;
            text.color = c;
        }
    }
}
