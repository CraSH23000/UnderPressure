using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// A secret door in the right wall: three columns of wall panels slide down into a floor slot, one after another,
    /// opening onto a hidden room the same size as the chamber, and an EXIT sign slides out of the wall beside it and
    /// flickers on. The teleporters live in there. The room stays switched off until the door starts opening, so it
    /// can't be spotted over the wall.
    /// </summary>
    public class SecretDoor : MonoBehaviour
    {
        public static SecretDoor Instance { get; private set; }

        public bool IsOpen => state == State.Open;
        public bool IsClosed => state == State.Closed;
        /// <summary>Someone is standing in the hidden room right now.</summary>
        public bool AnyoneInside { get; private set; }
        /// <summary>The first time anyone walks into the hidden room (for the teleporters to come up).</summary>
        public event System.Action Entered;
        /// <summary>Where you can walk in the hidden room (x range in x/xMax, z range in y/yMax).</summary>
        public Rect Room { get; private set; }
        /// <summary>Things that belong in the hidden room go under this (switched off until the door opens).</summary>
        public Transform RoomRoot => roomRoot;
        /// <summary>The doorway's z range.</summary>
        public float DoorZ0 => z0;
        public float DoorZ1 => z1;
        /// <summary>The hidden room's far (right) wall, inside face.</summary>
        public float FarWallX { get; private set; }
        /// <summary>How far right of the main room the hidden room sits: a spot in the chamber plus this is the same spot in there.</summary>
        public float RoomOffsetX { get; private set; }
        /// <summary>The way through the wall (x/xMax = x, y/yMax = z).</summary>
        Rect Doorway => Rect.MinMaxRect(wallX - 0.3f, z0, wallX + 0.8f, z1);

        /// <summary>Open (or opening) and p is in the hidden room or its doorway: somewhere a ghost may float.</summary>
        public bool IsWalkable(Vector3 p)
        {
            if (state == State.Closed) return false;
            var q = new Vector2(p.x, p.z);
            return Room.Contains(q) || Doorway.Contains(q);
        }

        enum State { Closed, Opening, Open }
        State state;
        float t, autoOpen = -1f;
        bool entered;

        float wallX, z0, z1, mainLeft, zFront, zBack;
        Transform roomRoot, sign;
        Transform[] columns;
        bool[] colStarted, colLanded;
        Vector3 signIn, signOut;
        Material signFace;
        Collider blocker;
        bool signStarted, signLanded;

        const float ColumnDelay = 0.22f, SlideTime = 1.1f, SignDelay = 0.25f, SignTime = 0.5f, FlickerTime = 0.6f;
        static readonly Color ExitGreen = new Color(0.1f, 0.85f, 0.35f);

        float DoorH => ChamberBuilder.SecretDoorH;
        float SignStart => (columns.Length - 1) * ColumnDelay + SlideTime + SignDelay;

        /// <param name="wallX">The right wall's inside face (the room's xRight).</param>
        /// <param name="mainLeft">The main room's left wall: the hidden room copies the main room's size.</param>
        /// <param name="openAfter">Seconds until it opens by itself (negative: wait for Open()).</param>
        public static SecretDoor Create(Transform parent, float wallX, float z0, float z1, float mainLeft, float zFront, float zBack, float openAfter = -1f)
        {
            var go = new GameObject("SecretDoor");
            go.transform.SetParent(parent, false);
            var d = go.AddComponent<SecretDoor>();
            d.wallX = wallX; d.z0 = z0; d.z1 = z1;
            d.mainLeft = mainLeft; d.zFront = zFront; d.zBack = zBack;
            d.autoOpen = openAfter;
            d.Build();
            Instance = d;
            return d;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Slide the panels away (does nothing if it's already open or opening).</summary>
        public void Open()
        {
            if (state != State.Closed) return;
            state = State.Opening;
            t = 0f;
            roomRoot.gameObject.SetActive(true);
            Game.Cam?.AddTrauma(0.12f);
            Sfx.Play(Sfx.I != null ? Sfx.I.clunk : null, 0.7f, 0.6f, 0f);
        }

        /// <summary>Straight to open, no show (the room was rebuilt bigger around a door that was already open).</summary>
        public void OpenInstantly()
        {
            if (state == State.Open) return;
            state = State.Open;
            autoOpen = -1f;
            roomRoot.gameObject.SetActive(true);
            for (int c = 0; c < columns.Length; c++) { colStarted[c] = colLanded[c] = true; columns[c].gameObject.SetActive(false); }
            blocker.enabled = false;
            signStarted = signLanded = true;
            sign.gameObject.SetActive(true);
            sign.localPosition = signOut;
            signFace.SetColor("_EmissionColor", ExitGreen * 2.2f);
            Mats.SetBase(signFace, ExitGreen);
        }

        // ================================================================== build

        void Build()
        {
            float P = ChamberBuilder.PanelSize, gap = ChamberBuilder.Gap, wallH = ChamberBuilder.WallH;
            float zc = (z0 + z1) * 0.5f, w = z1 - z0;
            var rng = new System.Random(1234);
            var ink = Mats.Lit(Theme.Ink, 0.1f);
            var trim = Mats.Lit(Theme.WallTrim, 0.4f, 0.4f);

            // the slot the panels sink into (hidden under the closed door)
            Prims.Make(PrimitiveType.Cube, transform, new Vector3(wallX + 0.25f, -0.01f, zc), new Vector3(0.6f, 0.04f, w), ink, null, "DoorSlot");

            // the door: one column of three wall panels (plus its bit of kick plate) per panel width
            columns = new Transform[3];
            colStarted = new bool[3]; colLanded = new bool[3];
            for (int c = 0; c < columns.Length; c++)
            {
                var col = Prims.Empty("DoorColumn" + c, transform, new Vector3(wallX, 0f, z0 + (c + 0.5f) * P)).transform;
                for (int r = 0; r < 3; r++)
                    Prims.Make(PrimitiveType.Cube, col, new Vector3(0.12f, r * P + P * 0.5f, 0f), new Vector3(0.16f, P - gap, P - gap), ChamberBuilder.PanelMat(rng), null, "WallPanel");
                Prims.Make(PrimitiveType.Cube, col, new Vector3(0f, 0.15f, 0f), new Vector3(0.14f, 0.3f, P), trim, null, "KickPlate");
                columns[c] = col;
            }
            var b = new GameObject("DoorBlocker");
            b.transform.SetParent(transform, false);
            b.transform.position = new Vector3(wallX, 2f, zc);
            var bc = b.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.5f, 4f, w);
            blocker = bc;

            BuildRoom(P, wallH, rng);
            BuildSign();
        }

        void BuildRoom(float P, float wallH, System.Random rng)
        {
            // a copy of the chamber's footprint on the other side of the right wall (open at the front, like the chamber)
            float x0 = wallX + 0.5f, x1 = x0 + (wallX - mainLeft);
            float rz0 = zFront, rz1 = zBack;
            RoomOffsetX = x0 - mainLeft;
            FarWallX = x1;
            Room = Rect.MinMaxRect(x0 + 0.25f, rz0 + 0.3f, x1 - 0.25f, rz1 - 0.3f);
            roomRoot = Prims.Empty("HiddenRoom", transform, Vector3.zero).transform;

            var seam = Mats.Lit(Theme.FloorSeam, 0.2f);
            var panel = Mats.Lit(Theme.Floor, 0.55f);
            var panelAlt = Mats.Lit(Color.Lerp(Theme.Floor, Theme.FloorSeam, 0.12f), 0.5f);
            var backing = Mats.Lit(Theme.WallBacking, 0.2f);
            var trim = Mats.Lit(Theme.WallTrim, 0.4f, 0.4f);

            // floor (from the door slot to the far wall)
            Prims.Make(PrimitiveType.Cube, roomRoot, new Vector3((wallX + 0.55f + x1) * 0.5f, -0.08f, (rz0 + rz1) * 0.5f), new Vector3(x1 - wallX - 0.55f, 0.1f, rz1 - rz0), seam, null, "FloorBase");
            const float FP = 2f;
            for (float x = x0; x < x1 - 0.01f; x += FP)
            for (float z = rz0; z < rz1 - 0.01f; z += FP)
            {
                float pw = Mathf.Min(FP, x1 - x), pd = Mathf.Min(FP, rz1 - z);
                Prims.Make(PrimitiveType.Cube, roomRoot, new Vector3(x + pw * 0.5f, -0.04f, z + pd * 0.5f), new Vector3(pw - 0.06f, 0.08f, pd - 0.06f),
                    rng.NextDouble() < 0.15 ? panelAlt : panel, null, "FloorPanel");
            }
            // a strip of floor through the doorway, over the slot's far half
            Prims.Make(PrimitiveType.Cube, roomRoot, new Vector3(wallX + 0.4f, -0.04f, (z0 + z1) * 0.5f), new Vector3(0.24f, 0.08f, z1 - z0 - 0.06f), panel, null, "Threshold");

            // walls: back and right like the chamber's, plus panels on this side of the shared wall (round the doorway)
            Prims.Make(PrimitiveType.Cube, roomRoot, new Vector3((x0 + x1) * 0.5f, wallH * 0.5f, rz1 + 0.35f), new Vector3(x1 - x0 + 0.8f, wallH, 0.3f), backing, null, "BackWallBacking");
            Prims.Make(PrimitiveType.Cube, roomRoot, new Vector3(x1 + 0.35f, wallH * 0.5f, (rz0 + rz1) * 0.5f), new Vector3(0.3f, wallH, rz1 - rz0 + 0.4f), backing, null, "SideWallBacking");
            for (float y = 0f; y < wallH - 0.01f; y += P)
            {
                for (float x = x0; x < x1 - 0.01f; x += P)
                {
                    float pw = Mathf.Min(P, x1 - x);
                    RoomPanel(new Vector3(x + pw * 0.5f, y + P * 0.5f, rz1 + 0.12f), new Vector3(pw, P, 0.16f), rng);
                }
                for (float z = rz0; z < rz1 - 0.01f; z += P)
                {
                    float pd = Mathf.Min(P, rz1 - z);
                    RoomPanel(new Vector3(x1 + 0.12f, y + P * 0.5f, z + pd * 0.5f), new Vector3(0.16f, P, pd), rng);
                    if (y < DoorH - 0.01f && z + pd > z0 + 0.01f && z < z1 - 0.01f) continue; // the doorway
                    RoomPanel(new Vector3(x0 + 0.08f, y + P * 0.5f, z + pd * 0.5f), new Vector3(0.16f, P, pd), rng);
                }
            }
            Prims.Make(PrimitiveType.Cube, roomRoot, new Vector3((x0 + x1) * 0.5f, 0.15f, rz1), new Vector3(x1 - x0, 0.3f, 0.14f), trim, null, "KickPlate");
            Prims.Make(PrimitiveType.Cube, roomRoot, new Vector3(x1, 0.15f, (rz0 + rz1) * 0.5f), new Vector3(0.14f, 0.3f, rz1 - rz0), trim, null, "KickPlate");
            Prims.Make(PrimitiveType.Cube, roomRoot, new Vector3(x0 + 0.16f, 0.15f, (rz0 + z0) * 0.5f), new Vector3(0.14f, 0.3f, z0 - rz0), trim, null, "KickPlate");
            Prims.Make(PrimitiveType.Cube, roomRoot, new Vector3(x0 + 0.16f, 0.15f, (z1 + rz1) * 0.5f), new Vector3(0.14f, 0.3f, rz1 - z1), trim, null, "KickPlate");
            ChamberBuilder.LightStrip(roomRoot, new Vector3((x0 + x1) * 0.5f, wallH - 0.3f, rz1 - 0.02f), new Vector3(x1 - x0 - 1f, 0.14f, 0.1f), rng);
            ChamberBuilder.LightStrip(roomRoot, new Vector3(x1 - 0.02f, wallH - 0.3f, (rz0 + rz1) * 0.5f), new Vector3(0.1f, 0.14f, rz1 - rz0 - 1f), rng);

            // invisible walls round the walkable floor (the doorway stays open)
            const float h = 4f, th = 0.5f;
            var r = Room;
            AddWall(new Vector3(r.xMax + th * 0.5f, h * 0.5f, r.center.y), new Vector3(th, h, r.height + 2f));
            AddWall(new Vector3(r.center.x, h * 0.5f, r.yMax + th * 0.5f), new Vector3(r.width + 2f, h, th));
            AddWall(new Vector3(r.center.x, h * 0.5f, r.yMin - th * 0.5f), new Vector3(r.width + 2f, h, th));
            AddWall(new Vector3(x0, h * 0.5f, (r.yMin - 1f + z0) * 0.5f), new Vector3(th, h, z0 - r.yMin + 1f));
            AddWall(new Vector3(x0, h * 0.5f, (z1 + r.yMax + 1f) * 0.5f), new Vector3(th, h, r.yMax + 1f - z1));

            roomRoot.gameObject.SetActive(false);
        }

        void RoomPanel(Vector3 pos, Vector3 size, System.Random rng)
        {
            float gap = ChamberBuilder.Gap;
            var s = new Vector3(size.x > 0.2f ? size.x - gap : size.x, size.y - gap, size.z > 0.2f ? size.z - gap : size.z);
            Prims.Make(PrimitiveType.Cube, roomRoot, pos, s, ChamberBuilder.PanelMat(rng), null, "WallPanel");
        }

        void AddWall(Vector3 pos, Vector3 size)
        {
            var go = new GameObject("InvisibleWall");
            go.transform.SetParent(roomRoot, false);
            go.transform.position = pos;
            go.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>A double-sided EXIT blade sign on the wall just south of the door, tucked inside the wall to start.</summary>
        void BuildSign()
        {
            const float len = 1.0f, ht = 0.44f, thick = 0.12f;
            float y = 3.4f, z = z0 - 0.55f;
            signOut = new Vector3(wallX - len * 0.5f - 0.04f, y, z);
            signIn = new Vector3(wallX + len * 0.5f + 0.06f, y, z);
            sign = Prims.Empty("ExitSign", transform, signIn).transform;
            Prims.Make(PrimitiveType.Cube, sign, Vector3.zero, new Vector3(len, ht, thick), Mats.Lit(Theme.Ink, 0.5f, 0.3f), null, "Body");
            signFace = new Material(Mats.Lit(ExitGreen * 0.3f, 0.6f, 0f, Color.black));
            for (int s = -1; s <= 1; s += 2)
            {
                Prims.Make(PrimitiveType.Cube, sign, new Vector3(0f, 0f, s * (thick * 0.5f + 0.004f)), new Vector3(len - 0.08f, ht - 0.08f, 0.006f), signFace, null, "Face");
                var cgo = new GameObject("ExitText", typeof(RectTransform));
                cgo.transform.SetParent(sign, false);
                cgo.transform.localPosition = new Vector3(0f, 0f, s * (thick * 0.5f + 0.012f));
                cgo.transform.localRotation = s < 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
                var canvas = cgo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                cgo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
                var rt = (RectTransform)cgo.transform;
                rt.sizeDelta = new Vector2(200, 80);
                rt.localScale = Vector3.one * ((len - 0.1f) / 200f);
                var tgo = new GameObject("Text", typeof(RectTransform));
                var tr = (RectTransform)tgo.transform;
                tr.SetParent(rt, false);
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
                tr.offsetMin = tr.offsetMax = Vector2.zero;
                var txt = tgo.AddComponent<Text>();
                txt.font = Popup.Font;
                txt.text = "EXIT";
                txt.fontSize = 62;
                txt.fontStyle = FontStyle.Bold;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = Color.white;
                txt.raycastTarget = false;
            }
            sign.gameObject.SetActive(false); // inside the wall (and poking into the hidden room) until it's needed
        }

        // ================================================================== animation

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (state == State.Closed && autoOpen >= 0f)
            {
                autoOpen -= dt;
                if (autoOpen <= 0f) { autoOpen = -1f; Open(); }
            }
            if (state == State.Opening) Animate(dt);
            if (state != State.Closed) CheckInside();
        }

        void Animate(float dt)
        {
            t += dt;
            for (int c = 0; c < columns.Length; c++)
            {
                float tc = t - c * ColumnDelay;
                if (tc < 0f) continue;
                if (!colStarted[c])
                {
                    colStarted[c] = true;
                    Sfx.Play(Sfx.I != null ? Sfx.I.clank : null, 0.5f, 0.8f + c * 0.08f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.creak : null, 0.35f, 0.9f + c * 0.1f);
                }
                float u = Mathf.Clamp01(tc / SlideTime);
                // a little hop as the latch lets go, then down into the floor
                float y = u < 0.15f ? Mathf.Sin(u / 0.15f * Mathf.PI) * 0.05f
                    : -(DoorH + 0.4f) * Juice.InCubic(Mathf.InverseLerp(0.15f, 1f, u));
                float jitter = u > 0.15f && u < 1f ? Mathf.Sin(t * 90f + c) * 0.008f : 0f;
                columns[c].localPosition = new Vector3(wallX + jitter, y, columns[c].localPosition.z);
                if (u >= 1f && !colLanded[c])
                {
                    colLanded[c] = true;
                    columns[c].gameObject.SetActive(false);
                    Sfx.Play(Sfx.I != null ? Sfx.I.thunk : null, 0.6f, 0.75f);
                    Game.Cam?.AddTrauma(0.08f);
                    Fx.Burst(new Vector3(wallX, 0.1f, columns[c].position.z), new Color(0.75f, 0.77f, 0.8f), new Color(0.55f, 0.56f, 0.58f), 10, 1.8f, 0.14f, 0.6f, -0.05f);
                }
            }
            if (colLanded[columns.Length - 1] && blocker.enabled) blocker.enabled = false;

            // the EXIT sign slides out of the wall, then flickers on
            float ts = t - SignStart;
            if (ts < 0f) return;
            if (!signStarted)
            {
                signStarted = true;
                sign.gameObject.SetActive(true);
                Sfx.Play(Sfx.I != null ? Sfx.I.whoosh : null, 0.4f, 1.3f);
            }
            float k = Mathf.Clamp01(ts / SignTime);
            sign.localPosition = Vector3.LerpUnclamped(signIn, signOut, Juice.OutBack(k));
            if (k >= 1f && !signLanded)
            {
                signLanded = true;
                Sfx.Play(Sfx.I != null ? Sfx.I.clunk : null, 0.5f, 1.2f);
                Sfx.Play(Sfx.I != null ? Sfx.I.hum : null, 0.3f, 1.4f);
            }
            float tf = ts - SignTime;
            if (tf < 0f) return;
            bool lit = tf >= FlickerTime || Mathf.PerlinNoise(tf * 14f, 3.7f) > 0.45f;
            signFace.SetColor("_EmissionColor", lit ? ExitGreen * 2.2f : Color.black);
            Mats.SetBase(signFace, lit ? ExitGreen : ExitGreen * 0.3f);
            if (tf >= FlickerTime) state = State.Open;
        }

        void CheckInside()
        {
            bool inside = false;
            foreach (var p in Game.Players)
            {
                if (p == null) continue;
                var q = p.transform.position;
                if (Room.Contains(new Vector2(q.x, q.z))) { inside = true; break; }
            }
            AnyoneInside = inside;
            if (inside && !entered)
            {
                entered = true;
                Entered?.Invoke();
            }
        }
    }
}
