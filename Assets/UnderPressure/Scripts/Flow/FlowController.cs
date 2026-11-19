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

        Phase phase = Phase.Idle;
        float t;
        PipePiece piece;
        int channel;
        bool fromA, loopPending;
        Dir exitDir;
        Vector2Int cell;

        GridBoard board;
        GameConfig cfg;
        Transform front, warning;
        Springy frontSpring;
        ParticleSystem trail;
        Material warnMat;

        public bool Running => phase == Phase.Spout || phase == Phase.Piece;
        public bool Finished => phase == Phase.Done;

        /// <summary>Connected, still-empty pipes ahead of the liquid before it would leak (capped at a small number).</summary>
        public int PiecesAhead { get; private set; } = 99;

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
            float S = cfg.tileSize;
            var glow = Mats.Lit(Mats.Liquid, 0.9f, 0f, Mats.LiquidGlow);
            front = Prims.Make(PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one, glow, null, "LiquidFront").transform;
            frontSpring = front.gameObject.AddComponent<Springy>();
            frontSpring.stiffness = 200f;
            frontSpring.damping = 6f;
            frontSpring.SetBase(PipeBuilder.TubeRadius(S) * 2.5f, true);
            front.gameObject.SetActive(false);

            warnMat = new Material(Mats.Lit(Mats.Danger, 0.5f, 0f, Mats.Danger));
            warning = Prims.Make(PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one * 0.5f, warnMat, null, "SpillWarning").transform;
            warning.gameObject.SetActive(false);
        }

        public void Begin()
        {
            phase = Phase.Spout;
            t = 0f;
            front.gameObject.SetActive(true);
            front.position = board.Spout.PointAt(0f);
            trail = Fx.Trail(Mats.Liquid, new Color(0.6f, 1f, 0.7f), 18f, 0.14f);
            trail.transform.SetParent(transform, true);
        }

        void Update()
        {
            PiecesAhead = Running ? CountAhead(Mathf.Max(1, cfg.panicPiecesAhead) + 2) : 99;
            if (!Running) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (phase == Phase.Spout)
            {
                t += dt * SpeedMultiplier / cfg.spoutFillSeconds;
                board.Spout.SetFill(t);
                MoveFront(board.Spout.PointAt(Mathf.Min(t, 1f)), dt);
                UpdateWarning(board.Spout.Cell, board.Spout.Out, t);
                if (t >= 1f) Advance(board.Spout.Cell, board.Spout.Out);
                return;
            }

            if (piece == null) { Spill(cell, exitDir); return; } // defensive: piece destroyed under us

            t += dt * SpeedMultiplier / cfg.secondsPerPiece;
            piece.SetChannelFill(channel, Mathf.Min(t, 1f), fromA);
            MoveFront(piece.ChannelPoint(channel, Mathf.Min(t, 1f), fromA), dt);

            if (loopPending && t >= 0.5f)
            {
                loopPending = false;
                int open = firstPass.TryGetValue(piece, out int f) ? f : step;
                int nested = 0;
                foreach (var l in closedLoops) if (l.open > open) nested++;
                closedLoops.Add((open, step));
                LoopCompleted?.Invoke(piece, nested);
            }
            UpdateWarning(cell, exitDir, t);

            if (t >= 1f)
            {
                var done = piece;
                PieceFilled?.Invoke(done);
                Advance(cell, exitDir);
            }
        }

        void MoveFront(Vector3 p, float dt)
        {
            front.position = p;
            // wobble like a blob of goo
            if (UnityEngine.Random.value < dt * 6f) frontSpring.Squash(UnityEngine.Random.Range(-0.15f, 0.15f));
            if (trail != null) trail.transform.position = p;
        }

        void Advance(Vector2Int from, Dir dir)
        {
            var n = from + DirUtil.Offset(dir);
            var p = board.Get(n);
            int ch = p != null && p.State == PieceState.Placed ? p.ChannelWithOpening(DirUtil.Opposite(dir)) : -1;
            if (ch < 0 || board.IsSpout(n) || p.Model.channels[ch].fill > 0f)
            {
                Spill(from, dir);
                return;
            }

            loopPending = false;
            for (int i = 0; i < p.Model.channels.Count; i++)
                if (i != ch && p.Model.channels[i].Full) loopPending = true;

            step++;
            if (!loopPending && p.Model.channels.Count > 1) firstPass[p] = step; // a loop opens here
            piece = p;
            channel = ch;
            cell = n;
            fromA = p.WorldA(ch) == DirUtil.Opposite(dir);
            exitDir = fromA ? p.WorldB(ch) : p.WorldA(ch);
            p.FlowLocked = true;
            phase = Phase.Piece;
            t = 0f;
            frontSpring.Squash(0.4f);
        }

        /// <summary>Walk the connected path ahead (without changing anything) and count pipes until it would leak.</summary>
        int CountAhead(int max)
        {
            Vector2Int cur;
            Dir dir;
            if (phase == Phase.Spout) { cur = board.Spout.Cell; dir = board.Spout.Out; }
            else { cur = cell; dir = exitDir; }
            var used = new HashSet<(PipePiece, int)>();
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
                bool a = p.WorldA(ch) == DirUtil.Opposite(dir);
                dir = a ? p.WorldB(ch) : p.WorldA(ch);
                cur = n;
            }
            return count;
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

        void Spill(Vector2Int from, Dir dir)
        {
            phase = Phase.Done;
            front.gameObject.SetActive(false);
            warning.gameObject.SetActive(false);
            if (trail != null)
            {
                trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                Destroy(trail.gameObject, 1f);
            }
            Spilled?.Invoke(EdgePoint(from, dir), DirUtil.ToVector3(dir));
        }
    }
}
