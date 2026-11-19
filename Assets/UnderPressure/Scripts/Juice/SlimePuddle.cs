using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// The spill: a glossy 3D blob with surface tension (a low dome that bulges where it meets the floor, so
    /// there's no flat decal to z-fight), plus satellite droplets. It splats out with an overshoot, then keeps
    /// a slow jiggle like slime.
    /// </summary>
    public class SlimePuddle : MonoBehaviour
    {
        class Blob { public Transform t; public Vector3 size; public float delay, phase; }

        readonly List<Blob> blobs = new List<Blob>();
        float age;

        public static void Spawn(Vector3 pos, Vector3 flowDir)
        {
            var go = new GameObject("SlimePuddle");
            go.transform.SetParent(Game.LevelRoot, false);
            go.transform.position = new Vector3(pos.x, 0f, pos.z);
            var p = go.AddComponent<SlimePuddle>();
            p.Build(flowDir);
        }

        void Build(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            float gs = Mats.FillGlowScale;
            var mat = Mats.Lit(Mats.Liquid, 0.97f, 0.05f, Mats.LiquidGlow * 0.35f * gs);
            var rim = Mats.Lit(Color.Lerp(Mats.Liquid, Color.black, 0.25f), 0.9f, 0.05f, Mats.LiquidGlow * (0.2f + 0.3f * (1f - gs)));

            // main body, elongated along the spill direction; a darker rim underneath gives it a meniscus edge
            Add(rim, Vector3.zero, new Vector3(2.3f, 0.2f, 2.7f), dir, 0f);
            Add(mat, Vector3.up * 0.01f, new Vector3(2.1f, 0.3f, 2.5f), dir, 0f);
            Add(mat, dir * 0.35f + Vector3.up * 0.02f, new Vector3(1.2f, 0.36f, 1.3f), dir, 0.05f); // thick centre
            // satellite droplets flung further out
            var rng = new System.Random();
            for (int i = 0; i < 6; i++)
            {
                float along = 0.9f + (float)rng.NextDouble() * 1.1f;
                float across = ((float)rng.NextDouble() - 0.5f) * 2.6f;
                float s = 0.18f + (float)rng.NextDouble() * 0.32f;
                Add(mat, dir * along + side * across, new Vector3(s, s * 0.55f, s), dir, 0.08f + i * 0.04f);
            }
        }

        void Add(Material m, Vector3 offset, Vector3 size, Vector3 dir, float delay)
        {
            // sphere centred on the floor plane: the bottom half is hidden underground, the top is a dome
            var t = Prims.Make(PrimitiveType.Sphere, transform, offset, Vector3.zero, m, Quaternion.LookRotation(dir), "Blob").transform;
            t.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            blobs.Add(new Blob { t = t, size = size, delay = delay, phase = Random.value * 6f });
        }

        void Update()
        {
            age += Time.unscaledDeltaTime;
            foreach (var b in blobs)
            {
                float t = Mathf.Clamp01((age - b.delay) / 0.7f);
                float grow = Juice.OutElastic(t);
                // slow slime jiggle: flatter <-> taller, keeping volume roughly constant
                float j = Mathf.Sin(age * 3.2f + b.phase) * 0.05f * Mathf.Clamp01(age - b.delay);
                b.t.localScale = new Vector3(b.size.x * grow * (1f - j * 0.5f), b.size.y * Mathf.Clamp01(t * 1.5f) * (1f + j), b.size.z * grow * (1f - j * 0.5f));
            }
        }
    }
}
