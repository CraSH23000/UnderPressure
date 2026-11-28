using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// A belt that's scenery you can ride: the same look as the real one, running flat out along a path with square
    /// corners, with nothing on it. It comes up out of a floor chute and ends where its path ends (no pieces, no pit,
    /// no cost), but it carries anyone who steps on it.
    /// </summary>
    public class DecorBelt : MonoBehaviour
    {
        static readonly List<DecorBelt> all = new List<DecorBelt>();

        Vector3[] pts;
        float[] cum;
        float total, speed, offset, halfWidth;
        readonly List<(Transform t, float s0)> slats = new List<(Transform, float)>();
        const float SlatPitch = 0.5f;

        /// <param name="path">Centreline, from the chute to the end (y ignored); each turn is a corner.</param>
        public static DecorBelt Create(Transform parent, Vector3[] path, float halfWidth, float speed)
        {
            var go = new GameObject("DecorBelt");
            go.transform.SetParent(parent, false);
            var belt = go.AddComponent<DecorBelt>();
            belt.Build(path, halfWidth, speed);
            return belt;
        }

        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDisable() => all.Remove(this);

        /// <summary>How fast the belts here would carry someone standing at p (zero if not on one).</summary>
        public static Vector3 CarryAt(Vector3 p)
        {
            foreach (var b in all)
            {
                var v = b.SurfaceVelocity(p);
                if (v != Vector3.zero) return v;
            }
            return Vector3.zero;
        }

        void Build(Vector3[] path, float hw, float spd)
        {
            pts = new Vector3[path.Length];
            for (int i = 0; i < path.Length; i++) pts[i] = new Vector3(path[i].x, 0f, path[i].z);
            cum = new float[pts.Length];
            for (int i = 1; i < pts.Length; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            total = cum[pts.Length - 1];
            speed = spd; halfWidth = hw;
            float w = hw * 2f;

            var frame = Mats.Lit(Color.Lerp(Theme.Wall, Theme.WallTrim, 0.15f), 0.7f, 0.2f);
            var beltMat = Mats.Lit(Theme.Ink, 0.25f);
            var slatMat = Mats.Lit(Color.Lerp(Theme.Ink, Theme.WallTrim, 0.6f), 0.35f, 0.3f);
            var stripe = Mats.Lit(Theme.AccentOrange, 0.5f, 0f, Theme.AccentOrange * 0.4f);

            // straight runs, overlapping into square corners (like the real belt)
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                Vector3 dir = (b - a).normalized;
                float len = Vector3.Distance(a, b) + (i > 0 ? hw : 0f) + (i < pts.Length - 2 ? hw : 0f);
                Vector3 mid = (a + b) * 0.5f + dir * (((i < pts.Length - 2) ? hw : 0f) - (i > 0 ? hw : 0f)) * 0.5f;
                var rot = Quaternion.LookRotation(dir);
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                Prims.Make(PrimitiveType.Cube, transform, mid + Vector3.up * 0.002f, new Vector3(w, 0.02f, len), beltMat, rot, "BeltBed");
                for (int s = -1; s <= 1; s += 2)
                    Prims.Make(PrimitiveType.Cube, transform, mid + Vector3.up * 0.006f + side * s * (hw + 0.06f), new Vector3(0.12f, 0.03f, len + 0.12f), frame, rot, "Edge");
                for (float d = 0.9f; d < Vector3.Distance(a, b) - 0.6f; d += 1.2f)
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 p = a + dir * d + side * s * (hw - 0.2f) + Vector3.up * 0.016f;
                        Prims.Make(PrimitiveType.Cube, transform, p - side * 0.08f, new Vector3(0.06f, 0.01f, 0.3f), stripe, rot * Quaternion.Euler(0, 35f, 0), "Chevron");
                        Prims.Make(PrimitiveType.Cube, transform, p + side * 0.08f, new Vector3(0.06f, 0.01f, 0.3f), stripe, rot * Quaternion.Euler(0, -35f, 0), "Chevron");
                    }
            }
            // an end stop
            {
                Vector3 dir = (pts[pts.Length - 1] - pts[pts.Length - 2]).normalized;
                Prims.Make(PrimitiveType.Cube, transform, pts[pts.Length - 1] + Vector3.up * 0.006f + dir * 0.06f, new Vector3(w + 0.24f, 0.03f, 0.12f), frame, Quaternion.LookRotation(dir), "EndEdge");
            }
            for (int i = 0; i < Mathf.CeilToInt(total / SlatPitch); i++)
                slats.Add((Prims.Make(PrimitiveType.Cube, transform, Vector3.zero, new Vector3(w - 0.1f, 0.01f, SlatPitch * 0.25f), slatMat, null, "Slat").transform, i * SlatPitch));

            // the chute it comes up out of: a dark mouth in the floor
            {
                Vector3 dir = (pts[1] - pts[0]).normalized;
                Prims.Make(PrimitiveType.Cube, transform, pts[0] - dir * 0.3f + Vector3.up * 0.008f, new Vector3(w + 0.1f, 0.012f, 0.6f), Mats.Lit(Theme.Ink, 0.1f), Quaternion.LookRotation(dir), "Chute");
            }

            Place();
        }

        int SegmentAt(float s)
        {
            for (int i = 0; i < pts.Length - 2; i++) if (s < cum[i + 1]) return i;
            return pts.Length - 2;
        }

        Vector3 PointAt(float s, out Vector3 dir)
        {
            int i = SegmentAt(s);
            float segLen = cum[i + 1] - cum[i];
            dir = (pts[i + 1] - pts[i]).normalized;
            return Vector3.Lerp(pts[i], pts[i + 1], segLen > 0f ? Mathf.Clamp01((s - cum[i]) / segLen) : 0f);
        }

        /// <summary>Belt velocity under p: the nearest run's direction, if p is on the belt.</summary>
        Vector3 SurfaceVelocity(Vector3 p)
        {
            float best = float.MaxValue;
            Vector3 bestDir = Vector3.zero;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector3 a = pts[i], ab = pts[i + 1] - a;
                Vector3 ap = p - a; ap.y = 0f;
                float t = Vector3.Dot(ap, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude);
                // corners count as part of both runs; past the very ends is off the belt
                float ext = halfWidth / Mathf.Max(0.0001f, ab.magnitude);
                if (t < (i == 0 ? 0f : -ext) || t > (i == pts.Length - 2 ? 1f : 1f + ext)) continue;
                Vector3 q = a + ab * Mathf.Clamp01(t);
                Vector3 d = p - q; d.y = 0f;
                float m = d.magnitude;
                if (m < best) { best = m; bestDir = ab.normalized; }
            }
            return best < halfWidth - 0.05f ? bestDir * speed : Vector3.zero;
        }

        void Update()
        {
            offset += speed * Time.deltaTime;
            Place();
        }

        void Place()
        {
            foreach (var (t, s0) in slats)
            {
                var p = PointAt(Mathf.Repeat(s0 + offset, total), out var dir);
                t.SetPositionAndRotation(p + Vector3.up * 0.017f, Quaternion.LookRotation(dir));
            }
        }
    }
}
