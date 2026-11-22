using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// The slime leaves a trail of goo in its own color: flat blobs dropped on the floor as it moves, which shrink
    /// away after a few seconds. Lives on the rig; the blobs live in the level so they stay where they were dropped.
    /// </summary>
    public class SlimeTrail : MonoBehaviour
    {
        const int MaxBlobs = 70;
        const float Life = 5f;

        Material mat;
        float size;
        Vector3 lastDrop;
        bool hasLast;
        readonly List<(Transform t, float born, float s)> blobs = new List<(Transform, float, float)>();

        public void Init(Color tint, float footprint)
        {
            mat = Mats.Transparent(new Color(tint.r * 0.9f, tint.g * 0.9f, tint.b * 0.9f, 0.45f));
            size = Mathf.Clamp(footprint, 0.25f, 0.9f);
        }

        void Update()
        {
            if (mat == null) return;
            float now = Time.time;
            Vector3 p = transform.position;
            // only on the floor (not riding high on pipes, mid-hop or vanished)
            bool grounded = p.y < 0.05f && transform.lossyScale.y > 0.5f;
            if (!hasLast) { lastDrop = p; hasLast = true; }
            Vector3 flat = p - lastDrop; flat.y = 0f;
            if (grounded && flat.magnitude > size * 0.45f)
            {
                lastDrop = p;
                float s = size * Random.Range(0.75f, 1.05f);
                var go = Prims.Make(PrimitiveType.Cylinder, Game.LevelRoot, new Vector3(p.x, 0.009f + blobs.Count * 0.00002f, p.z),
                    new Vector3(s, 0.004f, s * Random.Range(0.8f, 1.1f)), mat, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), "SlimeTrail");
                blobs.Add((go.transform, now, s));
                if (blobs.Count > MaxBlobs) { if (blobs[0].t != null) Destroy(blobs[0].t.gameObject); blobs.RemoveAt(0); }
            }
            // older blobs dry up and shrink away
            for (int i = blobs.Count - 1; i >= 0; i--)
            {
                var b = blobs[i];
                if (b.t == null) { blobs.RemoveAt(i); continue; }
                float age = (now - b.born) / Life;
                if (age >= 1f) { Destroy(b.t.gameObject); blobs.RemoveAt(i); continue; }
                float k = age < 0.6f ? 1f : 1f - (age - 0.6f) / 0.4f;
                var sc = b.t.localScale;
                b.t.localScale = new Vector3(b.s * k, sc.y, b.s * k * (sc.z / Mathf.Max(0.0001f, sc.x)));
            }
        }

        void OnDestroy()
        {
            foreach (var b in blobs) if (b.t != null) Destroy(b.t.gameObject);
        }
    }
}
