using System;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// A little elevator that brings a critter up through the floor: a hatch slides open, a pad rises in a beam
    /// of the player's color, the critter materialises on it, then the pad sinks and the hatch shuts.
    /// Used for joining, arriving in a new chamber and coming back after falling in the pit.
    /// </summary>
    public class ArrivalPad : MonoBehaviour
    {
        public const float MaterializeAt = 0.55f, ReleaseAt = 1.0f, Duration = 1.6f;
        const float PadDown = -0.2f, PadUp = 0.1f, PadH = 0.12f;

        Transform pad, doorL, doorR, beam;
        Material beamMat;
        Color color;
        float t;
        bool materialized, released;
        Action onMaterialize, onRelease;

        /// <summary>Height of the pad's top surface right now (the critter stands on it).</summary>
        public float Top => pad != null ? pad.localPosition.y + PadH * 0.5f : 0f;

        public static ArrivalPad Spawn(Vector3 pos, Color color, Action onMaterialize, Action onRelease)
        {
            var go = new GameObject("ArrivalPad");
            if (Game.LevelRoot != null) go.transform.SetParent(Game.LevelRoot, false);
            go.transform.position = new Vector3(pos.x, 0f, pos.z);
            var a = go.AddComponent<ArrivalPad>();
            a.color = color;
            a.onMaterialize = onMaterialize;
            a.onRelease = onRelease;
            a.Build();
            return a;
        }

        void Build()
        {
            var t0 = transform;
            var metal = Mats.Lit(new Color(0.62f, 0.64f, 0.68f), 0.8f, 0.9f);
            var dark = Mats.Lit(new Color(0.02f, 0.02f, 0.03f), 0.1f);
            var trim = Mats.Lit(Theme.WallTrim, 0.5f, 0.5f);
            // the hole and its rim
            Prims.Make(PrimitiveType.Cylinder, t0, new Vector3(0, 0.008f, 0), new Vector3(1.15f, 0.004f, 1.15f), dark, null, "Hole");
            Prims.Make(PrimitiveType.Cylinder, t0, new Vector3(0, 0.006f, 0), new Vector3(1.4f, 0.004f, 1.4f), trim, null, "Rim");
            // the pad itself, with a glowing ring in the player's color
            pad = Prims.Empty("Pad", t0, new Vector3(0, PadDown, 0)).transform;
            Prims.Make(PrimitiveType.Cylinder, pad, Vector3.zero, new Vector3(1.05f, PadH * 0.5f, 1.05f), metal, null, "Deck");
            Prims.Make(PrimitiveType.Cylinder, pad, new Vector3(0, PadH * 0.5f + 0.003f, 0), new Vector3(0.9f, 0.004f, 0.9f),
                Mats.Lit(color, 0.6f, 0f, color * 2f), null, "Ring");
            Prims.Make(PrimitiveType.Cylinder, pad, new Vector3(0, PadH * 0.5f + 0.006f, 0), new Vector3(0.74f, 0.004f, 0.74f), metal, null, "Plate");
            Prims.Make(PrimitiveType.Cylinder, pad, new Vector3(0, -0.6f, 0), new Vector3(0.8f, 0.55f, 0.8f), trim, null, "Ram");
            // hatch doors slide apart
            doorL = Prims.Make(PrimitiveType.Cube, t0, new Vector3(-0.29f, 0.014f, 0), new Vector3(0.58f, 0.012f, 1.15f), trim, null, "DoorL").transform;
            doorR = Prims.Make(PrimitiveType.Cube, t0, new Vector3(0.29f, 0.014f, 0), new Vector3(0.58f, 0.012f, 1.15f), trim, null, "DoorR").transform;
            // beam of light
            beamMat = Mats.Transparent(new Color(color.r, color.g, color.b, 0f));
            beam = Prims.Make(PrimitiveType.Cylinder, t0, new Vector3(0, 1.3f, 0), new Vector3(0.95f, 1.3f, 0.95f), beamMat, null, "Beam").transform;
            Sfx.Play(Sfx.I != null ? Sfx.I.clunk : null, 0.5f, 0.8f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            t += dt;

            // doors: open 0..0.25, close 1.3..1.5
            float open = t < 1.3f ? Mathf.Clamp01(t / 0.25f) : 1f - Mathf.Clamp01((t - 1.3f) / 0.2f);
            float o = Juice.OutCubic(open);
            doorL.localScale = new Vector3(0.58f * (1f - o), 0.012f, 1.15f);
            doorR.localScale = doorL.localScale;
            doorL.localPosition = new Vector3(-0.29f - o * 0.29f, 0.014f, 0);
            doorR.localPosition = new Vector3(0.29f + o * 0.29f, 0.014f, 0);
            bool doorsShowing = o < 0.99f;
            doorL.gameObject.SetActive(doorsShowing);
            doorR.gameObject.SetActive(doorsShowing);

            // pad: up 0.15..0.55, down 1.0..1.4
            float y;
            if (t < ReleaseAt) y = Mathf.LerpUnclamped(PadDown, PadUp, Juice.OutBack(Mathf.Clamp01((t - 0.15f) / 0.4f)));
            else y = Mathf.Lerp(PadUp, PadDown - 0.1f, Juice.InCubic(Mathf.Clamp01((t - ReleaseAt) / 0.4f)));
            pad.localPosition = new Vector3(0, y, 0);

            // beam swells in, flares at the materialise, then fades
            float a = t < MaterializeAt ? Mathf.Clamp01(t / MaterializeAt) * 0.35f : Mathf.Max(0f, 0.6f - (t - MaterializeAt) * 1.2f);
            Mats.SetBase(beamMat, new Color(color.r, color.g, color.b, a));
            float w = t < MaterializeAt ? 0.95f : Mathf.Lerp(0.95f, 0.2f, Mathf.Clamp01((t - MaterializeAt) / 0.5f));
            beam.localScale = new Vector3(w, 1.3f, w);
            beam.gameObject.SetActive(a > 0.01f);

            if (!materialized && t >= MaterializeAt)
            {
                materialized = true;
                Sfx.Play(Sfx.I != null ? Sfx.I.materialize : null, 0.7f, 1.1f, 0.08f);
                onMaterialize?.Invoke();
            }
            if (!released && t >= ReleaseAt)
            {
                released = true;
                Sfx.Play(Sfx.I != null ? Sfx.I.clunk : null, 0.35f, 1.1f);
                onRelease?.Invoke();
            }
            if (t >= Duration) Destroy(gameObject);
        }
    }
}
