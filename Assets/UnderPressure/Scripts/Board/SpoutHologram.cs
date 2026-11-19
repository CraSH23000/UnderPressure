using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// A holographic readout projected above the dispenser: countdown / flow state, flow speed, and distance.
    /// It sits where your eyes already are, so the HUD doesn't have to shout. Always faces the camera.
    /// </summary>
    public class SpoutHologram : MonoBehaviour
    {
        const float PxW = 640f, PxH = 300f, WorldW = 2.8f;
        static readonly Color Holo = new Color(0.45f, 1f, 0.95f);

        Transform panel;
        Text main, sub, dist;
        Image bg, bar, barBg;
        RectTransform barRect;
        Material beamMat;
        float punch, flicker;
        Color tint = Holo;
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
            rt.localScale = Vector3.one * (WorldW / PxW);
            panel = rt;

            bg = Child("BG", rt, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            for (int i = 0; i < 18; i++)
            {
                var line = Child("Scan", rt, new Vector2(0, i / 18f), new Vector2(1, i / 18f));
                line.sizeDelta = new Vector2(0, 2);
                line.gameObject.AddComponent<Image>().color = new Color(Holo.r, Holo.g, Holo.b, 0.08f);
            }
            main = Popup.MakeText(Child("Main", rt, new Vector2(0, 0.5f), new Vector2(1, 1)).gameObject, "", 96, Holo);
            sub = Popup.MakeText(Child("Sub", rt, new Vector2(0, 0.3f), new Vector2(1, 0.52f)).gameObject, "", 40, Holo);
            barRect = Child("BarBG", rt, new Vector2(0.08f, 0.08f), new Vector2(0.62f, 0.22f));
            barBg = barRect.gameObject.AddComponent<Image>();
            var fill = Child("Fill", barRect, Vector2.zero, new Vector2(0, 1));
            bar = fill.gameObject.AddComponent<Image>();
            dist = Popup.MakeText(Child("Dist", rt, new Vector2(0.64f, 0.02f), new Vector2(0.97f, 0.28f)).gameObject, "", 40, Holo, TextAnchor.MiddleRight);
            foreach (var t in new[] { main, sub, dist }) t.supportRichText = true;
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

        /// <summary>Update the readout. 'danger' tints it red (last seconds of the countdown, leak warning).</summary>
        public void Set(string mainText, string subText, int distance, int goal, bool danger)
        {
            if (main.text != mainText) { main.text = mainText; punch = 1f; }
            sub.text = subText;
            dist.text = distance >= goal ? $"{distance}  <color=#FFD640>GOAL</color>" : $"{distance} / {goal}";
            ((RectTransform)bar.transform).anchorMax = new Vector2(Mathf.Clamp01(distance / (float)Mathf.Max(1, goal)), 1f);
            tint = danger ? new Color(1f, 0.35f, 0.3f) : Holo;
            bar.color = distance >= goal ? Mats.Money : new Color(tint.r, tint.g, tint.b, 0.9f);
        }

        public void Hide() => gameObject.SetActive(false);

        void LateUpdate()
        {
            var cam = Game.Cam != null ? Game.Cam.Cam : Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation; // billboard

            float dt = Time.unscaledDeltaTime;
            punch = Mathf.MoveTowards(punch, 0f, dt * 5f);
            // everything "unstable" about the projection scales with grime: a clean lab has a clean hologram
            float g = Theme.Grime;
            panel.localScale = Vector3.one * (WorldW / PxW) * (1f + punch * Mathf.Lerp(0.04f, 0.14f, g));

            // shimmer + the occasional glitch dropout: none at grime 0, frequent at grime 1
            flicker = g > 0.05f && rng.NextDouble() < g * 0.04 ? Mathf.Lerp(0.85f, 0.35f, g) : Mathf.MoveTowards(flicker, 1f, dt * 6f);
            float shimmer = Mathf.Lerp(0.03f, 0.15f, g);
            float a = (1f - shimmer + shimmer * Mathf.Sin(Time.unscaledTime * 20f)) * flicker;
            main.color = new Color(tint.r, tint.g, tint.b, a);
            sub.color = new Color(tint.r, tint.g, tint.b, a * 0.85f);
            dist.color = new Color(tint.r, tint.g, tint.b, a * 0.9f);
            bg.color = new Color(tint.r * 0.2f, tint.g * 0.25f, tint.b * 0.25f, 0.35f * flicker);
            barBg.color = new Color(tint.r, tint.g, tint.b, 0.15f);
            Mats.SetBase(beamMat, new Color(tint.r, tint.g, tint.b, 0.07f * flicker));
        }
    }
}
