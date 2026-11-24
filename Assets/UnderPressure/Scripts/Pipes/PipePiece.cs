using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    public enum PieceState { OnBelt, Held, Placed, Loose, Falling }

    /// <summary>A physical pipe piece. Lives on the belt, in the player's hands, on the floor, or on a board tile.</summary>
    public class PipePiece : MonoBehaviour
    {
        public static readonly List<PipePiece> Loose = new List<PipePiece>();

        public PipeType Type { get; private set; }
        public int Rotation { get; private set; }
        public PieceState State { get; private set; } = PieceState.OnBelt;
        public Vector2Int Cell { get; private set; }
        public bool Connected { get; private set; }
        /// <summary>True once liquid has entered: can't be removed any more.</summary>
        public bool FlowLocked { get; set; }
        public PipeModel Model { get; private set; }
        public Springy Spring { get; private set; }
        public Transform Visual { get; private set; }
        public Rigidbody Body { get; private set; }
        public float TopHeight => Model.topHeight;

        /// <summary>Where the player wants the piece while carrying it.</summary>
        [NonSerialized] public Vector3 HoldTarget;
        /// <summary>Time.time until which this piece can't be stolen (stops instant steal-backs).</summary>
        [NonSerialized] public float StealProtectedUntil;

        BoxCollider col;
        readonly List<BoxCollider> shape = new List<BoxCollider>(); // placed: blocks along the tube only
        float S;
        bool flying;
        bool highlighted;
        float wiggle;
        readonly HashSet<Dir> joinedLocal = new HashSet<Dir>();
        readonly HashSet<Dir> blownOpenings = new HashSet<Dir>(); // a burst blew these openings away (no clamps)
        Material plateMat;

        static Material highlightMat;

        public static PipePiece Create(PipeType type, int rotation, Vector3 position)
        {
            float S = Game.Config.tileSize;
            var go = new GameObject("Pipe_" + type);
            if (Game.LevelRoot != null) go.transform.SetParent(Game.LevelRoot, false);
            go.transform.position = position;
            var p = go.AddComponent<PipePiece>();
            p.S = S;
            p.Type = type;
            p.Rotation = ((rotation % 4) + 4) % 4;
            go.transform.rotation = p.TargetRotation;

            p.Visual = Prims.Empty("Visual", go.transform, Vector3.zero).transform;
            p.Spring = p.Visual.gameObject.AddComponent<Springy>();
            p.Model = PipeBuilder.Build(type, p.Visual, S);
            p.plateMat = p.Model.flanges.Count > 0 ? p.Model.flanges[0].sharedMaterial : p.Model.plate.sharedMaterial;
            p.Model.baseRoot.gameObject.SetActive(false); // the plate only comes out once it's installed

            p.Body = go.AddComponent<Rigidbody>();
            p.Body.isKinematic = true;
            p.Body.mass = 2f;
            p.Body.linearDamping = 0.4f;
            p.Body.angularDamping = 1.5f;
            p.col = go.AddComponent<BoxCollider>();
            p.SetState(PieceState.OnBelt);
            p.Spring.SetBase(0.85f, true);
            return p;
        }

        public Quaternion TargetRotation => Quaternion.Euler(0f, 90f * Rotation, 0f);

        // ------------------------------------------------------------------ state

        public void SetState(PieceState s)
        {
            if (State == PieceState.Loose) Loose.Remove(this);
            bool wasPlaced = State == PieceState.Placed;
            State = s;
            if (s == PieceState.Placed && !wasPlaced) Juice.Delay(0.11f, () => { if (this != null && State == PieceState.Placed) DeployBase(true); });
            else if (s != PieceState.Placed && wasPlaced) DeployBase(false);
            FlowLocked = false;
            // Only physics-driven pieces interpolate; everything else is moved by hand via the transform.
            Body.interpolation = s == PieceState.Loose ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            switch (s)
            {
                case PieceState.OnBelt:
                    Body.isKinematic = true; col.enabled = false; gameObject.layer = Layers.Default;
                    Spring.SetBase(0.85f);
                    break;
                case PieceState.Held:
                    Body.isKinematic = true; col.enabled = false; gameObject.layer = Layers.Default;
                    Spring.SetBase(0.58f);
                    break;
                case PieceState.Placed:
                    Body.isKinematic = true; gameObject.layer = Type == PipeType.Cross ? Layers.PlacedWalkable : Layers.PlacedPipes;
                    if (Type == PipeType.Cross)
                    {
                        // crosses are walked over anyway: one box for the whole tile (loose pieces still land on it)
                        col.enabled = true;
                        col.size = new Vector3(0.94f * S, TopHeight + 0.05f, 0.94f * S);
                        col.center = new Vector3(0, (TopHeight + 0.05f) * 0.5f, 0);
                    }
                    else
                    {
                        // only the tube blocks: the bare plate around it is free to walk on
                        col.enabled = false;
                        EnsureShape();
                        SetShape(true, gameObject.layer);
                    }
                    Spring.SetBase(1f);
                    break;
                case PieceState.Loose:
                    gameObject.layer = Layers.LoosePieces;
                    col.enabled = true;
                    col.size = new Vector3(0.78f * S, 0.55f, 0.78f * S);
                    col.center = new Vector3(0, 0.27f, 0);
                    Body.isKinematic = false;
                    Spring.SetBase(0.82f);
                    Loose.Add(this);
                    break;
                case PieceState.Falling:
                    Body.isKinematic = true; col.enabled = false;
                    break;
            }
            if (s != PieceState.Placed) { Connected = false; joinedLocal.Clear(); SetShape(false, gameObject.layer); }
            RefreshAttachments(false);
            ClearHighlights();
        }

        public void SetCell(Vector2Int c) => Cell = c;

        // ------------------------------------------------------------------ collision shape

        /// <summary>Half-width of what blocks: the tube plus its flanges and clamps.</summary>
        float ShapeHalfWidth => PipeBuilder.TubeRadius(S) * 1.4f;

        /// <summary>
        /// Placed pipes block along their tube only, as a chain of boxes following each channel's path (and the
        /// filter's canister), so critters can walk across the bare plate, e.g. around the inside of a corner.
        /// Built once, on first install; children of the root so they rotate with the piece but not with its springs.
        /// </summary>
        void EnsureShape()
        {
            if (shape.Count > 0) return;
            float half = ShapeHalfWidth, top = TopHeight + 0.05f;
            float overlap = half * 0.3f, inset = 0.03f * S; // overlap at bends; stay inside the tile like the old 0.94 box
            var root = new GameObject("Shape").transform;
            root.SetParent(transform, false);
            foreach (var ch in Model.channels)
            {
                var pts = ch.points;
                int n = pts.Length - 1;
                for (int i = 0; i < n; i++)
                {
                    Vector3 a = pts[i], b = pts[i + 1];
                    a.y = b.y = 0f;
                    Vector3 d = b - a;
                    float len = d.magnitude;
                    if (len < 1e-4f) continue;
                    Vector3 dir = d / len;
                    a += i == 0 ? dir * inset : -dir * overlap;
                    b += i == n - 1 ? -dir * inset : dir * overlap;
                    shape.Add(ShapeBox(root, (a + b) * 0.5f, Quaternion.LookRotation(dir, Vector3.up), new Vector3(half * 2f, top, (b - a).magnitude)));
                }
            }
            if (Type == PipeType.Filter)
            {
                float cr = PipeBuilder.CanisterRadius(S) * 1.1f;
                shape.Add(ShapeBox(root, Vector3.zero, Quaternion.identity, new Vector3(cr * 2f, top, cr * 2f)));
            }
        }

        static BoxCollider ShapeBox(Transform root, Vector3 groundCenter, Quaternion rot, Vector3 size)
        {
            var go = new GameObject("Block");
            go.transform.SetParent(root, false);
            go.transform.localPosition = groundCenter + Vector3.up * size.y * 0.5f;
            go.transform.localRotation = rot;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            bc.enabled = false;
            return bc;
        }

        void SetShape(bool on, int layer)
        {
            foreach (var c in shape) { c.gameObject.layer = layer; c.enabled = on; }
        }

        /// <summary>
        /// Would a critter of this radius at worldPos overlap this piece's tube, once it's installed at cellCenter?
        /// Worked out from the paths rather than the colliders, because a freshly placed piece is still flying in.
        /// Crosses count as the whole tile.
        /// </summary>
        public bool FootprintHits(Vector3 cellCenter, Vector3 worldPos, float radius)
        {
            if (Type == PipeType.Cross) return true;
            Vector3 p = Quaternion.Inverse(TargetRotation) * (worldPos - cellCenter);
            p.y = 0f;
            if (Type == PipeType.Filter && p.magnitude < PipeBuilder.CanisterRadius(S) * 1.1f + radius) return true;
            return ClosestOnTube(p, out _) < ShapeHalfWidth + radius;
        }

        /// <summary>The point on top of the tube (on the ground plane) nearest to worldPos: where a climber should land.</summary>
        public Vector3 NearestTubePoint(Vector3 worldPos)
        {
            Vector3 c = transform.position;
            Vector3 p = Quaternion.Inverse(TargetRotation) * (worldPos - c);
            p.y = 0f;
            ClosestOnTube(p, out var q);
            Vector3 w = c + TargetRotation * q;
            w.y = worldPos.y;
            return w;
        }

        /// <summary>Distance (in the piece's local ground plane) from p to the nearest point on any channel's path.</summary>
        float ClosestOnTube(Vector3 p, out Vector3 closest)
        {
            float best = float.MaxValue;
            closest = Vector3.zero;
            foreach (var ch in Model.channels)
            {
                var pts = ch.points;
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    Vector3 a = pts[i], b = pts[i + 1];
                    a.y = b.y = 0f;
                    Vector3 ab = b - a;
                    float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                    Vector3 q = a + ab * t;
                    float d = (q - p).magnitude;
                    if (d < best) { best = d; closest = q; }
                }
            }
            return best;
        }

        public void Rotate(int delta)
        {
            Rotation = (((Rotation + delta) % 4) + 4) % 4;
            Spring.Squash(-0.25f);
        }

        /// <summary>Snap the stored rotation to whatever 90° step the piece is closest to (after tumbling as a loose piece).</summary>
        public void SnapRotationToYaw()
        {
            float yaw = transform.eulerAngles.y;
            Rotation = ((Mathf.RoundToInt(yaw / 90f) % 4) + 4) % 4;
        }

        // ------------------------------------------------------------------ openings

        public Dir WorldA(int ch) => DirUtil.Rotate(Model.channels[ch].a, Rotation);
        public Dir WorldB(int ch) => DirUtil.Rotate(Model.channels[ch].b, Rotation);

        public bool HasOpening(Dir world) => ChannelWithOpening(world) >= 0;

        public int ChannelWithOpening(Dir world)
        {
            for (int i = 0; i < Model.channels.Count; i++)
                if (WorldA(i) == world || WorldB(i) == world) return i;
            return -1;
        }

        public IEnumerable<Dir> WorldOpenings()
        {
            for (int i = 0; i < Model.channels.Count; i++)
            {
                yield return WorldA(i);
                yield return WorldB(i);
            }
        }

        // ------------------------------------------------------------------ liquid

        public void SetChannelFill(int index, float fill, bool fromA) => SetChannelFill(index, fill, fromA, null);

        /// <summary>Fill a channel; 'grade' tints it for filtered / concentrated streams (null = the chamber's liquid).</summary>
        public void SetChannelFill(int index, float fill, bool fromA, LiquidGrade? grade)
        {
            bool tinted = grade.HasValue && !(grade.Value.Plain && grade.Value.water == Progress.LiquidIsWater);
            Color gc = tinted ? grade.Value.Color : default, gg = tinted ? grade.Value.Glow : default;
            var ch = Model.channels[index];
            ch.fill = Mathf.Clamp01(fill);
            ch.fromA = fromA;
            int n = ch.segments.Length;
            for (int j = 0; j < n; j++)
            {
                int pos = fromA ? j : n - 1 - j;
                float f = Mathf.Clamp01(ch.fill * n - pos);
                int lvl = Mathf.RoundToInt(f * (Mats.FillLevels - 1));
                var mat = tinted ? Mats.FillMatFor(gc, gg, lvl) : Mats.FillMat(lvl);
                foreach (var r in ch.segments[j])
                    if (r.sharedMaterial != mat) r.sharedMaterial = mat;
            }
        }

        static Material FillMatFor(LiquidGrade grade, float f)
        {
            bool tinted = !(grade.Plain && grade.water == Progress.LiquidIsWater);
            int lvl = Mathf.RoundToInt(Mathf.Clamp01(f) * (Mats.FillLevels - 1));
            return tinted ? Mats.FillMatFor(grade.Color, grade.Glow, lvl) : Mats.FillMat(lvl);
        }

        /// <summary>Two streams filling one channel from opposite ends: 'fromA' of it from the a end, 'fromB' from the b end.</summary>
        public void SetChannelFillTwoWay(int index, float fromA, float fromB, LiquidGrade gradeA, LiquidGrade gradeB)
        {
            var ch = Model.channels[index];
            ch.fill = Mathf.Clamp01(fromA + fromB);
            int n = ch.segments.Length;
            for (int j = 0; j < n; j++)
            {
                float fa = Mathf.Clamp01(fromA * n - j);
                float fb = Mathf.Clamp01(fromB * n - (n - 1 - j));
                var mat = fa >= fb ? FillMatFor(gradeA, fa) : FillMatFor(gradeB, fb);
                foreach (var r in ch.segments[j])
                    if (r.sharedMaterial != mat) r.sharedMaterial = mat;
            }
        }

        /// <summary>
        /// The channel bursts at 'atFromA' (0 = its a end, 1 = its b end): the tube there is blown away and the two
        /// torn ends peel back in strips. Each end takes the color of the stream that filled it.
        /// </summary>
        public void BurstChannel(int index, float atFromA, LiquidGrade gradeA, LiquidGrade gradeB)
        {
            var ch = Model.channels[index];
            int n = ch.segments.Length;
            // blow away the segment at the tear plus its nearer neighbour (one, at the very ends)
            float u = Mathf.Clamp01(atFromA) * n;
            int j0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, n - 1);
            int j1 = Mathf.Clamp(u - j0 >= 0.5f ? j0 + 1 : j0 - 1, 0, n - 1);
            int lo = Mathf.Min(j0, j1), hi = Mathf.Max(j0, j1);
            for (int j = lo; j <= hi; j++)
                foreach (var r in ch.segments[j]) r.enabled = false;

            float r0 = PipeBuilder.TubeRadius(S);
            // torn end on the a side: points[lo], facing on along the path; on the b side: points[hi + 1], facing back
            Vector3 pa = ch.points[lo], fwd = (ch.points[Mathf.Min(lo + 1, n)] - ch.points[Mathf.Max(lo - 1, 0)]).normalized;
            var outerA = FillMatFor(gradeA, 1f);
            TornEnd.Spawn(Model.root, pa, fwd, r0, outerA, InsideOf(outerA), index * 31 + 1);
            Vector3 pb = ch.points[hi + 1], back = (ch.points[hi] - ch.points[Mathf.Min(hi + 2, n)]).normalized;
            var outerB = FillMatFor(gradeB, 1f);
            TornEnd.Spawn(Model.root, pb, back, r0, outerB, InsideOf(outerB), index * 31 + 2);
            AddInnerWalls(ch, lo, hi, outerA, outerB);

            // a cross bursting underneath takes its bridge with it (the tear would be hidden under the arch otherwise):
            // the arch is gone, leaving two torn stubs on the flanges
            if (Type == PipeType.Cross && index == 0 && Model.channels.Count > 1) BlowAwayBridge(Model.channels[1], r0);
        }

        /// <summary>World position of the middle of a filter's canister, at tube height.</summary>
        public Vector3 CanisterPoint => Model.root.TransformPoint(new Vector3(0f, PipeBuilder.TubeHeight(S), 0f));

        /// <summary>
        /// A filter fed two liquids: the glass canister shatters (a jagged stump stays, shards fly), the cap and its
        /// handle pop off and clatter to the floor, and the soaked cartridge is left standing.
        /// </summary>
        public void ShatterCanister()
        {
            var glass = Model.root.Find("CanisterGlass");
            if (glass == null) return;
            float cr = PipeBuilder.CanisterRadius(S), h = PipeBuilder.TubeHeight(S), r = PipeBuilder.TubeRadius(S);
            float bottom = 0.05f, top = h + r + 0.16f * S, len = top - bottom;
            var glassMat = glass.GetComponent<Renderer>().sharedMaterial;
            glass.gameObject.SetActive(false);
            GlassShatter.Stump(Model.root, Vector3.zero, cr, bottom, len * 0.12f, len * 0.5f, glassMat);

            var sparkle = Mats.Lit(new Color(0.85f, 0.95f, 1f), 0.95f, 0.2f, new Color(0.3f, 0.4f, 0.5f));
            Vector3 c = Model.root.TransformPoint(Vector3.zero);
            float scale = Model.root.lossyScale.x;
            GlassShatter.Shards(c, cr * scale, (bottom + len * 0.3f) * scale, top * scale, 14, sparkle);

            // the cap (and its handle) blow off together
            var cap = Model.root.Find("CapTop");
            var handle = Model.root.Find("Handle");
            if (cap != null)
            {
                var popped = new GameObject("PoppedCap").transform;
                if (Game.LevelRoot != null) popped.SetParent(Game.LevelRoot, false);
                popped.position = cap.position;
                cap.SetParent(popped, true);
                if (handle != null) handle.SetParent(popped, true);
                Vector2 side = UnityEngine.Random.insideUnitCircle.normalized;
                Tossed.Launch(popped.gameObject, new Vector3(side.x, 0f, side.y) * UnityEngine.Random.Range(1f, 2f) + Vector3.up * 7f,
                    new Vector3(UnityEngine.Random.Range(-500f, 500f), UnityEngine.Random.Range(-200f, 200f), UnityEngine.Random.Range(-500f, 500f)), 0.03f * scale, false);
            }
        }

        /// <summary>The whole arch goes: tube, flanges, mouths and clamps at both of its openings.</summary>
        void BlowAwayBridge(PipeChannel bridge, float r0)
        {
            foreach (var seg in bridge.segments)
                foreach (var r in seg) r.enabled = false;
            foreach (var d in new[] { bridge.a, bridge.b })
            {
                blownOpenings.Add(d);
                if (Model.openingParts.TryGetValue(d, out var parts))
                    foreach (var go in parts) go.SetActive(false);
                if (Model.clamps.TryGetValue(d, out var clamp)) clamp.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// The tube is single-sided, so looking into a torn end showed whatever was behind it. Give every tube
        /// segment still standing in the burst channel an inner wall (a copy facing inward) in the inside color.
        /// </summary>
        void AddInnerWalls(PipeChannel ch, int lo, int hi, Material outerA, Material outerB)
        {
            for (int j = 0; j < ch.segments.Length; j++)
            {
                if (j >= lo && j <= hi) continue;
                var inside = InsideOf(j < lo ? outerA : outerB);
                foreach (var r in ch.segments[j])
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var go = new GameObject("InnerWall");
                    go.transform.SetParent(r.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = InsideOut(mf.sharedMesh);
                    go.AddComponent<MeshRenderer>().sharedMaterial = inside;
                }
            }
        }

        static readonly Dictionary<Mesh, Mesh> insideOut = new Dictionary<Mesh, Mesh>();

        /// <summary>The same surface facing the other way: triangles reversed, normals flipped (cached per mesh).</summary>
        static Mesh InsideOut(Mesh m)
        {
            if (insideOut.TryGetValue(m, out var cached) && cached != null) return cached;
            var tris = m.triangles;
            for (int i = 0; i < tris.Length; i += 3) { int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t; }
            var norms = m.normals;
            for (int i = 0; i < norms.Length; i++) norms[i] = -norms[i];
            var inv = new Mesh { name = m.name + "_Inside", vertices = m.vertices, normals = norms, triangles = tris };
            inv.RecalculateBounds();
            insideOut[m] = inv;
            return inv;
        }

        /// <summary>The inside of a torn tube: the outside's color, darker and wet-glossy (pure black read as a hole).</summary>
        static Material InsideOf(Material outer)
        {
            Color c = outer != null && outer.HasProperty("_BaseColor") ? outer.GetColor("_BaseColor") : Mats.PipeBody;
            Color e = outer != null && outer.IsKeywordEnabled("_EMISSION") ? outer.GetColor("_EmissionColor") : Color.black;
            return Mats.Lit(Color.Lerp(c, Color.black, 0.35f), 0.9f, 0f, e * 0.6f);
        }

        public Vector3 ChannelPoint(int index, float fill, bool fromA)
        {
            var ch = Model.channels[index];
            return Model.root.TransformPoint(PipeBuilder.Eval(ch.points, fromA ? fill : 1f - fill));
        }

        public bool AnyLiquid()
        {
            foreach (var ch in Model.channels) if (ch.fill > 0f) return true;
            return false;
        }

        // ------------------------------------------------------------------ connection visuals

        /// <summary>Called by the board after connectivity is recomputed.</summary>
        public void ApplyConnection(bool connected, HashSet<Dir> joinedWorld, float delay, bool animate)
        {
            Connected = connected;
            joinedLocal.Clear();
            foreach (var d in joinedWorld) joinedLocal.Add(DirUtil.Rotate(d, -Rotation));
            if (delay <= 0f) RefreshAttachments(animate);
            else Juice.Delay(delay, () => { if (this != null) RefreshAttachments(animate); });
        }

        void RefreshAttachments(bool animate)
        {
            bool placed = State == PieceState.Placed;
            bool showStaples = placed && !Connected;
            foreach (var st in Model.staples) ShowPart(st, showStaples, animate, true);
            foreach (var kv in Model.clamps)
                ShowPart(kv.Value, placed && Connected && joinedLocal.Contains(kv.Key) && !blownOpenings.Contains(kv.Key), animate, false);
        }

        static void ShowPart(Transform t, bool show, bool animate, bool isStaple)
        {
            bool active = t.gameObject.activeSelf;
            if (show == active) return;
            if (!animate)
            {
                t.gameObject.SetActive(show);
                t.localScale = Vector3.one;
                return;
            }
            if (show)
            {
                t.gameObject.SetActive(true);
                // staples slam up out of the ground, clamps pop on
                Vector3 from = isStaple ? new Vector3(1f, 0f, 1f) : Vector3.one * 0.2f;
                Juice.ScaleTo(t, from, Vector3.one, isStaple ? 0.22f : 0.28f, isStaple ? Juice.OutBack : Juice.OutElastic);
            }
            else
            {
                Vector3 to = isStaple ? new Vector3(1f, 0f, 1f) : Vector3.one * 1.6f;
                Juice.ScaleTo(t, Vector3.one, to, 0.15f, Juice.InCubic, () => { if (t != null) { t.gameObject.SetActive(false); t.localScale = Vector3.one; } });
            }
        }

        /// <summary>World position of the joint between this piece and its neighbour in direction 'world'.</summary>
        public Vector3 JointPosition(Dir world) =>
            transform.position + DirUtil.ToVector3(world) * S * 0.5f + Vector3.up * PipeBuilder.TubeHeight(S);

        // ------------------------------------------------------------------ feel

        readonly HashSet<object> highlighters = new HashSet<object>();

        /// <summary>Several players can highlight the same piece; it stays lit until all of them let go.</summary>
        public void SetHighlight(object who, bool on)
        {
            if (on) highlighters.Add(who); else highlighters.Remove(who);
            ApplyHighlight(highlighters.Count > 0);
        }

        public void ClearHighlights()
        {
            highlighters.Clear();
            ApplyHighlight(false);
        }

        void ApplyHighlight(bool on)
        {
            if (on == highlighted) return;
            highlighted = on;
            if (highlightMat == null) highlightMat = Mats.Lit(new Color(1f, 0.95f, 0.7f), 0.3f, 0f, new Color(0.9f, 0.75f, 0.3f));
            foreach (var f in Model.flanges) f.sharedMaterial = on ? highlightMat : plateMat;
            if (!on) Spring.pulse = 0f;
        }

        public void SetWiggle(float amount) => wiggle = amount;

        /// <summary>The base plate snaps out from under an installed piece (and tucks away when it's pulled up).</summary>
        void DeployBase(bool show)
        {
            var b = Model.baseRoot;
            if (b == null) return;
            if (show)
            {
                b.gameObject.SetActive(true);
                Juice.ScaleTo(b, new Vector3(0.2f, 1f, 0.2f), Vector3.one, 0.2f, Juice.OutBack);
                Sfx.Play(Sfx.I != null ? Sfx.I.clunk : null, 0.35f, 1.35f, 0.08f);
            }
            else
            {
                b.gameObject.SetActive(false);
                b.localScale = Vector3.one;
            }
        }

        public void FlyTo(Vector3 target, Quaternion rotation, float duration, float arc, Action onLand)
        {
            StartCoroutine(FlyCo(target, rotation, duration, arc, onLand));
        }

        IEnumerator FlyCo(Vector3 target, Quaternion rotation, float duration, float arc, Action onLand)
        {
            flying = true;
            Vector3 start = transform.position;
            Quaternion startRot = transform.rotation;
            float e = 0f;
            while (e < duration)
            {
                e += Time.deltaTime;
                float t = Mathf.Clamp01(e / duration);
                float k = t * t; // accelerate into the slam
                transform.position = Vector3.Lerp(start, target, k) + Vector3.up * Mathf.Sin(Mathf.PI * t) * arc;
                transform.rotation = Quaternion.Slerp(startRot, rotation, Juice.OutCubic(t));
                yield return null;
            }
            transform.position = target;
            transform.rotation = rotation;
            flying = false;
            onLand?.Invoke();
        }

        public void FallOffBelt(Vector3 direction)
        {
            SetState(PieceState.Falling);
            StartCoroutine(FallCo(direction));
        }

        IEnumerator FallCo(Vector3 dir)
        {
            Vector3 start = transform.position;
            Quaternion r0 = transform.rotation;
            float e = 0f, d = 0.7f;
            while (e < d)
            {
                e += Time.deltaTime;
                float t = e / d;
                transform.position = start + dir * t * 1.5f + Vector3.down * t * t * 2.5f;
                transform.rotation = r0 * Quaternion.Euler(t * 120f, 0, 0);
                Spring.SetBase(Mathf.Lerp(0.85f, 0f, Juice.InCubic(t)));
                yield return null;
            }
            Destroy(gameObject);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (State == PieceState.Held && !flying)
            {
                transform.position = Vector3.Lerp(transform.position, HoldTarget, 1f - Mathf.Exp(-28f * dt));
                transform.rotation = Quaternion.Slerp(transform.rotation, TargetRotation, 1f - Mathf.Exp(-20f * dt));
            }
            else if (State == PieceState.OnBelt && !flying)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, TargetRotation, 1f - Mathf.Exp(-14f * dt));
            }
            else if (State == PieceState.Loose && transform.position.y < -10f)
            {
                Destroy(gameObject);
            }

            if (highlighted) Spring.pulse = 0.06f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f));

            if (wiggle > 0f)
            {
                float t = Time.unscaledTime;
                Visual.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 55f) * 7f * wiggle, Mathf.Sin(t * 41f) * 2f * wiggle);
                Visual.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(t * 35f)) * 0.05f * wiggle, 0f);
            }
            else if (Visual.localRotation != Quaternion.identity || Visual.localPosition != Vector3.zero)
            {
                Visual.localRotation = Quaternion.identity;
                Visual.localPosition = Vector3.zero;
            }
        }

        void OnDestroy() => Loose.Remove(this);
    }
}
