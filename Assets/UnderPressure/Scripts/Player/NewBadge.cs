using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// A small gold hologram floating over a closet that has something newly unlocked inside: "NEW!" plus who it's
    /// for and what it is, with UI confetti drifting through it. Same look as the spout's readout, just smaller.
    /// Disappears once that player has been inside the closet.
    /// </summary>
    public class NewBadge : MonoBehaviour
    {
        const float PxW = 460f, PxH = 200f, WorldW = 1.5f;
        static readonly Color Gold = new Color(1f, 0.84f, 0.3f);

        RectTransform panel;
        Text main, sub;
        Image bg;
        RectTransform[] bits;
        Vector2[] vel;
        float[] spin, ang;
        float punch, t;
        bool showing;
        readonly System.Random rng = new System.Random(3);

        public static NewBadge Create(Transform parent, Vector3 worldPos)
        {
            var go = new GameObject("NewBadge");
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            var b = go.AddComponent<NewBadge>();
            b.Build();
            go.SetActive(false);
            return b;
        }

        void Build()
        {
            var cgo = new GameObject("Canvas", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            cgo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            panel = (RectTransform)cgo.transform;
            panel.sizeDelta = new Vector2(PxW, PxH);
            panel.localScale = Vector3.one * (WorldW / PxW);

            bg = Child("BG", panel, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            for (int i = 0; i < 10; i++)
            {
                var line = Child("Scan", panel, new Vector2(0, i / 10f), new Vector2(1, i / 10f));
                line.sizeDelta = new Vector2(0, 2);
                line.gameObject.AddComponent<Image>().color = new Color(Gold.r, Gold.g, Gold.b, 0.1f);
            }
            // confetti drifting through the hologram
            Color[] cols = { Gold, new Color(1f, 0.4f, 0.6f), new Color(0.45f, 1f, 0.95f), new Color(0.6f, 1f, 0.4f), Color.white };
            bits = new RectTransform[16];
            vel = new Vector2[bits.Length];
            spin = new float[bits.Length];
            ang = new float[bits.Length];
            for (int i = 0; i < bits.Length; i++)
            {
                var r = Child("Confetti", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                r.sizeDelta = new Vector2(10 + rng.Next(8), 5 + rng.Next(5));
                r.gameObject.AddComponent<Image>().color = cols[i % cols.Length];
                bits[i] = r;
                Respawn(i, true);
            }
            main = Popup.MakeText(Child("Main", panel, new Vector2(0, 0.42f), new Vector2(1, 1)).gameObject, "NEW!", 92, Gold);
            sub = Popup.MakeText(Child("Sub", panel, new Vector2(0, 0.04f), new Vector2(1, 0.44f)).gameObject, "", 30, Gold);
            sub.supportRichText = true;
        }

        void Respawn(int i, bool anywhere)
        {
            float x = (float)(rng.NextDouble() - 0.5) * PxW * 0.95f;
            float y = anywhere ? (float)(rng.NextDouble() - 0.5) * PxH : PxH * 0.5f + 10f;
            bits[i].anchoredPosition = new Vector2(x, y);
            vel[i] = new Vector2((float)(rng.NextDouble() - 0.5) * 40f, -40f - (float)rng.NextDouble() * 50f);
            spin[i] = (float)(rng.NextDouble() - 0.5) * 600f;
        }

        static RectTransform Child(string name, Transform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = aMin; r.anchorMax = aMax;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            return r;
        }

        /// <summary>Show (with a pop the first time) or hide. 'lines' is e.g. "P1 SHOWER CAP".</summary>
        public void Set(string lines)
        {
            bool on = !string.IsNullOrEmpty(lines);
            if (on && !showing)
            {
                gameObject.SetActive(true);
                punch = 1f;
                for (int i = 0; i < bits.Length; i++) Respawn(i, true);
                Fx.Confetti(transform.position, 30, 5f);
                Sfx.Play(Sfx.I != null ? Sfx.I.coin : null, 0.5f, 1.5f);
            }
            else if (!on && showing) gameObject.SetActive(false);
            showing = on;
            if (on) sub.text = lines;
        }

        void LateUpdate()
        {
            var cam = Game.Cam != null ? Game.Cam.Cam : Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation; // billboard

            float dt = Time.unscaledDeltaTime;
            t += dt;
            punch = Mathf.MoveTowards(punch, 0f, dt * 3f);
            float pop = 1f + Juice.OutBack(punch) * 0.25f + Mathf.Sin(t * 4f) * 0.03f;
            panel.localScale = Vector3.one * (WorldW / PxW) * pop;
            panel.localPosition = new Vector3(0f, Mathf.Sin(t * 2.2f) * 0.05f, 0f);

            // the same steady shimmer as the spout's hologram (grime makes it glitchier)
            float g = Theme.Grime;
            float flicker = g > 0.05f && rng.NextDouble() < g * 0.03 ? 0.5f : 1f;
            float a = (0.92f + 0.08f * Mathf.Sin(t * 20f)) * flicker;
            main.color = new Color(Gold.r, Gold.g, Gold.b, a);
            sub.color = new Color(1f, 1f, 1f, a * 0.9f);
            bg.color = new Color(Gold.r * 0.25f, Gold.g * 0.2f, Gold.b * 0.08f, 0.4f * flicker);

            for (int i = 0; i < bits.Length; i++)
            {
                var p = bits[i].anchoredPosition + vel[i] * dt;
                p.x += Mathf.Sin(t * 3f + i) * 12f * dt;
                bits[i].anchoredPosition = p;
                ang[i] += spin[i] * dt;
                bits[i].localRotation = Quaternion.Euler(0, 0, ang[i]);
                if (p.y < -PxH * 0.5f - 10f) Respawn(i, false);
            }
        }
    }
}
