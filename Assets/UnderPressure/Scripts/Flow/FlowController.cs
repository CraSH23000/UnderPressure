using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// Moves the liquid from the spout through connected pipes, one channel at a time.
    /// Fires events; GameManager turns them into money and fanfare.
    /// </summary>
    public class FlowController : MonoBehaviour
    {
        public event Action<PipePiece> PieceFilled;
        /// <summary>
        /// Liquid crossed a cross piece for the second time. The int is how many already-finished loops are
        /// nested inside this one (opened after it opened, closed before it closed).
        /// </summary>
        public event Action<PipePiece, int> LoopCompleted;
        public event Action<Vector3, Vector3> Spilled;

        enum Phase { Idle, Spout, Piece, Done }

        /// <summary>One stream's leading edge. Filters split a stream into two fronts; any front that can't go on
        /// leaks and ends the round.</summary>
        class Front
        {
            public bool spout;              // still coming out of the spout
            public float t;
            public PipePiece piece;
            public int channel;
            public bool fromA, loopPending;
            public bool filterIn;           // filling a filter's input arm (splits when it reaches the canister)
            public Dir exitDir;
            public Vector2Int cell;
            public LiquidGrade grade;
            public Transform blob;
            public Springy spring;
            public ParticleSystem trail;
        }

        Phase phase = Phase.Idle;
        readonly List<Front> fronts = new List<Front>();

        GridBoard board;
        GameConfig cfg;
        Transform warning;
        Material warnMat, frontMat;

        public bool Running => phase == Phase.Spout || phase == Phase.Piece;
        public bool Finished => phase == Phase.Done;

        /// <summary>Connected, still-empty pipes ahead of the most urgent stream before it would leak (capped).</summary>
        public int PiecesAhead { get; private set; } = 99;
        /// <summary>The grade of the stream that just filled a pipe (read in PieceFilled).</summary>
        public LiquidGrade LastGrade { get; private set; }
        /// <summary>What leaked out at the end (the funnel catches this).</summary>
        public LiquidGrade SpillGrade { get; private set; }
        /// <summary>How many streams are flowing right now.</summary>
        public int Streams => fronts.Count;

        // loop bookkeeping: every channel the liquid enters is one "step"
        int step;
        readonly Dictionary<PipePiece, int> firstPass = new Dictionary<PipePiece, int>();
        readonly List<(int open, int close)> closedLoops = new List<(int, int)>();

        int speedIndex = -1;
        public float SpeedMultiplier
        {
            get
            {
                var steps = cfg.flowSpeedSteps;
                if (steps == null || steps.Length == 0) return 1f;
                if (speedIndex < 0) speedIndex = Mathf.Clamp(cfg.flowSpeedDefault, 0, steps.Length - 1);
                return steps[Mathf.Clamp(speedIndex, 0, steps.Length - 1)];
            }
        }

        public int SpeedIndex { get { _ = SpeedMultiplier; return speedIndex; } }
        public int SpeedCount => cfg.flowSpeedSteps != null ? cfg.flowSpeedSteps.Length : 1;

        /// <summary>Turn the dispenser valve. Returns false if already at the limit.</summary>
        public bool AdjustSpeed(int delta)
        {
            var steps = cfg.flowSpeedSteps;
            if (steps == null || steps.Length == 0 || phase == Phase.Done) return false;
            _ = SpeedMultiplier; // initialise index
            int next = Mathf.Clamp(speedIndex + delta, 0, steps.Length - 1);
            if (next == speedIndex) return false;
            speedIndex = next;
            return true;
        }

        public void Init(GameConfig config, GridBoard b)
        {
            cfg = config; board = b;
            frontMat = Mats.Lit(Mats.Liquid, 0.9f, 0f, Mats.LiquidGlow);
            warnMat = new Material(Mats.Lit(Mats.Danger, 0.5f, 0f, Mats.Danger));
            warning = Prims.Make(PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one * 0.5f, warnMat, null, "SpillWarning").transform;
            warning.gameObject.SetActive(false);
        }

        Front NewFront(LiquidGrade grade, Vector3 at)
        {
            float S = cfg.tileSize;
            bool tinted = !(grade.Plain && grade.water == Progress.LiquidIsWater);
            var mat = tinted ? Mats.Lit(grade.Color, 0.9f, 0f, grade.Glow) : frontMat;
            var f = new Front { grade = grade };
            f.blob = Prims.Make(PrimitiveType.Sphere, transform, at, Vector3.one, mat, null, "LiquidFront").transform;
            f.spring = f.blob.gameObject.AddComponent<Springy>();
            f.spring.stiffness = 200f;
            f.spring.damping = 6f;
            f.spring.SetBase(PipeBuilder.TubeRadius(S) * 2.5f, true);
            f.trail = Fx.Trail(tinted ? grade.Color : Mats.Liquid, new Color(0.6f, 1f, 0.7f), 18f, 0.14f);
            f.trail.transform.SetParent(transform, true);
            f.trail.transform.position = at;
            fronts.Add(f);
            return f;
        }

        void RemoveFront(Front f)
        {
            fronts.Remove(f);
            if (f.blob != null) Destroy(f.blob.gameObject);
            if (f.trail != null)
            {
                f.trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(f.trail.gameObject, 1f);
            }
        }

        public void Begin()
        {
            phase = Phase.Spout;
            var f = NewFront(LiquidGrade.Source, board.Spout.PointAt(0f));
            f.spout = true;
            f.t = 0f;
        }

        void Update()
        {
            PiecesAhead = Running ? CountAheadAll(Mathf.Max(1, cfg.panicPiecesAhead) + 2) : 99;
            if (!Running) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Front urgent = null;
            float urgentT = -1f;
            foreach (var f in fronts.ToArray())
            {
                if (phase == Phase.Done) return;
                StepFront(f, dt);
                if (phase == Phase.Done) return;
                // the warning shows over whichever stream is closest to leaking
                if (!f.spout && f.piece != null && !f.filterIn && !NextValid(f.cell, f.exitDir) && f.t > urgentT) { urgent = f; urgentT = f.t; }
                else if (f.spout && !NextValid(board.Spout.Cell, board.Spout.Out) && f.t > urgentT) { urgent = f; urgentT = f.t; }
            }
            if (urgent != null) UpdateWarning(urgent.spout ? board.Spout.Cell : urgent.cell, urgent.spout ? board.Spout.Out : urgent.exitDir, urgent.t);
            else if (warning.gameObject.activeSelf) warning.gameObject.SetActive(false);
        }

        void StepFront(Front f, float dt)
        {
            if (f.spout)
            {
                f.t += dt * SpeedMultiplier / cfg.spoutFillSeconds;
                board.Spout.SetFill(f.t);
                MoveFront(f, board.Spout.PointAt(Mathf.Min(f.t, 1f)), dt);
                if (f.t >= 1f) { f.spout = false; Advance(f, board.Spout.Cell, board.Spout.Out); }
                return;
            }

            if (f.piece == null) { Spill(f, f.cell, f.exitDir); return; } // defensive: piece destroyed under us

            f.t += dt * SpeedMultiplier / cfg.secondsPerPiece;
            f.piece.SetChannelFill(f.channel, Mathf.Min(f.t, 1f), f.fromA, f.grade);
            MoveFront(f, f.piece.ChannelPoint(f.channel, Mathf.Min(f.t, 1f), f.fromA), dt);

            if (f.loopPending && f.t >= 0.5f)
            {
                f.loopPending = false;
                int open = firstPass.TryGetValue(f.piece, out int fp) ? fp : step;
                int nested = 0;
                foreach (var l in closedLoops) if (l.open > open) nested++;
                closedLoops.Add((open, step));
                LoopCompleted?.Invoke(f.piece, nested);
            }

            if (f.t < 1f) return;
            var done = f.piece;
            if (f.filterIn)
            {
                // reached the canister: split into the purified stream (clockwise arm) and the concentrate
                SplitAtFilter(f);
                return;
            }
            LastGrade = f.grade;
            if (!(done.Type == PipeType.Filter)) PieceFilled?.Invoke(done); // a filter pays once, when its input arm fills
            Advance(f, f.cell, f.exitDir);
        }

        void SplitAtFilter(Front f)
        {
            var p = f.piece;
            LastGrade = f.grade;
            PieceFilled?.Invoke(p);
            Dir inDir = p.WorldA(f.channel);
            Dir cw = DirUtil.Rotate(inDir, 1), ccw = DirUtil.Rotate(inDir, 3), opp = DirUtil.Opposite(inDir);
            // the two other arms, in clockwise order from the input: the first gets the purified stream
            var outs = new List<int>();
            foreach (var d in new[] { cw, opp, ccw })
            {
                int ch = p.ChannelWithOpening(d);
                if (ch >= 0 && ch != f.channel && !outs.Contains(ch)) outs.Add(ch);
            }
            var at = p.ChannelPoint(f.channel, 1f, true);
            for (int i = 0; i < outs.Count; i++)
            {
                int ch = outs[i];
                var grade = i == 0 ? f.grade.Purified : f.grade.Concentrated;
                if (p.Model.channels[ch].fill > 0f) { Spill(f, p.Cell, p.WorldA(ch)); return; } // already wet: it overflows
                var nf = i == 0 ? f : NewFront(grade, at);
                nf.grade = grade;
                nf.piece = p; nf.cell = p.Cell; nf.channel = ch;
                nf.fromA = false;               // arms run edge -> canister, so outflow is from the canister end
                nf.exitDir = p.WorldA(ch);
                nf.filterIn = false;
                nf.loopPending = false;
                nf.t = 0f;
                if (i == 0 && !grade.Same(f.grade))
                {
                    // the purified stream changes color: swap the blob's material
                    var r = nf.blob.GetComponent<Renderer>();
                    if (r != null) r.sharedMaterial = Mats.Lit(grade.Color, 0.9f, 0f, grade.Glow);
                }
                nf.spring.Squash(0.5f);
            }
            Fx.Burst(at + Vector3.up * 0.2f, f.grade.Purified.Color, f.grade.Concentrated.Color, 14, 2.5f, 0.1f, 0.5f, -0.3f);
            Sfx.Play(Sfx.I != null ? Sfx.I.bloop : null, 0.7f, 0.75f);
            phase = Phase.Piece;
        }

        void MoveFront(Front f, Vector3 p, float dt)
        {
            f.blob.position = p;
            // wobble like a blob of goo
            if (UnityEngine.Random.value < dt * 6f) f.spring.Squash(UnityEngine.Random.Range(-0.15f, 0.15f));
            if (f.trail != null) f.trail.transform.position = p;
        }

        void Advance(Front f, Vector2Int from, Dir dir)
        {
            var n = from + DirUtil.Offset(dir);
            var p = board.Get(n);
            int ch = p != null && p.State == PieceState.Placed ? p.ChannelWithOpening(DirUtil.Opposite(dir)) : -1;
            if (ch < 0 || board.IsSpout(n) || p.Model.channels[ch].fill > 0f)
            {
                Spill(f, from, dir);
                return;
            }

            f.loopPending = false;
            if (p.Type != PipeType.Filter)
                for (int i = 0; i < p.Model.channels.Count; i++)
                    if (i != ch && p.Model.channels[i].Full) f.loopPending = true;

            step++;
            if (!f.loopPending && p.Model.channels.Count > 1 && p.Type != PipeType.Filter) firstPass[p] = step; // a loop opens here
            f.piece = p;
            f.channel = ch;
            f.cell = n;
            f.filterIn = p.Type == PipeType.Filter;
            f.fromA = f.filterIn || p.WorldA(ch) == DirUtil.Opposite(dir);
            f.exitDir = f.fromA ? p.WorldB(ch) : p.WorldA(ch);
            p.FlowLocked = true;
            phase = Phase.Piece;
            f.t = 0f;
            f.spring.Squash(0.4f);
        }

        int CountAheadAll(int max)
        {
            int best = max;
            foreach (var f in fronts) best = Mathf.Min(best, CountAhead(f, max));
            return fronts.Count == 0 ? 99 : best;
        }

        /// <summary>Walk the connected path ahead (without changing anything) and count pipes until it would leak.</summary>
        int CountAhead(Front f, int max)
        {
            var used = new HashSet<(PipePiece, int)>();
            if (f.spout) return CountFrom(board.Spout.Cell, board.Spout.Out, max, used);
            if (f.filterIn)
            {
                // filling a filter's input arm: what matters is the shorter of its two outlets
                return CountOutlets(f.piece, f.channel, max, used);
            }
            return CountFrom(f.cell, f.exitDir, max, used);
        }

        /// <summary>Pipes the liquid can still fill going out of 'cur' in 'dir' before it would leak. Through a filter
        /// it follows both outlets and takes the shorter one (either one running dry ends the round).</summary>
        int CountFrom(Vector2Int cur, Dir dir, int max, HashSet<(PipePiece, int)> used)
        {
            int count = 0;
            while (count < max)
            {
                var n = cur + DirUtil.Offset(dir);
                if (board.IsSpout(n)) break;
                var p = board.Get(n);
                if (p == null || p.State != PieceState.Placed) break;
                int ch = p.ChannelWithOpening(DirUtil.Opposite(dir));
                if (ch < 0 || p.Model.channels[ch].fill > 0f || used.Contains((p, ch))) break;
                used.Add((p, ch));
                count++;
                if (p.Type == PipeType.Filter) return count + CountOutlets(p, ch, max - count, used);
                bool a = p.WorldA(ch) == DirUtil.Opposite(dir);
                dir = a ? p.WorldB(ch) : p.WorldA(ch);
                cur = n;
            }
            return count;
        }

        /// <summary>The shorter of a filter's two outlets (the arms other than the one the liquid came in by).</summary>
        int CountOutlets(PipePiece filter, int inArm, int max, HashSet<(PipePiece, int)> used)
        {
            if (max <= 0) return 0;
            int best = max;
            for (int i = 0; i < filter.Model.channels.Count; i++)
            {
                if (i == inArm) continue;
                if (filter.Model.channels[i].fill > 0f) return 0; // already wet: it would overflow right there
                best = Mathf.Min(best, CountFrom(filter.Cell, filter.WorldA(i), max, new HashSet<(PipePiece, int)>(used)));
            }
            return best;
        }

        bool NextValid(Vector2Int from, Dir dir)
        {
            var n = from + DirUtil.Offset(dir);
            if (board.IsSpout(n)) return false;
            var p = board.Get(n);
            if (p == null || p.State != PieceState.Placed) return false;
            int ch = p.ChannelWithOpening(DirUtil.Opposite(dir));
            return ch >= 0 && p.Model.channels[ch].fill <= 0f;
        }

        /// <summary>Red pulsing blob at the exit when the liquid is about to spill — tension!</summary>
        void UpdateWarning(Vector2Int from, Dir dir, float progress)
        {
            bool show = progress > 0.35f && !NextValid(from, dir);
            if (warning.gameObject.activeSelf != show) warning.gameObject.SetActive(show);
            if (!show) return;
            warning.position = EdgePoint(from, dir) + Vector3.up * 0.55f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 26f, progress));
            warning.localScale = Vector3.one * (0.22f + 0.12f * pulse);
            warnMat.SetColor("_EmissionColor", Mats.Danger * (0.5f + 2f * pulse));
        }

        Vector3 EdgePoint(Vector2Int from, Dir dir) =>
            board.CellCenter(from) + DirUtil.ToVector3(dir) * cfg.tileSize * 0.5f + Vector3.up * PipeBuilder.TubeHeight(cfg.tileSize);

        /// <summary>One stream can't go on: it leaks, and the round is over (every other stream stops where it is).</summary>
        void Spill(Front f, Vector2Int from, Dir dir)
        {
            phase = Phase.Done;
            SpillGrade = f.grade;
            warning.gameObject.SetActive(false);
            foreach (var o in fronts.ToArray()) RemoveFront(o);
            Spilled?.Invoke(EdgePoint(from, dir), DirUtil.ToVector3(dir));
        }
    }
}
