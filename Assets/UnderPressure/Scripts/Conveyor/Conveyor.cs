using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// A belt that runs around the outside of the board along a polyline path:
    /// it comes out of the left wall at the top-left, runs to the top-right, then turns 90° (clockwise) at each
    /// corner and finally tips anything still riding it into a pit just below the entrance — costing you money.
    /// Pieces turn with the belt, so their placement rotation changes at every corner.
    /// </summary>
    public class Conveyor : MonoBehaviour
    {
        public float HalfWidth { get; private set; } = 1f;
        public float TopY => 0.03f; // flush with the floor: you can walk (and ride) on it
        public Vector3 PitCenter { get; private set; }
        /// <summary>Half the width of the pit opening (critters whose centre is inside this fall in).</summary>
        public float PitHalfSize => HalfWidth + 0.15f;

        class Item { public PipePiece piece; public float s; public int seg; }

        int speedIndex = -1;
        public float SpeedMultiplier
        {
            get
            {
                var steps = cfg != null ? cfg.beltSpeedSteps : null;
                if (steps == null || steps.Length == 0) return 1f;
                if (speedIndex < 0) speedIndex = Mathf.Clamp(cfg.beltSpeedDefault, 0, steps.Length - 1);
                return steps[Mathf.Clamp(speedIndex, 0, steps.Length - 1)];
            }
        }
        public int SpeedIndex { get { _ = SpeedMultiplier; return speedIndex; } }
        public int SpeedCount => cfg != null && cfg.beltSpeedSteps != null ? cfg.beltSpeedSteps.Length : 1;

        /// <summary>Back to the default lever step (the end of every round).</summary>
        public void ResetSpeed()
        {
            if (cfg != null && cfg.beltSpeedSteps != null && cfg.beltSpeedSteps.Length > 0)
                speedIndex = Mathf.Clamp(cfg.beltSpeedDefault, 0, cfg.beltSpeedSteps.Length - 1);
            Game.Lever?.Pull(0);
        }

        /// <summary>Pull the belt lever. Returns false at the end stops.</summary>
        public bool AdjustSpeed(int delta)
        {
            if (cfg.beltSpeedSteps == null || cfg.beltSpeedSteps.Length == 0) return false;
            _ = SpeedMultiplier;
            int next = Mathf.Clamp(speedIndex + delta, 0, cfg.beltSpeedSteps.Length - 1);
            if (next == speedIndex) return false;
            speedIndex = next;
            return true;
        }

        Vector3[] pts;
        float[] cum;      // cumulative length at each point
        float total;
        float speed, spacing, sinceSpawn;
        float speedScale, beltOffset; // speedScale eases to 0 before anyone joins and after the round ends
        readonly List<Item> items = new List<Item>();
        readonly List<(Transform t, float s)> slats = new List<(Transform, float)>();
        readonly List<PipeType> bag = new List<PipeType>();
        Transform pitGlow;
        Material pitGlowMat;
        GameConfig cfg;
        const float SlatPitch = 0.5f;

        public void Build(GameConfig config, Vector3[] path, Vector3 pitCenter)
        {
            cfg = config;
            speed = cfg.beltSpeed;
            spacing = cfg.beltSpacing;
            PitCenter = pitCenter;
            SetPath(path);
            BuildTrack();
            BuildPit();

            // Pre-fill only the start of the belt, so nothing is about to fall in the pit when the round begins.
            float visibleStart = Vector3.Distance(pts[0], pts[1]) > 6f ? 4.5f : 0f;
            float prefillEnd = Mathf.Lerp(visibleStart, total, Mathf.Clamp01(cfg.beltPrefill));
            for (float s = prefillEnd; s > visibleStart; s -= spacing) Spawn(s, false);
        }

        static Vector3 side_(Vector3 dir) => Vector3.Cross(Vector3.up, dir);

        Transform track; // the bed, rails, chevrons and slats (re-laid when the room grows); the pit stays

        void SetPath(Vector3[] path)
        {
            pts = path;
            cum = new float[pts.Length];
            for (int i = 1; i < pts.Length; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            total = cum[pts.Length - 1];
        }

        void BuildTrack()
        {
            track = new GameObject("Track").transform;
            track.SetParent(transform, false);
            var frame = Mats.Lit(Color.Lerp(Theme.Wall, Theme.WallTrim, 0.15f), 0.7f, 0.2f);
            var beltMat = Mats.Lit(Theme.Ink, 0.25f);
            var slatMat = Mats.Lit(Color.Lerp(Theme.Ink, Theme.WallTrim, 0.6f), 0.35f, 0.3f);
            var stripe = Mats.Lit(Theme.AccentOrange, 0.5f, 0f, Theme.AccentOrange * 0.4f);
            float w = HalfWidth * 2f;

            // straight runs: body (with collider so nobody walks on the belt) + rails
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                Vector3 dir = (b - a).normalized;
                // extend by half a width so runs overlap into square corners
                float len = Vector3.Distance(a, b) + (i > 0 ? HalfWidth : 0f) + (i < pts.Length - 2 ? HalfWidth : 0f);
                Vector3 mid = (a + b) * 0.5f + dir * (((i < pts.Length - 2) ? HalfWidth : 0f) - (i > 0 ? HalfWidth : 0f)) * 0.5f;
                var rot = Quaternion.LookRotation(dir);
                Prims.Make(PrimitiveType.Cube, track, new Vector3(mid.x, 0.002f, mid.z), new Vector3(w, 0.02f, len), beltMat, rot, "BeltBed");
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                for (int sgn = -1; sgn <= 1; sgn += 2)
                    Prims.Make(PrimitiveType.Cube, track, new Vector3(mid.x, 0.006f, mid.z) + side * sgn * (HalfWidth + 0.06f),
                        new Vector3(0.12f, 0.03f, len + 0.12f), frame, rot, "Edge");
                // orange chevrons along the inner rail show the direction of travel
                Vector3 inner = Vector3.Cross(Vector3.up, dir); // belt runs clockwise, so the board is on this side
                for (float d = 0.6f; d < Vector3.Distance(a, b) - 0.3f; d += 1.2f)
                {
                    // chevron arrows painted on both edges point the way the belt runs
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        Vector3 p = a + dir * d + inner * sgn * (HalfWidth - 0.2f) + Vector3.up * 0.016f;
                        Prims.Make(PrimitiveType.Cube, track, p - side_(dir) * 0.08f, new Vector3(0.06f, 0.01f, 0.3f), stripe, rot * Quaternion.Euler(0, 35f, 0), "Chevron");
                        Prims.Make(PrimitiveType.Cube, track, p + side_(dir) * 0.08f, new Vector3(0.06f, 0.01f, 0.3f), stripe, rot * Quaternion.Euler(0, -35f, 0), "Chevron");
                    }
                }
            }

            // moving slats spaced evenly along the whole path
            int count = Mathf.CeilToInt(total / SlatPitch);
            for (int i = 0; i < count; i++)
            {
                var t = Prims.Make(PrimitiveType.Cube, track, Vector3.zero, new Vector3(w - 0.1f, 0.01f, SlatPitch * 0.25f), slatMat, null, "Slat").transform;
                slats.Add((t, i * SlatPitch));
            }
        }

        /// <summary>
        /// The room grew: lay the belt along a new path (same corners, same order, the end at the pit unchanged).
        /// Every piece riding it stays on its own run and is re-projected onto that run's new line; the belt's easing
        /// glides it over, and its rotation still matches the run, so nothing turns the wrong way.
        /// </summary>
        public void Repath(Vector3[] path)
        {
            SetPath(path);
            if (track != null) Destroy(track.gameObject);
            slats.Clear();
            BuildTrack();
            foreach (var it in items)
            {
                if (it.piece == null) continue;
                int seg = Mathf.Clamp(it.seg, 0, pts.Length - 2);
                Vector3 a = pts[seg], ab = pts[seg + 1] - a;
                float len = ab.magnitude;
                Vector3 ap = it.piece.transform.position - a; ap.y = 0f;
                float t = Mathf.Clamp(Vector3.Dot(ap, ab / Mathf.Max(0.0001f, len)), 0f, Mathf.Max(0f, len - 0.01f));
                it.s = cum[seg] + t;
                it.seg = seg;
            }
        }

        void BuildPit()
        {
            var ink = Mats.Lit(new Color(0.01f, 0.01f, 0.01f), 0.1f);
            var black = Mats.Lit(Theme.Ink, 0.3f);
            var yellow = Mats.Lit(Theme.Hazard, 0.4f);
            float size = HalfWidth * 2f + 0.3f;
            var c = PitCenter;
            Prims.Make(PrimitiveType.Cube, transform, new Vector3(c.x, 0.012f, c.z), new Vector3(size, 0.02f, size), ink, null, "PitHole");
            // inner lips that fake depth, just inside the north and south caution tape
            var lipMat = Mats.Lit(Theme.WallTrim, 0.3f);
            Prims.Make(PrimitiveType.Cube, transform, new Vector3(c.x, 0.02f, c.z + size * 0.5f - 0.1f), new Vector3(size, 0.03f, 0.2f), lipMat, null, "PitLip");
            Prims.Make(PrimitiveType.Cube, transform, new Vector3(c.x, 0.02f, c.z - size * 0.5f + 0.1f), new Vector3(size, 0.03f, 0.2f), lipMat, null, "PitLip");
            int i = 0;
            float h = size * 0.5f + 0.12f;
            for (float d = -h; d < h - 0.01f; d += 0.3f, i++)
            {
                var m = (i & 1) == 0 ? black : yellow;
                Prims.Make(PrimitiveType.Cube, transform, new Vector3(c.x + d + 0.15f, 0.02f, c.z - h), new Vector3(0.3f, 0.04f, 0.24f), m);
                Prims.Make(PrimitiveType.Cube, transform, new Vector3(c.x + d + 0.15f, 0.02f, c.z + h), new Vector3(0.3f, 0.04f, 0.24f), m);
                Prims.Make(PrimitiveType.Cube, transform, new Vector3(c.x - h, 0.02f, c.z + d + 0.15f), new Vector3(0.24f, 0.04f, 0.3f), m);
                Prims.Make(PrimitiveType.Cube, transform, new Vector3(c.x + h, 0.02f, c.z + d + 0.15f), new Vector3(0.24f, 0.04f, 0.3f), m);
            }
            // a sullen red glow from somewhere down there
            pitGlowMat = new Material(Mats.Lit(new Color(0.3f, 0.02f, 0.02f), 0.2f, 0f, new Color(0.6f, 0.03f, 0.02f)));
            pitGlow = Prims.Make(PrimitiveType.Cube, transform, new Vector3(c.x, 0.015f, c.z), new Vector3(size * 0.8f, 0.02f, size * 0.8f), pitGlowMat, null, "PitGlow").transform;
            // invisible wall so loose pieces don't tumble in (critters pass through it, and can fall in)
            var guard = new GameObject("PitGuard");
            guard.layer = Layers.PlacedWalkable;
            guard.transform.SetParent(transform, false);
            guard.transform.position = new Vector3(c.x, 0.75f, c.z);
            guard.AddComponent<BoxCollider>().size = new Vector3(size, 1.5f, size);
            BuildSafetyCage(c);
        }

        /// <summary>
        /// Railings on the pit's west, north and east edges (on the hazard stripes) so nobody strolls in by accident.
        /// The south side, where the belt feeds in, stays open: ride the belt (or chase a piece) and in you go.
        /// </summary>
        void BuildSafetyCage(Vector3 c)
        {
            var rail = Mats.Lit(Theme.Hazard, 0.5f, 0.6f);
            var post = Mats.Lit(Theme.Ink, 0.4f, 0.6f);
            var cage = new GameObject("PitCage").transform;
            cage.SetParent(transform, false);
            const float railH = 0.85f, postR = 0.05f, railR = 0.035f;
            // corners, going round W -> N -> E; the open south side is the gap between the first and last.
            // The rails hug the inside of the hole's edge (you only fall in well past it), which leaves room to walk
            // between the pit and the belt lever, and keeps the north side off the top run of the belt.
            float h = PitHalfSize - 0.08f;
            var corners = new[]
            {
                new Vector3(c.x - h, 0f, c.z - h),
                new Vector3(c.x - h, 0f, c.z + h),
                new Vector3(c.x + h, 0f, c.z + h),
                new Vector3(c.x + h, 0f, c.z - h),
            };
            for (int s = 0; s < 3; s++)
            {
                Vector3 a = corners[s], b = corners[s + 1];
                // posts every ~0.9 along the side
                int n = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / 0.9f));
                for (int k = s == 0 ? 0 : 1; k <= n; k++)
                {
                    Vector3 p = Vector3.Lerp(a, b, k / (float)n);
                    Prims.Segment(cage, p, p + Vector3.up * (railH + 0.04f), postR, post);
                    Prims.Make(PrimitiveType.Sphere, cage, p + Vector3.up * (railH + 0.04f), Vector3.one * postR * 2.6f, post, null, "Cap");
                }
                Prims.Segment(cage, a + Vector3.up * railH, b + Vector3.up * railH, railR, rail);        // top rail
                Prims.Segment(cage, a + Vector3.up * railH * 0.5f, b + Vector3.up * railH * 0.5f, railR, rail); // knee rail
                // the actual barrier: a thin wall the walking (and vaulting) player bumps into
                var wall = new GameObject("CageWall");
                wall.layer = Layers.Default;
                wall.transform.SetParent(cage, false);
                Vector3 d = b - a;
                wall.transform.position = (a + b) * 0.5f + Vector3.up * 0.9f;
                wall.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                wall.AddComponent<BoxCollider>().size = new Vector3(0.12f, 1.8f, d.magnitude + 0.12f);
            }
        }

        // ------------------------------------------------------------------ path

        int SegmentAt(float s)
        {
            for (int i = 0; i < pts.Length - 2; i++) if (s < cum[i + 1]) return i;
            return pts.Length - 2;
        }

        Vector3 PointAt(float s, out Vector3 dir)
        {
            int i = SegmentAt(s);
            float segLen = cum[i + 1] - cum[i];
            float t = segLen > 0f ? Mathf.Clamp01((s - cum[i]) / segLen) : 0f;
            dir = (pts[i + 1] - pts[i]).normalized;
            return Vector3.Lerp(pts[i], pts[i + 1], t);
        }

        float Project(Vector3 p, out float distance)
        {
            float best = 0f;
            distance = float.MaxValue;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                Vector3 ab = b - a; ab.y = 0f;
                Vector3 ap = p - a; ap.y = 0f;
                float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                Vector3 q = a + ab * t;
                Vector3 d = p - q; d.y = 0f;
                float m = d.magnitude;
                if (m < distance) { distance = m; best = cum[i] + t * ab.magnitude; }
            }
            return best;
        }

        // ------------------------------------------------------------------ spawning

        PipeType NextFromBag()
        {
            if (bag.Count == 0)
            {
                if (cfg.spawnBag == null || cfg.spawnBag.Length == 0) bag.Add(PipeType.Straight);
                else bag.AddRange(cfg.spawnBag);
                for (int i = bag.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (bag[i], bag[j]) = (bag[j], bag[i]);
                }
            }
            var t = bag[bag.Count - 1];
            bag.RemoveAt(bag.Count - 1);
            return t;
        }

        void Spawn(float s, bool pop)
        {
            var pos = PointAt(s, out _);
            var type = NextFromBag();
            // the Crossing Sign: some non-crosses turn into crosses (luck can tip it), and arrive with the sign over them
            bool signed = false, lucky = false;
            if (pop && type != PipeType.Cross && Items.TeamOwns(Items.CrossingSign) && Items.Roll(ExtraCrossChance(), out lucky))
            {
                type = PipeType.Cross;
                signed = true;
            }
            var p = PipePiece.Create(type, Random.Range(0, 4), new Vector3(pos.x, TopY, pos.z));
            items.Add(new Item { piece = p, s = s, seg = SegmentAt(s) });
            if (pop) { p.Spring.SetBase(0.85f, true); p.Spring.Squash(-0.4f); }
            if (signed) FloatingTag.Create(p, "item_crossing", lucky);
        }

        /// <summary>Chance to turn a non-cross into a cross, so crosses come crossingSignBoost (25%) more often overall.</summary>
        float ExtraCrossChance()
        {
            if (cfg.spawnBag == null || cfg.spawnBag.Length == 0) return 0f;
            int crosses = 0;
            foreach (var t in cfg.spawnBag) if (t == PipeType.Cross) crosses++;
            float r = crosses / (float)cfg.spawnBag.Length;
            if (r <= 0f || r >= 1f) return 0f;
            return Mathf.Clamp01(Mathf.Max(0f, cfg.crossingSignBoost) * r / (1f - r));
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            float dt = Time.deltaTime;
            bool running = Game.Manager != null && !Game.Manager.IsOver && Game.Players.Count > 0;
            bool idling = Game.Manager != null && Game.Manager.IsOver && Game.Players.Count > 0; // off the clock: slow, but still delivering toys
            float scaleTarget = running ? 1f : idling ? (Game.Config != null ? Game.Config.freePlayBeltSpeed : 0.35f) : 0f; // the lever still scales it
            speedScale = Mathf.MoveTowards(speedScale, scaleTarget, dt * 1.5f);
            float step = speed * SpeedMultiplier * speedScale * dt;
            beltOffset += step;

            sinceSpawn += step;
            if (sinceSpawn >= spacing)
            {
                sinceSpawn -= spacing;
                Spawn(0f, true);
            }

            for (int i = items.Count - 1; i >= 0; i--)
            {
                var it = items[i];
                if (it.piece == null || it.piece.State != PieceState.OnBelt) { items.RemoveAt(i); continue; }
                it.s += step;
                if (it.s >= total)
                {
                    items.RemoveAt(i);
                    Dump(it.piece);
                    continue;
                }
                int seg = SegmentAt(it.s);
                if (seg != it.seg)
                {
                    // the belt turned 90° clockwise, so did the piece
                    it.piece.Rotate(seg - it.seg);
                    it.seg = seg;
                }
                var target = PointAt(it.s, out _);
                var pos = it.piece.transform.position;
                // ease toward the belt line (pieces put back by hand glide into place)
                pos.x = Mathf.Lerp(pos.x, target.x, 1f - Mathf.Exp(-10f * dt));
                pos.z = Mathf.Lerp(pos.z, target.z, 1f - Mathf.Exp(-10f * dt));
                pos.y = Mathf.Lerp(pos.y, TopY, 1f - Mathf.Exp(-12f * dt));
                it.piece.transform.position = pos;
            }

            foreach (var (t, s0) in slats)
            {
                float s = Mathf.Repeat(s0 + beltOffset, total);
                var p = PointAt(s, out var dir);
                t.position = new Vector3(p.x, 0.017f, p.z);
                t.rotation = Quaternion.LookRotation(dir);
            }

            CatchLoosePieces();

            if (pitGlowMat != null)
                pitGlowMat.SetColor("_EmissionColor", new Color(0.6f, 0.03f, 0.02f) * (0.7f + 0.3f * Mathf.Sin(Time.time * 1.7f)));
        }

        void Dump(PipePiece p)
        {
            PointAt(total - 0.01f, out var dir);
            p.FallOffBelt(dir);
            Game.Manager?.OnPieceWasted(PitCenter);
        }

        /// <summary>Loose pieces that come to rest on the belt get picked up by it.</summary>
        void CatchLoosePieces()
        {
            var loose = PipePiece.Loose;
            for (int i = loose.Count - 1; i >= 0; i--)
            {
                var p = loose[i];
                if (p == null || p.Body.linearVelocity.sqrMagnitude > 0.25f) continue;
                float s = Project(p.transform.position, out float d);
                if (d < HalfWidth - 0.2f && s > 1f && s < total - 1.5f) Insert(p, p.transform.position);
            }
        }

        /// <summary>World velocity of the belt surface under this point (zero if not on the belt).</summary>
        public Vector3 SurfaceVelocity(Vector3 point)
        {
            float s = Project(point, out float d);
            if (d > HalfWidth - 0.05f || s <= 0f || s >= total) return Vector3.zero;
            PointAt(s, out var dir);
            return dir * speed * SpeedMultiplier * speedScale;
        }

        // ------------------------------------------------------------------ queries

        public PipePiece FindNearest(Vector3 point, float radius)
        {
            PipePiece best = null;
            float bestD = radius;
            foreach (var it in items)
            {
                if (it.piece == null) continue;
                var d = it.piece.transform.position - point;
                d.y = 0f;
                float m = d.magnitude;
                if (m < bestD) { bestD = m; best = it.piece; }
            }
            return best;
        }

        public void Take(PipePiece p) => items.RemoveAll(it => it.piece == p);

        public bool IsOverBelt(Vector3 point)
        {
            Project(point, out float d);
            return d < HalfWidth + 0.35f;
        }

        /// <summary>Put a carried piece back on the belt at the nearest point.</summary>
        public void Insert(PipePiece p, Vector3 near)
        {
            float s = Mathf.Clamp(Project(near, out _), 0.5f, total - 1f);
            p.SetState(PieceState.OnBelt);
            var pos = PointAt(s, out _);
            p.FlyTo(new Vector3(pos.x, TopY, pos.z), p.TargetRotation, 0.15f, 0.4f, null);
            items.Add(new Item { piece = p, s = s, seg = SegmentAt(s) });
        }
    }
}
