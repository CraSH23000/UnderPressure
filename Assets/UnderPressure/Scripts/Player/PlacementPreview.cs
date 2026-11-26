using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>Target-tile frame + translucent ghost of the carried piece, so you can see what Interact will do.</summary>
    public class PlacementPreview : MonoBehaviour
    {
        public static readonly Color Good = new Color(0.35f, 1f, 0.5f);
        public static readonly Color Neutral = new Color(0.95f, 0.95f, 1f);
        public static readonly Color Swap = new Color(1f, 0.6f, 0.15f);
        public static readonly Color Bad = new Color(1f, 0.25f, 0.2f);

        Transform frame;
        Material frameMat, ghostMat;
        readonly Dictionary<PipeType, Transform> ghosts = new Dictionary<PipeType, Transform>();
        Transform activeGhost;
        Vector3 framePos;
        bool visible;

        public void Build(float S)
        {
            frameMat = new Material(Mats.Lit(Color.white, 0.2f, 0f, Color.white));
            frame = Prims.Empty("Frame", transform, Vector3.zero).transform;
            float half = S * 0.5f - 0.04f, t = 0.08f;
            Prims.Make(PrimitiveType.Cube, frame, new Vector3(0, 0.05f, half), new Vector3(S - 0.08f, 0.05f, t), frameMat);
            Prims.Make(PrimitiveType.Cube, frame, new Vector3(0, 0.05f, -half), new Vector3(S - 0.08f, 0.05f, t), frameMat);
            Prims.Make(PrimitiveType.Cube, frame, new Vector3(half, 0.05f, 0), new Vector3(t, 0.05f, S - 0.08f), frameMat);
            Prims.Make(PrimitiveType.Cube, frame, new Vector3(-half, 0.05f, 0), new Vector3(t, 0.05f, S - 0.08f), frameMat);

            ghostMat = Mats.Transparent(new Color(1, 1, 1, 0.35f));
            foreach (PipeType type in System.Enum.GetValues(typeof(PipeType)))
            {
                var root = Prims.Empty("Ghost_" + type, transform, Vector3.zero).transform;
                var model = PipeBuilder.Build(type, root, S, ghostMat);
                model.baseRoot.gameObject.SetActive(false);
                foreach (var st in model.staples) st.gameObject.SetActive(false);
                root.gameObject.SetActive(false);
                ghosts[type] = root;
            }
            Hide();
        }

        public void Show(Vector3 cellCenter, Color color, bool showGhost, PipeType type, int rotation)
        {
            visible = true;
            frame.gameObject.SetActive(true);
            if ((framePos - cellCenter).sqrMagnitude > 0.01f)
            {
                framePos = cellCenter;
                frame.localScale = Vector3.one * 1.15f; // pop when the target changes
            }
            frame.position = cellCenter;
            Mats.SetBase(frameMat, color);
            frameMat.SetColor("_EmissionColor", color * 0.9f);

            var g = showGhost ? ghosts[type] : null;
            if (activeGhost != g)
            {
                if (activeGhost != null) activeGhost.gameObject.SetActive(false);
                activeGhost = g;
                if (g != null) g.gameObject.SetActive(true);
            }
            if (g != null)
            {
                g.position = cellCenter + Vector3.up * (0.04f + 0.04f * Mathf.Sin(Time.time * 6f));
                g.rotation = Quaternion.Slerp(g.rotation, Quaternion.Euler(0, 90f * rotation, 0), 1f - Mathf.Exp(-25f * Time.deltaTime));
                var cfg = Game.Config;
                var c = color; c.a = cfg != null ? cfg.previewOpacity : 0.7f;
                Mats.SetBase(ghostMat, c);
                // glow only as much as the color is saturated: a white glow flattens the tube into a blob on the white
                // floor, while lit shading shows its bends and openings (and so how it's rotated)
                Color.RGBToHSV(color, out _, out float sat, out _);
                ghostMat.SetColor("_EmissionColor", color * (cfg != null ? cfg.previewGlow : 0.6f) * Mathf.Lerp(0.15f, 1f, sat));
            }
        }

        public void Hide()
        {
            if (!visible && frame != null && !frame.gameObject.activeSelf) return;
            visible = false;
            frame.gameObject.SetActive(false);
            if (activeGhost != null) activeGhost.gameObject.SetActive(false);
            activeGhost = null;
            framePos = new Vector3(9999, 0, 9999);
        }

        void Update()
        {
            if (!visible) return;
            float pulse = 1f + 0.03f * Mathf.Sin(Time.time * 9f);
            frame.localScale = Vector3.Lerp(frame.localScale, Vector3.one * pulse, 1f - Mathf.Exp(-18f * Time.deltaTime));
        }
    }
}
