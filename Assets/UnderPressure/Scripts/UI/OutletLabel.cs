using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// A small hologram over one of a filter's outlets once it splits: what comes out of that arm, in the liquid's own
    /// color (optionally followed by its pay multiplier, see GameConfig.filterLabelShowsMultiplier). It sits at the
    /// outlet's edge of the tile and grows away from the filter, so a filter's two labels point apart instead of
    /// stacking on each other. Pops in, faces the camera, and stays as long as the filter does.
    /// </summary>
    public class OutletLabel : MonoBehaviour
    {
        const float PxPerUnit = 280f, PxH = 64f, PadX = 22f;

        PipePiece filter;
        Vector3 offset, outDir;
        RectTransform rt;
        Text nameText;
        Image bg;
        float t;

        static float Scale => Game.Config != null ? Mathf.Max(0.1f, Game.Config.filterLabelScale) : 1f;

        public static OutletLabel Create(PipePiece filter, Dir outlet, LiquidGrade grade)
        {
            if (filter == null) return null;
            var go = new GameObject("OutletLabel", typeof(RectTransform));
            if (Game.LevelRoot != null) go.transform.SetParent(Game.LevelRoot, false);
            var l = go.AddComponent<OutletLabel>();
            l.filter = filter;
            float S = Game.Config != null ? Game.Config.tileSize : 1.6f;
            l.outDir = DirUtil.ToVector3(outlet);
            // at the tile's edge over the arm, lifted clear of the canister
            l.offset = l.outDir * S * 0.5f + Vector3.up * 0.95f;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            l.rt = (RectTransform)go.transform;

            l.bg = Child("BG", l.rt).gameObject.AddComponent<Image>();
            l.bg.raycastTarget = false;
            l.bg.color = new Color(0.05f, 0.08f, 0.1f, 0.55f);

            string label = grade.Name;
            if (Game.Config != null && Game.Config.filterLabelShowsMultiplier)
                label += $" <size=28><color=#{ColorUtility.ToHtmlStringRGB(Mats.Money)}>x{grade.ValueMultiplier:0.##}</color></size>";
            l.nameText = Popup.MakeText(Child("Name", l.rt).gameObject, label, 38, grade.TextColor);
            l.nameText.supportRichText = true;
            l.rt.sizeDelta = new Vector2(l.nameText.preferredWidth + PadX * 2f, PxH);
            l.LateUpdate();
            return l;
        }

        static RectTransform Child(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            return r;
        }

        void LateUpdate()
        {
            if (filter == null) { Destroy(gameObject); return; }
            t += Time.deltaTime;
            transform.position = filter.transform.position + offset + Vector3.up * Mathf.Sin(t * 2.5f) * 0.03f;
            var cam = Game.Cam != null ? Game.Cam.Cam : Camera.main;
            if (cam != null)
            {
                transform.rotation = cam.transform.rotation; // billboard, like the spout hologram
                // anchor the label on the filter's side so it grows outward: left/right arms grow sideways,
                // far/near arms grow up/down the screen
                float dx = Vector3.Dot(outDir, cam.transform.right);
                Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                float dz = Vector3.Dot(outDir, fwd);
                var pivot = new Vector2(0.5f, 0.5f);
                if (Mathf.Abs(dx) >= Mathf.Abs(dz)) pivot.x = dx > 0f ? 0f : 1f;
                else pivot.y = dz > 0f ? 0f : 1f;
                rt.pivot = pivot;
            }
            float pop = Juice.OutBack(Mathf.Clamp01(t / 0.35f));
            transform.localScale = Vector3.one * (Scale / PxPerUnit) * Mathf.Max(0.01f, pop);
        }
    }
}
