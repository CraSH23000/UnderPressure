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
        float S;
        bool flying;
        bool highlighted;
        float wiggle;
        readonly HashSet<Dir> joinedLocal = new HashSet<Dir>();
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
                    Body.isKinematic = true; col.enabled = true; gameObject.layer = Type == PipeType.Cross ? Layers.PlacedWalkable : Layers.PlacedPipes;
                    col.size = new Vector3(0.94f * S, TopHeight + 0.05f, 0.94f * S);
                    col.center = new Vector3(0, (TopHeight + 0.05f) * 0.5f, 0);
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
            if (s != PieceState.Placed) { Connected = false; joinedLocal.Clear(); }
            RefreshAttachments(false);
            ClearHighlights();
        }

        public void SetCell(Vector2Int c) => Cell = c;

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

        public void SetChannelFill(int index, float fill, bool fromA)
        {
            var ch = Model.channels[index];
            ch.fill = Mathf.Clamp01(fill);
            ch.fromA = fromA;
            int n = ch.segments.Length;
            for (int j = 0; j < n; j++)
            {
                int pos = fromA ? j : n - 1 - j;
                float f = Mathf.Clamp01(ch.fill * n - pos);
                var mat = Mats.FillMat(Mathf.RoundToInt(f * (Mats.FillLevels - 1)));
                foreach (var r in ch.segments[j])
                    if (r.sharedMaterial != mat) r.sharedMaterial = mat;
            }
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
            foreach (var kv in Model.clamps) ShowPart(kv.Value, placed && Connected && joinedLocal.Contains(kv.Key), animate, false);
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
