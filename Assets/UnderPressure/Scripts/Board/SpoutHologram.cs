using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// A holographic readout projected above the dispenser: a big countdown (or the leak warning / result) over the
    /// name of the liquid in its own color. It sits where your eyes already are, so the HUD doesn't have to shout.
    /// Always faces the camera. The distance goal lives elsewhere (it isn't the spout's business).
    /// </summary>
    public class SpoutHologram : MonoBehaviour
    {
        const float PxW = 640f, PxH = 340f, WorldW = 3.3f;
        // GameConfig.spoutHologramScale, read live so it can be tuned in play mode
        static float Scale => Game.Config != null ? Mathf.Max(0.1f, Game.Config.spoutHologramScale) : 1f;
        static readonly Color Holo = new Color(0.45f, 1f, 0.95f);

        Transform panel;
        Text main, sub, info;
        RectTransform mainRect, subRect, infoRect;
        Image bg;
        Material beamMat;
        float punch, flicker;
        Color tint = Holo;
        Color? subColor;
        bool subOnly;
        readonly System.Random rng = new System.Random(9);

        public static SpoutHologram Create(Spout spout)
        {
            var go = new GameObject("SpoutHologram");
            go.transform.SetParent(spout.transform, false);
            go.transform.position = spout.CenterTop + Vector3.up * 1.35f;
            var h = go.AddComponent<SpoutHologram>();
            h.Build(spout);
            return h;
        }

        void Build(Spout spout)
        {
            // projector beam from the top of the tank up to the display
            beamMat = Mats.Transparent(new Color(Holo.r, Holo.g, Holo.b, 0.08f));
            var beam = Prims.Make(PrimitiveType.Cylinder, spout.transform, transform.localPosition - Vector3.up * 0.7f, new Vector3(0.5f, 0.6f, 0.5f), beamMat, null, "HoloBeam");
            beam.transform.position = spout.CenterTop + Vector3.up * 0.65f;
            Prims.Make(PrimitiveType.Cylinder, spout.transform, Vector3.zero, new Vector3(0.18f, 0.04f, 0.18f),
                Mats.Lit(Holo, 0.8f, 0f, Holo * 1.5f), null, "HoloEmitter").transform.position = spout.CenterTop + Vector3.up * 0.02f;

            var cgo = new GameObject("Canvas", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            cgo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            var rt = (RectTransform)cgo.transform;
            rt.sizeDelta = new Vector2(PxW, PxH);
            rt.localScale = Vector3.one * (WorldW / PxW) * Scale;
            panel = rt;

            bg = Child("BG", rt, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            for (int i = 0; i < 18; i++)
            {
                var line = Child("Scan", rt, new Vector2(0, i / 18f), new Vector2(1, i / 18f));
                line.sizeDelta = new Vector2(0, 2);
                line.gameObject.AddComponent<Image>().color = new Color(Holo.r, Holo.g, Holo.b, 0.08f);
            }
            mainRect = Child("Main", rt, Vector2.zero, Vector2.one);
            main = Popup.MakeText(mainRect.gameObject, "", MainSize, Holo);
            subRect = Child("Sub", rt, Vector2.zero, Vector2.one);
            sub = Popup.MakeText(subRect.gameObject, "", SubSize, Holo);
            infoRect = Child("Info", rt, Vector2.zero, Vector2.one);
            info = Popup.MakeText(infoRect.gameObject, "", InfoSize, Holo);
            foreach (var t in new[] { main, sub, info }) t.supportRichText = true;
            Layout(false);
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

        const int MainSize = 150, SubSize = 54, SubOnlySize = 72, InfoSize = 38;

        /// <summary>Three rows: big main text, the liquid's name, and a small info line. Without main text the
        /// name grows and the two remaining rows share the panel.</summary>
        void Layout(bool nameOnly)
        {
            subOnly = nameOnly;
            void Span(RectTransform r, float y0, float y1) { r.anchorMin = new Vector2(0, y0); r.anchorMax = new Vector2(1, y1); }
            if (nameOnly)
            {
                Span(mainRect, 1f, 1f);
                Span(subRect, 0.42f, 0.92f);
                Span(infoRect, 0.1f, 0.42f);
                sub.fontSize = SubOnlySize;
            }
            else
            {
                Span(mainRect, 0.42f, 1f);
                Span(subRect, 0.2f, 0.46f);
                Span(infoRect, 0.02f, 0.22f);
                sub.fontSize = SubSize;
            }
        }

        /// <summary>
        /// Update the readout. 'danger' tints it red (last seconds of the countdown, leak warning). 'subTint' colors
        /// the second line (the liquid's own color); null = the hologram's. With no main text, the second line
        /// takes the whole panel.
        /// </summary>
        public void Set(string mainText, string subText, bool danger, Color? subTint = null, string infoText = "")
        {
            if (main.text != mainText) { main.text = mainText; punch = 1f; }
            sub.text = subText;
            info.text = infoText ?? "";
            subColor = subTint;
            tint = danger ? new Color(1f, 0.35f, 0.3f) : Holo;
            bool only = string.IsNullOrEmpty(mainText);
            if (only != subOnly) Layout(only);
        }

        /// <summary>The name of the chamber's liquid, and a readable text color for it (very dark liquids like VOID
        /// use their glow instead, or they'd vanish on the dark panel).</summary>
        public static string LiquidName => LiquidGrade.Source.Name;
        public static Color LiquidTextColor => LiquidGrade.Source.TextColor;

        public void Hide() => gameObject.SetActive(false);

        void LateUpdate()
        {
            var cam = Game.Cam != null ? Game.Cam.Cam : Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation; // billboard

            float dt = Time.unscaledDeltaTime;
            punch = Mathf.MoveTowards(punch, 0f, dt * 5f);
            // everything "unstable" about the projection scales with grime: a clean lab has a clean hologram
            float g = Theme.Grime;
            panel.localScale = Vector3.one * (WorldW / PxW) * Scale * (1f + punch * Mathf.Lerp(0.04f, 0.14f, g));
            // grow upward from the bottom edge, so a bigger panel doesn't sink into the tank
            panel.localPosition = Vector3.up * (WorldW * PxH / PxW * 0.5f * (Scale - 1f));

            // shimmer + the occasional glitch dropout: none at grime 0, frequent at grime 1
            flicker = g > 0.05f && rng.NextDouble() < g * 0.04 ? Mathf.Lerp(0.85f, 0.35f, g) : Mathf.MoveTowards(flicker, 1f, dt * 6f);
            float shimmer = Mathf.Lerp(0.03f, 0.15f, g);
            float a = (1f - shimmer + shimmer * Mathf.Sin(Time.unscaledTime * 20f)) * flicker;
            main.color = new Color(tint.r, tint.g, tint.b, a);
            Color sc = subColor ?? tint;
            sub.color = new Color(sc.r, sc.g, sc.b, a * (subColor.HasValue ? 1f : 0.85f));
            info.color = new Color(tint.r, tint.g, tint.b, a * 0.8f);
            bg.color = new Color(tint.r * 0.2f, tint.g * 0.25f, tint.b * 0.25f, 0.35f * flicker);
            Mats.SetBase(beamMat, new Color(tint.r, tint.g, tint.b, 0.07f * flicker));
        }
    }
}
