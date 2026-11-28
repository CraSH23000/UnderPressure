using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// A big floor lever that sets the belt speed. The arm's angle shows the current step, and a row of lamps
    /// on the base lights up to match. Grabbed and worked like the flow valve (Use toggles, rotate changes).
    /// </summary>
    public class BeltLever : MonoBehaviour
    {
        /// <summary>Half its depth front to back (its collider), so it can be set right up against things.</summary>
        public const float HalfDepth = 0.35f;

        Transform arm;
        Renderer[] lamps;
        Material lampOn, lampOff, lampHot;
        float angle, angleVel;
        public Vector3 GuideAnchor => transform.position + Vector3.up * 2.1f;

        public static BeltLever Create(Transform parent, Vector3 pos, Vector3 facing)
        {
            var go = new GameObject("BeltLever");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            var l = go.AddComponent<BeltLever>();
            l.Build();
            return l;
        }

        void Build()
        {
            var ink = Mats.Lit(Theme.Ink, 0.5f, 0.4f);
            var body = Mats.Lit(Color.Lerp(Theme.Wall, Theme.WallTrim, 0.25f), 0.6f, 0.2f);
            var yellow = Mats.Lit(Theme.Hazard, 0.4f);
            var knob = Mats.Lit(Theme.AccentOrange, 0.8f, 0f, Theme.AccentOrange * 0.5f);
            lampOff = Mats.Lit(new Color(0.15f, 0.15f, 0.17f), 0.6f);
            lampOn = Mats.Lit(Theme.AccentBlue, 0.8f, 0f, Theme.AccentBlue * 1.8f);
            lampHot = Mats.Lit(Mats.Danger, 0.8f, 0f, Mats.Danger * 1.8f);

            // base housing with hazard trim
            Prims.Make(PrimitiveType.Cube, transform, new Vector3(0, 0.3f, 0), new Vector3(0.8f, 0.6f, 0.6f), body, null, "Base");
            Prims.Make(PrimitiveType.Cube, transform, new Vector3(0, 0.05f, 0), new Vector3(0.95f, 0.1f, 0.75f), ink, null, "Plinth");
            for (int i = 0; i < 4; i++)
                Prims.Make(PrimitiveType.Cube, transform, new Vector3(-0.3f + i * 0.2f, 0.12f, 0.38f), new Vector3(0.1f, 0.06f, 0.01f), yellow, Quaternion.Euler(0, 0, 35f));
            // slot the arm travels in
            Prims.Make(PrimitiveType.Cube, transform, new Vector3(0, 0.605f, 0), new Vector3(0.6f, 0.02f, 0.12f), ink, null, "Slot");
            // painted end marks: blue (slow) on the left, red (fast) on the right
            Prims.Make(PrimitiveType.Cube, transform, new Vector3(-0.34f, 0.606f, 0), new Vector3(0.06f, 0.02f, 0.2f), Mats.Lit(Theme.AccentBlue, 0.5f), null, "SlowMark");
            Prims.Make(PrimitiveType.Cube, transform, new Vector3(0.34f, 0.606f, 0), new Vector3(0.06f, 0.02f, 0.2f), Mats.Lit(Mats.Danger, 0.5f), null, "FastMark");

            // arm pivots at the top of the base, swinging left (slow) to right (fast) as seen from the camera
            arm = Prims.Empty("Arm", transform, new Vector3(0, 0.55f, 0)).transform;
            Prims.Make(PrimitiveType.Cylinder, arm, new Vector3(0, 0.45f, 0), new Vector3(0.08f, 0.45f, 0.08f), ink, null, "Shaft");
            Prims.Make(PrimitiveType.Sphere, arm, new Vector3(0, 0.95f, 0), Vector3.one * 0.26f, knob, null, "Knob");
            Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(0, 0.56f, 0), new Vector3(0.18f, 0.06f, 0.18f), ink, Quaternion.Euler(90f, 0, 0), "Hinge");

            // lamp row on the front face showing the speed step
            int count = Game.Conveyor != null ? Game.Conveyor.SpeedCount : 6;
            lamps = new Renderer[count];
            float w = 0.6f;
            for (int i = 0; i < count; i++)
            {
                float x = count > 1 ? -w * 0.5f + w * i / (count - 1) : 0f;
                lamps[i] = Prims.Make(PrimitiveType.Sphere, transform, new Vector3(x, 0.38f, -0.31f), Vector3.one * 0.08f, lampOff, null, "Lamp").GetComponent<Renderer>();
            }

            var col = gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(0.9f, 1.2f, HalfDepth * 2f);
            col.center = new Vector3(0, 0.6f, 0);
            gameObject.layer = Layers.Default;
        }

        /// <summary>Kick the arm when it's pulled (delta 0 = just grabbed).</summary>
        public void Pull(int delta) => angleVel += delta * 260f + (delta == 0 ? 60f : 0f);

        void Update()
        {
            var belt = Game.Conveyor;
            if (belt == null) return;
            int idx = belt.SpeedIndex, count = belt.SpeedCount;
            float t = count > 1 ? idx / (float)(count - 1) : 0.5f;
            float target = Mathf.Lerp(-50f, 50f, t);
            // springy arm
            float dt = Time.unscaledDeltaTime;
            float acc = -180f * (angle - target) - 12f * angleVel;
            angleVel += acc * dt;
            angle += angleVel * dt;
            arm.localRotation = Quaternion.Euler(0f, 0f, -angle); // positive angle tips the knob to the right

            for (int i = 0; i < lamps.Length; i++)
            {
                bool on = i <= idx;
                bool hot = belt.SpeedMultiplier > 1.01f && i == idx;
                var m = !on ? lampOff : hot ? lampHot : lampOn;
                if (lamps[i].sharedMaterial != m) lamps[i].sharedMaterial = m;
            }
        }
    }
}
