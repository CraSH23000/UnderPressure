using UnityEngine;

namespace UnderPressure
{
    /// <summary>The liquid source. Occupies one board cell and outputs in one direction.</summary>
    public class Spout : MonoBehaviour
    {
        public Vector2Int Cell { get; private set; }
        public SpoutHologram Hologram { get; private set; }
        public Dir Out { get; private set; }
        /// <summary>0..1, drives shaking / wheel spin / steam as the countdown runs out.</summary>
        public float Pressure { get; set; }

        float S;
        Transform body, wheel, clamp, eyePivot, level;
        Material irisMat;
        Springy spring;
        Renderer[][] nozzle;
        Vector3[] path;
        float steamTimer;
        bool clampShown;

        public static Spout Create(Transform parent, Vector2Int cell, Dir output, Vector3 worldPos, float S)
        {
            var go = new GameObject("Spout");
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            var s = go.AddComponent<Spout>();
            s.Cell = cell; s.Out = output; s.S = S;
            s.Build();
            return s;
        }

        void Build()
        {
            var white = Mats.Lit(Color.Lerp(new Color(0.96f, 0.97f, 0.98f), Theme.Wall, 0.5f), 0.75f);
            var ink = Mats.Lit(Theme.Ink, 0.6f, 0.4f);
            var flange = Mats.Lit(Mats.PipeFlange, 0.55f, 0.4f);
            var liquidGlass = Mats.Lit(Mats.Liquid, 0.95f, 0f, Mats.Liquid * 0.8f);
            float h = PipeBuilder.TubeHeight(S), r = PipeBuilder.TubeRadius(S);

            Prims.Make(PrimitiveType.Cube, transform, new Vector3(0, 0.02f, 0), new Vector3(0.9f * S, 0.04f, 0.9f * S), Mats.Lit(Mats.Plate, 0.3f));

            body = Prims.Empty("Body", transform, Vector3.zero).transform;
            spring = body.gameObject.AddComponent<Springy>();
            spring.SetBase(1f, true);
            float d = 0.72f * S;
            Vector3 fwd = DirUtil.ToVector3(Out);

            // clinical dispenser tank
            Prims.Make(PrimitiveType.Cylinder, body, new Vector3(0, 0.62f, 0), new Vector3(d, 0.62f, d), white);
            Prims.Make(PrimitiveType.Sphere, body, new Vector3(0, 1.24f, 0), new Vector3(d, 0.36f, d), white);
            Prims.Make(PrimitiveType.Cylinder, body, new Vector3(0, 0.12f, 0), new Vector3(d * 1.06f, 0.1f, d * 1.06f), ink);
            Prims.Make(PrimitiveType.Cylinder, body, new Vector3(0, 0.95f, 0), new Vector3(d * 1.03f, 0.03f, d * 1.03f), ink);
            // vertical sight glass with the glowing liquid inside, facing the camera (-Z)
            Prims.Make(PrimitiveType.Cube, body, new Vector3(0, 0.55f, -d * 0.49f), new Vector3(0.16f, 0.6f, 0.04f), ink);
            level = Prims.Make(PrimitiveType.Cube, body, new Vector3(0, 0.55f, -d * 0.51f), new Vector3(0.1f, 0.52f, 0.03f), liquidGlass, null, "SightGlass").transform;

            // valve wheel
            wheel = Prims.Empty("Wheel", body, new Vector3(0, 1.48f, 0)).transform;
            Prims.Make(PrimitiveType.Cylinder, body, new Vector3(0, 1.38f, 0), new Vector3(0.08f, 0.1f, 0.08f), ink);
            Prims.Make(PrimitiveType.Cylinder, wheel, Vector3.zero, new Vector3(0.6f, 0.02f, 0.6f), ink);
            Prims.Make(PrimitiveType.Cube, wheel, new Vector3(0, 0.02f, 0), new Vector3(0.58f, 0.03f, 0.06f), ink);
            Prims.Make(PrimitiveType.Cube, wheel, new Vector3(0, 0.02f, 0), new Vector3(0.06f, 0.03f, 0.58f), ink);

            // the lens: a single unblinking eye that follows whoever is closest
            eyePivot = Prims.Empty("EyePivot", body, new Vector3(0, 1.05f, 0)).transform;
            Prims.Make(PrimitiveType.Sphere, eyePivot, new Vector3(0, 0, d * 0.42f), new Vector3(0.36f, 0.36f, 0.3f), ink, null, "Housing");
            irisMat = new Material(Mats.Lit(new Color(0.9f, 0.1f, 0.08f), 0.9f, 0f, new Color(1.2f, 0.08f, 0.05f)));
            Prims.Make(PrimitiveType.Sphere, eyePivot, new Vector3(0, 0, d * 0.42f + 0.12f), new Vector3(0.2f, 0.2f, 0.1f), irisMat, null, "Iris");
            Prims.Make(PrimitiveType.Sphere, eyePivot, new Vector3(0, 0, d * 0.42f + 0.16f), new Vector3(0.07f, 0.07f, 0.04f), ink, null, "Pupil");

            // nozzle: centre -> edge, fills first when the flow starts
            const int n = 4;
            path = new Vector3[n + 1];
            for (int i = 0; i <= n; i++) path[i] = fwd * (S * 0.5f * i / n) + Vector3.up * h;
            nozzle = new Renderer[n][];
            for (int i = 0; i < n; i++)
                nozzle[i] = new[] { Prims.Segment(transform, path[i], i == n - 1 ? Vector3.MoveTowards(path[i + 1], path[i], 0.03f * S) : path[i + 1], r, Mats.FillMat(0), i == n - 1 ? 1f : 1.06f).GetComponent<Renderer>() };
            Vector3 edge = path[n];
            Prims.Segment(transform, edge - fwd * 0.07f * S, edge, r * 1.25f, flange, 1f);
            Prims.Segment(transform, edge - fwd * 0.004f, edge + fwd * 0.012f, r * 0.82f, Mats.Lit(Theme.Ink, 0.4f), 1f);

            clamp = Prims.Empty("Clamp", transform, edge - fwd * 0.085f * S).transform;
            clamp.localRotation = Quaternion.LookRotation(fwd);
            Prims.Make(PrimitiveType.Cylinder, clamp, Vector3.zero, new Vector3(r * 2.9f, 0.06f * S, r * 2.9f),
                Mats.Lit(Mats.Clamp, 0.85f, 0.9f, Mats.Clamp * 0.15f), Quaternion.Euler(90, 0, 0));
            clamp.gameObject.SetActive(false);

            Hologram = SpoutHologram.Create(this);

            var col = gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(0.9f * S, 1.6f, 0.9f * S);
            col.center = new Vector3(0, 0.8f, 0);
            gameObject.layer = Layers.Default; // blocks walking AND climbing
        }

        float flowDrain, wheelAngle, wheelKick;

        /// <summary>Someone turned the valve (delta = +1 faster, -1 slower, 0 just grabbed it).</summary>
        public void TurnValve(int delta)
        {
            wheelKick += delta * 900f;
            spring.Squash(delta == 0 ? 0.1f : 0.22f);
        }

        void TrackNearestPlayer()
        {
            Transform best = null;
            float bestD = float.MaxValue;
            foreach (var pl in Game.Players)
            {
                if (pl == null) continue;
                float dd = (pl.transform.position - transform.position).sqrMagnitude;
                if (dd < bestD) { bestD = dd; best = pl.transform; }
            }
            Vector3 dir = best != null ? best.position + Vector3.up * 0.6f - eyePivot.position : Vector3.back;
            dir.y = Mathf.Clamp(dir.y, -2f, 2f);
            if (dir.sqrMagnitude < 0.01f) return;
            var want = Quaternion.LookRotation(dir.normalized, Vector3.up);
            eyePivot.rotation = Quaternion.Slerp(eyePivot.rotation, want, 1f - Mathf.Exp(-4f * Time.deltaTime));
        }

        public void SetFill(float t)
        {
            flowDrain = Mathf.Clamp01(t);
            int n = nozzle.Length;
            for (int j = 0; j < n; j++)
            {
                float f = Mathf.Clamp01(t * n - j);
                var mat = Mats.FillMat(Mathf.RoundToInt(f * (Mats.FillLevels - 1)));
                nozzle[j][0].sharedMaterial = mat;
            }
        }

        public Vector3 PointAt(float t) => transform.TransformPoint(PipeBuilder.Eval(path, t));
        public Vector3 CenterTop => transform.position + Vector3.up * 1.3f;

        public void SetClamp(bool on, bool animate)
        {
            if (on == clampShown) return;
            clampShown = on;
            if (!animate) { clamp.gameObject.SetActive(on); return; }
            if (on)
            {
                clamp.gameObject.SetActive(true);
                Juice.ScaleTo(clamp, Vector3.one * 0.2f, Vector3.one, 0.28f, Juice.OutElastic);
            }
            else Juice.ScaleTo(clamp, Vector3.one, Vector3.one * 1.5f, 0.12f, Juice.InCubic,
                () => { clamp.gameObject.SetActive(false); clamp.localScale = Vector3.one; });
        }

        public void Burst()
        {
            spring.Squash(-0.6f);
            Fx.Burst(CenterTop, Color.white, new Color(0.85f, 0.95f, 1f), 26, 5f, 0.35f, 0.7f, -0.3f);
        }

        public void Tick() => spring.Squash(0.18f);

        void Update()
        {
            float p = Pressure;
            TrackNearestPlayer();
            irisMat.SetColor("_EmissionColor", new Color(1.2f, 0.08f, 0.05f) * (0.6f + p * 3f + Mathf.Sin(Time.time * (4f + p * 20f)) * 0.3f * p));
            // sight glass: just a sliver during the countdown, filling up as the spout fills, full while it flows
            float frac = Mathf.Lerp(0.06f, 1f, flowDrain);
            level.localScale = new Vector3(0.1f, 0.52f * frac, 0.03f);
            level.localPosition = new Vector3(level.localPosition.x, 0.29f + 0.26f * frac, level.localPosition.z);
            wheelKick = Mathf.MoveTowards(wheelKick, 0f, Mathf.Abs(wheelKick) * 6f * Time.deltaTime + 1f);
            float flowMul = Game.Flow != null ? Game.Flow.SpeedMultiplier : 1f;
            wheelAngle += Time.deltaTime * ((40f + p * p * 900f) * flowMul + wheelKick);
            wheel.localRotation = Quaternion.Euler(0f, wheelAngle, 0f);
            float jitter = p * p * 0.035f;
            body.localPosition = new Vector3(Random.Range(-jitter, jitter), 0f, Random.Range(-jitter, jitter));

            if (p > 0.45f)
            {
                steamTimer -= Time.deltaTime;
                if (steamTimer <= 0f)
                {
                    steamTimer = Mathf.Lerp(0.5f, 0.08f, p);
                    var side = Random.insideUnitCircle.normalized * 0.4f;
                    Fx.Puff(CenterTop + new Vector3(side.x, -0.1f, side.y), Color.white, 0.25f + p * 0.2f);
                }
            }
        }
    }
}
