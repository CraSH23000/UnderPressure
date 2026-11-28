using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// A grime film over the world (under the HUD) that shows the lab's grime level, and the rings that change it:
    /// spill something clean and a bright ring washes the film back from where it landed; spill something filthy and
    /// a murky ring rolls out instead. The ring's look is exaggerated while it passes (see the shader's trail) and
    /// settles to the real change. Lives on the bootstrap, so it survives chamber rebuilds (Snap re-syncs it).
    /// Debug (editor / dev builds): F9 = clean ring at the mouse, F10 = grime ring at the mouse.
    /// </summary>
    public class GrimeOverlay : MonoBehaviour
    {
        public static GrimeOverlay I { get; private set; }

        const int MaxRings = 8;            // must match MAX_RINGS in GrimeOverlay.shader
        const float RingSpeed = 9f;        // world units per second
        const float EdgeWidth = 0.8f;      // softness of the wavefront
        const float TrailLength = 5f;      // how far behind the front the exaggeration eases off
        const float PlaneY = 0f;           // the floor

        class Wave { public Vector2 centre; public float delta, exaggeration, start; }

        readonly List<Wave> rings = new List<Wave>();
        float settled;                     // grime level everywhere no active ring has changed yet
        Material mat;
        RawImage img;
        readonly Vector4[] ringA = new Vector4[MaxRings], ringB = new Vector4[MaxRings];
        static readonly int LevelId = Shader.PropertyToID("_Level"), InvVpId = Shader.PropertyToID("_InvViewProj"),
            PlaneId = Shader.PropertyToID("_PlaneY"), CountId = Shader.PropertyToID("_RingCount"),
            RingAId = Shader.PropertyToID("_RingA"), RingBId = Shader.PropertyToID("_RingB");

        public static GrimeOverlay Create(Transform parent)
        {
            var go = new GameObject("GrimeOverlay");
            go.transform.SetParent(parent, false);
            return go.AddComponent<GrimeOverlay>();
        }

        void Awake()
        {
            I = this;
            var sh = Shader.Find("UnderPressure/GrimeOverlay");
            if (sh == null || !sh.isSupported) { Debug.LogWarning("[UnderPressure] GrimeOverlay shader missing: no grime film."); enabled = false; return; }
            mat = new Material(sh) { name = "UP_GrimeOverlay" };

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // under the HUD (100)
            var imgGo = new GameObject("Film", typeof(RectTransform));
            imgGo.transform.SetParent(transform, false);
            var r = (RectTransform)imgGo.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            img = imgGo.AddComponent<RawImage>();
            img.material = mat;
            img.raycastTarget = false;
            mat.SetVectorArray(RingAId, ringA);
            mat.SetVectorArray(RingBId, ringB);
        }

        void OnDestroy() { if (I == this) I = null; if (mat != null) Destroy(mat); }

        /// <summary>Jump straight to a grime level, no rings (a new chamber was built).</summary>
        public void Snap(float level)
        {
            settled = Mathf.Clamp01(level);
            rings.Clear();
        }

        /// <summary>
        /// A ring from 'world': 'applied' is the real change to the grime level (what's left once it has passed),
        /// 'intensity' how strong the spill was (it can be bigger than 'applied' when grime was already near 0 or 1,
        /// and it sets how extreme the passing wave looks).
        /// </summary>
        public void Ring(Vector3 world, float applied, float intensity)
        {
            if (!enabled || Mathf.Approximately(intensity, 0f)) return;
            if (rings.Count >= MaxRings) { settled = Mathf.Clamp01(settled + rings[0].delta); rings.RemoveAt(0); }
            rings.Add(new Wave
            {
                centre = new Vector2(world.x, world.z),
                delta = applied != 0f ? applied : Mathf.Sign(intensity) * 1e-4f, // keeps the ring's sign (clean / grime)
                exaggeration = Mathf.Min(0.6f, Mathf.Abs(intensity) * 1.5f + 0.15f),
                start = Time.unscaledTime, // the spill slow-mo shouldn't slow the ring
            });
        }

        void Update()
        {
            DebugKeys();
            var cam = Game.Cam != null ? Game.Cam.Cam : Camera.main;
            if (cam == null || mat == null) { if (img != null) img.enabled = false; return; }

            float reach = FarthestVisible(cam);
            int n = 0;
            for (int i = 0; i < rings.Count; i++)
            {
                var g = rings[i];
                float radius = (Time.unscaledTime - g.start) * RingSpeed;
                if (radius > reach + TrailLength * 4f + EdgeWidth)
                {
                    // it has crossed the whole view: its change is now just the level
                    settled = Mathf.Clamp01(settled + g.delta);
                    rings.RemoveAt(i--);
                    continue;
                }
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(reach, reach + TrailLength * 4f, radius));
                ringA[n] = new Vector4(g.centre.x, g.centre.y, radius, EdgeWidth);
                ringB[n] = new Vector4(g.delta, g.exaggeration, TrailLength, fade);
                n++;
            }

            bool show = settled > 0.005f || n > 0;
            img.enabled = show;
            if (!show) return;
            mat.SetFloat(LevelId, settled);
            mat.SetMatrix(InvVpId, (cam.projectionMatrix * cam.worldToCameraMatrix).inverse);
            mat.SetFloat(PlaneId, PlaneY);
            mat.SetInt(CountId, n);
            mat.SetVectorArray(RingAId, ringA);
            mat.SetVectorArray(RingBId, ringB);
        }

        /// <summary>How far from any ring's centre the view reaches on the floor (the ring is done past that).</summary>
        float FarthestVisible(Camera cam)
        {
            float far = 0f;
            foreach (var g in rings)
                foreach (var v in new[] { new Vector3(0, 0), new Vector3(1, 0), new Vector3(0, 1), new Vector3(1, 1) })
                    if (FloorPoint(cam.ViewportPointToRay(v), out var p))
                        far = Mathf.Max(far, Vector2.Distance(g.centre, new Vector2(p.x, p.z)));
            return Mathf.Max(far, 10f);
        }

        static bool FloorPoint(Ray ray, out Vector3 p)
        {
            p = default;
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = (PlaneY - ray.origin.y) / ray.direction.y;
            if (t < 0f) return false;
            p = ray.origin + ray.direction * t;
            return true;
        }

        void DebugKeys()
        {
            if (!Debug.isDebugBuild) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (kb == null || mouse == null) return;
            bool clean = kb.f9Key.wasPressedThisFrame, dirty = kb.f10Key.wasPressedThisFrame;
            if (!clean && !dirty) return;
            var cam = Game.Cam != null ? Game.Cam.Cam : Camera.main;
            if (cam == null || !FloorPoint(cam.ScreenPointToRay(mouse.position.ReadValue()), out var at)) return;
            float want = clean ? -0.3f : 0.3f;
            float applied = Game.Bootstrap != null ? Game.Bootstrap.AddGrime(want) : want;
            Ring(at, applied, want);
            Debug.Log($"[UnderPressure] Debug grime ring {(clean ? "clean" : "grime")} at {at:F1}: grime now {(Game.Bootstrap != null ? Game.Bootstrap.CurrentGrime : settled + applied):0.00}");
        }
    }
}
