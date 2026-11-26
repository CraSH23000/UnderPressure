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
            public Front partner;           // another stream filling this same channel from the other end: they'll meet and burst it
            public Dir exitDir;
            public Vector2Int cell;
            public LiquidGrade grade;
            public float dist;              // flow distance from the spout to where this stream entered its current channel
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
        /// <summary>Seconds until the most urgent stream spills at the current flow speed (999 = well clear).</summary>
        public float SecondsToSpill { get; private set; } = 999f;
        /// <summary>The grade of the stream that just filled a pipe (read in PieceFilled).</summary>
        public LiquidGrade LastGrade { get; private set; }
        /// <summary>What leaked out at the end (the funnel catches this).</summary>
        public LiquidGrade SpillGrade { get; private set; }
        /// <summary>How many streams are flowing right now.</summary>
        public int Streams => fronts.Count;

        /// <summary>World position of the first stream's leading edge (the spout creature watches it), or null.</summary>
        public Vector3? FrontPosition
        {
            get
            {
                foreach (var f in fronts) if (f.blob != null) return f.blob.position;
                return null;
            }
        }
        /// <summary>The round ended with two streams meeting head-on and bursting a pipe (read in Spilled).</summary>
        public bool LastSpillBurst { get; private set; }
        /// <summary>For a burst: the pipe's direction at the tear (the two torn ends spray both ways along it);
        /// zero for a shattered filter (it sprays straight up).</summary>
        public Vector3 BurstAxis { get; private set; }
        /// <summary>The burst was a filter fed two liquids (its canister shattered), not a pipe.</summary>
        public bool LastBurstFilter { get; private set; }

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
            frontMat = Mats.LiquidMat(Mats.LiquidId, Mats.Liquid, Mats.LiquidGlow, 1f, 0.9f);
            warnMat = new Material(Mats.Lit(Mats.Danger, 0.5f, 0f, Mats.Danger));
            warning = Prims.Make(PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one * 0.5f, warnMat, null, "SpillWarning").transform;
            warning.gameObject.SetActive(false);
        }

        Front NewFront(LiquidGrade grade, Vector3 at)
        {
            float S = cfg.tileSize;
            bool tinted = !(grade.Plain && grade.water == Progress.LiquidIsWater);
            var mat = tinted ? Mats.LiquidMat(grade.liquidId, grade.Color, grade.Glow, 1f, 0.9f) : frontMat;
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
            LastSpillBurst = false;
            LastBurstFilter = false;
            var f = NewFront(LiquidGrade.Source, board.Spout.PointAt(0f));
            f.spout = true;
            f.t = 0f;
        }

        void Update()
        {
            // look far enough ahead to cover the panic window and the music's riser window at this flow speed
            float perPipe = cfg.secondsPerPiece / Mathf.Max(0.01f, SpeedMultiplier);
            float window = Mathf.Max(cfg.panicSeconds, cfg.musicRiserSeconds);
            int max = Mathf.Clamp(Mathf.CeilToInt(window / Mathf.Max(0.01f, perPipe)) + 2, 3, 60);
            PiecesAhead = Running ? CountAheadAll(max) : 99;
            SecondsToSpill = Running && PiecesAhead < max ? (PiecesAhead + CurrentPipeLeft()) * perPipe : 999f;
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
                if (!f.spout && f.piece != null && !f.filterIn && f.partner == null && !NextValid(f.cell, f.exitDir) && f.t > urgentT) { urgent = f; urgentT = f.t; }
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

            if (f.partner != null)
            {
                // two streams closing in on each other inside one channel
                var o = f.partner;
                var fa = f.fromA ? f : o;
                var fb = f.fromA ? o : f;
                if (f.t + o.t >= 1f)
                {
                    float over = f.t + o.t - 1f;
                    BurstPipe(fa, fb, Mathf.Clamp01(fa.t - over * 0.5f));
                    return;
                }
                f.piece.SetChannelFillTwoWay(f.channel, fa.t, fb.t, fa.grade, fb.grade);
                MoveFront(f, f.piece.ChannelPoint(f.channel, f.t, f.fromA), dt);
                return;
            }
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
            var src = f.grade; // f becomes the purified stream below, so grade both outlets from what came in
            float outDist = f.dist + p.ChannelLength(f.channel); // both outlets carry on from the canister
            // another stream already came in by an outlet arm: the filter's fed two liquids and the canister shatters
            foreach (int ch in outs)
            {
                var other = fronts.Find(o => o != f && o.piece == p && o.channel == ch);
                if (other != null || p.Model.channels[ch].fill > 0f) { BurstFilter(p, src, other != null ? other.grade : src); return; }
            }
            for (int i = 0; i < outs.Count; i++)
            {
                int ch = outs[i];
                var grade = i == 0 ? src.Purified : src.Concentrated;
                var nf = i == 0 ? f : NewFront(grade, at);
                nf.grade = grade;
                nf.piece = p; nf.cell = p.Cell; nf.channel = ch;
                nf.fromA = false;               // arms run edge -> canister, so outflow is from the canister end
                nf.exitDir = p.WorldA(ch);
                nf.filterIn = false;
                nf.loopPending = false;
                nf.t = 0f;
                nf.dist = outDist;
                SetPath(nf);
                if (i == 0 && !grade.Same(src))
                {
                    // the purified stream changes color: swap the blob's material
                    var r = nf.blob.GetComponent<Renderer>();
                    if (r != null) r.sharedMaterial = Mats.LiquidMat(grade.liquidId, grade.Color, grade.Glow, 1f, 0.9f);
                }
                nf.spring.Squash(0.5f);
            }
            // a label over each outlet says what comes out of it and what it pays
            for (int i = 0; i < outs.Count; i++)
                OutletLabel.Create(p, p.WorldA(outs[i]), i == 0 ? src.Purified : src.Concentrated);
            Popup.Spawn(at + Vector3.up * 0.8f, "SPLIT!", Color.white, 44, 0.9f);
            Fx.Burst(at + Vector3.up * 0.2f, src.Purified.Color, src.Concentrated.Color, 14, 2.5f, 0.1f, 0.5f, -0.3f);
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
            if (ch >= 0 && !board.IsSpout(n))
            {
                // another stream is coming the other way through this channel: go in and meet it
                var other = fronts.Find(o => o != f && !o.spout && o.partner == null && o.piece == p && o.channel == ch);
                // a second liquid into a filter that's already got one (in this arm, or done with it): the canister shatters
                if (p.Type == PipeType.Filter && (other != null || p.Model.channels[ch].fill > 0f))
                {
                    BurstFilter(p, f.grade, other != null ? other.grade : f.grade);
                    return;
                }
                if (other != null) { Converge(f, other, p, ch, n, dir); return; }
            }
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
            if (f.piece != null) f.dist += f.piece.ChannelLength(f.channel);
            f.piece = p;
            f.channel = ch;
            f.cell = n;
            f.filterIn = p.Type == PipeType.Filter;
            f.fromA = f.filterIn || p.WorldA(ch) == DirUtil.Opposite(dir);
            f.exitDir = f.fromA ? p.WorldB(ch) : p.WorldA(ch);
            SetPath(f);
            p.FlowLocked = true;
            phase = Phase.Piece;
            f.t = 0f;
            f.spring.Squash(0.4f);
        }

        /// <summary>Patterned liquids: tell the channel how far along the flow it is, so the bands travel with it.</summary>
        static void SetPath(Front f)
        {
            if (f.piece != null && Mats.IsPatterned(f.grade.liquidId)) f.piece.SetChannelPath(f.channel, f.dist, f.fromA);
        }

        /// <summary>A stream entering a channel another stream is already filling from the other end.</summary>
        void Converge(Front f, Front other, PipePiece p, int ch, Vector2Int n, Dir dir)
        {
            step++;
            if (f.piece != null) f.dist += f.piece.ChannelLength(f.channel);
            f.piece = p;
            f.channel = ch;
            f.cell = n;
            f.filterIn = false;
            f.loopPending = false;
            f.fromA = p.WorldA(ch) == DirUtil.Opposite(dir); // a filter arm's 'a' end is its edge
            f.exitDir = f.fromA ? p.WorldB(ch) : p.WorldA(ch);
            f.partner = other;
            other.partner = f;
            p.FlowLocked = true;
            phase = Phase.Piece;
            f.t = 0f;
            f.spring.Squash(0.4f);
        }

        /// <summary>Two streams met head-on: the pipe bursts where they meet and the round is over.</summary>
        void BurstPipe(Front fa, Front fb, float atFromA)
        {
            var p = fa.piece;
            int ch = fa.channel;
            p.SetChannelFillTwoWay(ch, atFromA, 1f - atFromA, fa.grade, fb.grade);
            p.BurstChannel(ch, atFromA, fa.grade, fb.grade);
            Vector3 pos = p.ChannelPoint(ch, atFromA, true);
            Vector3 axis = p.ChannelPoint(ch, Mathf.Min(1f, atFromA + 0.05f), true) - p.ChannelPoint(ch, Mathf.Max(0f, atFromA - 0.05f), true);
            axis.y = 0f;
            EndInBurst(pos, axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.forward, fa.grade, fb.grade, false);
        }

        /// <summary>A filter fed two liquids: its glass canister shatters and the cap pops off.</summary>
        void BurstFilter(PipePiece p, LiquidGrade a, LiquidGrade b)
        {
            p.ShatterCanister();
            EndInBurst(p.CanisterPoint, Vector3.zero, a, b, true);
        }

        void EndInBurst(Vector3 pos, Vector3 axis, LiquidGrade a, LiquidGrade b, bool filter)
        {
            BurstAxis = axis;
            LastSpillBurst = true;
            LastBurstFilter = filter;
            // placeholder until merged grades have their own effects: the worse of the two
            SpillGrade = !a.water || b.water ? a : b;
            phase = Phase.Done;
            warning.gameObject.SetActive(false);
            foreach (var o in fronts.ToArray()) RemoveFront(o);
            Spilled?.Invoke(pos, Vector3.Cross(Vector3.up, axis));
        }

        /// <summary>How much of the pipe it's in the leading stream still has to fill (in pipes; the spout counts at
        /// its own fill time). The smallest across streams, so the estimate errs on the urgent side.</summary>
        float CurrentPipeLeft()
        {
            float best = 1f;
            foreach (var f in fronts)
            {
                float left = Mathf.Clamp01(1f - f.t);
                if (f.spout) left *= cfg.spoutFillSeconds / Mathf.Max(0.01f, cfg.secondsPerPiece);
                best = Mathf.Min(best, left);
            }
            return best;
        }

        /// <summary>One look-ahead path in CountAheadAll: its head is the channel it last claimed.</summary>
        class Walker
        {
            public PipePiece piece;
            public int ch = -1;
            public Vector2Int cell;
            public Dir dir;
            public int count;
            public bool splitNext, done, branched;
        }

        /// <summary>
        /// Connected, still-empty pipes ahead of the most urgent stream before it leaks (capped at max). Every stream
        /// walks ahead in lockstep, one channel per step (they all flow at the same speed), branching at filters, and
        /// claims each channel it reaches. Running into a claimed channel means two streams meet there, which is a
        /// burst, so two streams heading for each other only get half the shared run each.
        /// </summary>
        int CountAheadAll(int max)
        {
            if (fronts.Count == 0) return 99;
            var claims = new Dictionary<(PipePiece, int), Walker>();
            var walkers = new List<Walker>();
            foreach (var f in fronts)
            {
                if (f.partner != null) return 0; // already closing in on another stream
                if (f.spout) { walkers.Add(new Walker { cell = board.Spout.Cell, dir = board.Spout.Out }); continue; }
                if (f.piece == null) return 0;
                var w = new Walker { piece = f.piece, ch = f.channel, cell = f.cell, dir = f.exitDir, splitNext = f.filterIn };
                claims[(f.piece, f.channel)] = w;
                walkers.Add(w);
            }

            for (int guard = 0; guard < 64; guard++)
            {
                bool any = false;
                int live = walkers.Count;
                for (int i = 0; i < live; i++)
                {
                    var w = walkers[i];
                    if (w.done) continue;
                    if (w.count >= max) { w.done = true; continue; } // far enough ahead: no need to look further
                    any = true;
                    if (w.splitNext) SplitWalker(w, walkers, claims);
                    else StepWalker(w, claims);
                }
                if (!any) break;
            }

            int best = max;
            foreach (var w in walkers) if (!w.branched) best = Mathf.Min(best, w.count);
            return best;
        }

        /// <summary>Stops a walker at a claimed channel; if that channel is the other walker's head, they've met and it stops too.</summary>
        static void Collide(Walker w, Walker owner, PipePiece p, int ch)
        {
            w.done = true;
            if (owner != null && !owner.done && owner.piece == p && owner.ch == ch) owner.done = true;
        }

        void StepWalker(Walker w, Dictionary<(PipePiece, int), Walker> claims)
        {
            var n = w.cell + DirUtil.Offset(w.dir);
            var p = board.IsSpout(n) ? null : board.Get(n);
            int ch = p != null && p.State == PieceState.Placed ? p.ChannelWithOpening(DirUtil.Opposite(w.dir)) : -1;
            if (ch < 0 || p.Model.channels[ch].fill > 0f) { w.done = true; return; } // it would leak here
            if (claims.TryGetValue((p, ch), out var owner)) { Collide(w, owner, p, ch); return; }
            claims[(p, ch)] = w;
            w.piece = p;
            w.ch = ch;
            w.cell = n;
            w.count++;
            if (p.Type == PipeType.Filter) { w.splitNext = true; return; }
            bool a = p.WorldA(ch) == DirUtil.Opposite(w.dir);
            w.dir = a ? p.WorldB(ch) : p.WorldA(ch);
        }

        /// <summary>A walker at a filter's canister carries on down both outlets (the outlet arms take a step but aren't counted).</summary>
        void SplitWalker(Walker w, List<Walker> walkers, Dictionary<(PipePiece, int), Walker> claims)
        {
            var p = w.piece;
            w.splitNext = false;
            for (int i = 0; i < p.Model.channels.Count; i++)
            {
                if (i == w.ch) continue;
                if (p.Model.channels[i].fill > 0f) { w.done = true; return; } // already wet: it would overflow right there
                if (claims.TryGetValue((p, i), out var owner)) { Collide(w, owner, p, i); return; }
                var nw = new Walker { piece = p, ch = i, cell = p.Cell, dir = p.WorldA(i), count = w.count };
                claims[(p, i)] = nw;
                walkers.Add(nw);
            }
            w.done = true;
            w.branched = true; // its outlets carry on in its place
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
