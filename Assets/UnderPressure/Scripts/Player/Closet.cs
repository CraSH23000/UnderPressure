using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    public enum ClosetKind { BioReactor, SprayBooth, Wardrobe, Locker }

    /// <summary>
    /// A cosmetic booth set into the back wall. Walk up, press Use, and you're inside: up/down picks a row,
    /// left/right (or rotate) changes it live. Use or Jump steps back out. One critter at a time.
    /// Local space: origin = centre of the doorway at floor level on the wall line, +Z goes into the wall.
    /// </summary>
    public class Closet : MonoBehaviour
    {
        public static readonly List<Closet> All = new List<Closet>();

        public const float Width = 1.6f, Height = 1.6f, HeaderH = 0.35f, Depth = 1.3f;

        public ClosetKind Kind { get; private set; }
        /// <summary>Lockers (at the base) belong to one player slot: all three closets in one. -1 = anyone.</summary>
        public int Owner { get; private set; } = -1;
        bool HasTube => Kind == ClosetKind.BioReactor || Kind == ClosetKind.Locker;
        static readonly ClosetKind[] ShopKinds = { ClosetKind.BioReactor, ClosetKind.SprayBooth, ClosetKind.Wardrobe };

        /// <summary>Can this player use it? (Lockers are only for their owner.)</summary>
        public bool UsableBy(PlayerController p) => Owner < 0 || (p != null && p.Index == Owner);
        public PlayerController Occupant { get; private set; }
        public Vector3 Entrance => transform.TransformPoint(new Vector3(0, 0, -0.55f));
        public Vector3 Inside => transform.TransformPoint(new Vector3(0, 0, Depth * 0.5f));
        public Vector3 PromptAnchor => transform.TransformPoint(new Vector3(0, Height + HeaderH + 0.25f, -0.2f));
        public Vector3 Facing => -transform.forward;

        public string Title => Kind == ClosetKind.Locker ? $"P{Owner + 1} LOCKER" :
            Kind == ClosetKind.BioReactor ? "BIO-REACTOR" : Kind == ClosetKind.SprayBooth ? "SPRAY BOOTH" : "WARDROBE";

        /// <summary>The rows this player sees. Secret rows (arms, legs) only show up once something's unlocked for them.</summary>
        public Part[] RowsFor(PlayerController p)
        {
            if (Kind == ClosetKind.BioReactor) return new[] { Part.Species, Part.Size };
            if (Kind == ClosetKind.SprayBooth) return new[] { Part.Color };
            var rows = Kind == ClosetKind.Locker
                ? new List<Part> { Part.Player, Part.Species, Part.Size, Part.Color, Part.Hat, Part.Torso }
                : new List<Part> { Part.Hat, Part.Torso };
            if (Progress.AnyUnlocked(p.Index, Part.Arms)) rows.Add(Part.Arms);
            if (Progress.AnyUnlocked(p.Index, Part.Legs)) rows.Add(Part.Legs);
            rows.Add(Part.Feet);
            return rows.ToArray();
        }

        public static string RowLabel(Part p) => p == Part.Color ? "COLOR" : p == Part.Player ? "PLAYER" : p.ToString().ToUpperInvariant();

        // visuals
        Transform tube, liquid, header;
        Transform[] nozzles = new Transform[0];
        Transform[] swatches = new Transform[0];
        Material liquidMat, lampMat, headerMat, stripeMat;
        Color shownOwner = new Color(-1f, 0f, 0f);
        float tubeY, tubeTarget, fill, fillTarget, lampPulse;
        PlayerController pendingRebuild;
        NewBadge badge;
        float badgePoll;
        CritterLook pendingLook;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        public static Closet Create(Transform parent, ClosetKind kind, Vector3 doorCenter, int owner = -1)
        {
            var go = new GameObject(kind == ClosetKind.Locker ? "Locker_P" + (owner + 1) : "Closet_" + kind);
            go.transform.SetParent(parent, false);
            go.transform.position = doorCenter;
            var c = go.AddComponent<Closet>();
            c.Kind = kind;
            c.Owner = kind == ClosetKind.Locker ? owner : -1;
            c.Build();
            return c;
        }

        // ================================================================== build

        void Build()
        {
            float w = Width, h = Height, d = Depth;
            var t = transform;
            var trim = Mats.Lit(Theme.WallTrim, 0.5f, 0.5f);
            Color innerCol = Kind == ClosetKind.Locker ? new Color(0.3f, 0.33f, 0.36f) :
                Kind == ClosetKind.BioReactor ? new Color(0.22f, 0.26f, 0.28f) :
                Kind == ClosetKind.SprayBooth ? new Color(0.86f, 0.88f, 0.9f) : new Color(0.42f, 0.3f, 0.22f);
            var inner = Mats.Lit(Color.Lerp(innerCol, Theme.Stain, Theme.Grime * 0.35f), Kind == ClosetKind.Wardrobe ? 0.25f : 0.6f);

            // the box
            Prims.Make(PrimitiveType.Cube, t, new Vector3(0, h * 0.5f, d + 0.05f), new Vector3(w + 0.2f, h + 0.1f, 0.1f), inner, null, "Back");
            for (int s = -1; s <= 1; s += 2)
                Prims.Make(PrimitiveType.Cube, t, new Vector3(s * (w * 0.5f + 0.05f), h * 0.5f, (d + 0.1f) * 0.5f), new Vector3(0.1f, h + 0.1f, d + 0.1f), inner, null, "Side");
            // sides and ceiling start behind the door frame's front, so no faces line up with the posts (no flicker)
            Prims.Make(PrimitiveType.Cube, t, new Vector3(0, h + 0.05f, (d + 0.1f) * 0.5f), new Vector3(w + 0.2f, 0.1f, d + 0.1f), inner, null, "Ceiling");
            Prims.Make(PrimitiveType.Cube, t, new Vector3(0, 0.01f, d * 0.5f), new Vector3(w, 0.02f, d + 0.04f), Mats.Lit(new Color(0.3f, 0.31f, 0.33f), 0.5f, 0.6f), null, "FloorGrate");
            for (int i = -3; i <= 3; i++)
                Prims.Make(PrimitiveType.Cube, t, new Vector3(i * 0.2f, 0.022f, d * 0.5f + 0.02f), new Vector3(0.03f, 0.006f, d - 0.1f), trim, null, "Slat");
            // ceiling lamp
            lampMat = Kind == ClosetKind.Locker ? new Material(Mats.Lit(Color.white, 0.9f, 0f, LampColor() * 1.6f)) // its own: it follows the player color
                : Mats.Lit(Color.white, 0.9f, 0f, LampColor() * 1.6f);
            Prims.Make(PrimitiveType.Cube, t, new Vector3(0, h - 0.02f, d * 0.5f), new Vector3(w * 0.7f, 0.04f, 0.18f), lampMat, null, "Lamp");

            // door frame + header with the name on it
            for (int s = -1; s <= 1; s += 2)
                Prims.Make(PrimitiveType.Cube, t, new Vector3(s * (w * 0.5f + 0.1f), h * 0.5f, 0.02f), new Vector3(0.16f, h, 0.22f), trim, null, "Post"); // stops under the header (no shared faces to flicker)
            headerMat = Kind == ClosetKind.Locker ? new Material(Mats.Lit(OwnerColor * 0.85f, 0.5f, 0.2f, OwnerColor * 0.25f)) : trim;
            header = Prims.Make(PrimitiveType.Cube, t, new Vector3(0, h + HeaderH * 0.5f, 0.02f), new Vector3(w + 0.39f, HeaderH, 0.25f), headerMat, null, "Header").transform;
            Label(t, Title, new Vector3(0, h + HeaderH * 0.5f, -0.12f), w + 0.2f, Kind == ClosetKind.Locker ? Color.white : new Color(0.15f, 0.16f, 0.18f));
            // hazard plate on the floor in front of the door
            Prims.Make(PrimitiveType.Cube, t, new Vector3(0, 0.018f, -0.4f), new Vector3(w, 0.012f, 0.5f), Mats.Lit(new Color(0.95f, 0.75f, 0.1f), 0.4f), null, "Hazard");
            for (int i = 0; i < 6; i++)
                Prims.Make(PrimitiveType.Cube, t, new Vector3(-0.6f + i * 0.24f, 0.026f, -0.4f), new Vector3(0.08f, 0.006f, 0.45f),
                    Mats.Lit(new Color(0.08f, 0.08f, 0.09f), 0.4f), Quaternion.Euler(0, 35f, 0), "Stripe");

            badge = NewBadge.Create(t, transform.TransformPoint(new Vector3(0, h + HeaderH + 0.75f, -0.35f)));

            switch (Kind)
            {
                case ClosetKind.BioReactor: BuildReactor(); break;
                case ClosetKind.SprayBooth: BuildBooth(); break;
                case ClosetKind.Wardrobe: BuildWardrobe(); break;
                case ClosetKind.Locker: BuildLocker(); break;
            }
        }

        static readonly Color Unclaimed = new Color(0.42f, 0.44f, 0.48f);

        /// <summary>The owner's player color, or grey while nobody has joined in that slot.</summary>
        Color OwnerColor
        {
            get
            {
                if (Owner < 0) return Color.white;
                foreach (var pl in Game.Players) if (pl != null && pl.Index == Owner) return pl.Color;
                return Unclaimed;
            }
        }

        bool OwnerHere
        {
            get
            {
                foreach (var pl in Game.Players) if (pl != null && pl.Index == Owner) return true;
                return false;
            }
        }

        /// <summary>Repaint a locker's header, door stripes and lamp in its owner's color (grey and dark when unclaimed).</summary>
        void ApplyOwnerColor()
        {
            Color c = OwnerColor;
            bool here = OwnerHere;
            shownOwner = c;
            if (headerMat != null) { Mats.SetBase(headerMat, c * 0.85f); Mats.SetEmission(headerMat, here ? c * 0.25f : Color.black); }
            if (stripeMat != null) { Mats.SetBase(stripeMat, here ? c : new Color(0.2f, 0.21f, 0.23f)); Mats.SetEmission(stripeMat, here ? c * 0.6f : Color.black); }
            if (lampMat != null) Mats.SetEmission(lampMat, here ? LampColor() * 1.6f : new Color(0.35f, 0.35f, 0.38f));
        }

        Color LampColor() => Kind == ClosetKind.Locker ? Color.Lerp(OwnerColor, Color.white, 0.5f) :
            Kind == ClosetKind.BioReactor ? Mats.LiquidGlow * 0.5f + new Color(0.2f, 0.2f, 0.2f) :
            Kind == ClosetKind.SprayBooth ? new Color(1f, 0.9f, 0.95f) : new Color(1f, 0.85f, 0.6f);

        void BuildReactor()
        {
            var t = transform;
            var metal = Mats.Lit(new Color(0.6f, 0.62f, 0.66f), 0.8f, 0.9f);
            Vector3 c = new Vector3(0, 0, Depth * 0.5f);
            // base ring and top collar stay put; the glass sinks under the floor grate to let you in
            // a thin ring flush with the floor grate, so it doesn't hide the critter's feet
            Prims.Make(PrimitiveType.Cylinder, t, c + new Vector3(0, 0.012f, 0), new Vector3(1.15f, 0.012f, 1.15f), metal, null, "Base");
            Prims.Make(PrimitiveType.Cylinder, t, c + new Vector3(0, Height - 0.12f, 0), new Vector3(1.15f, 0.06f, 1.15f), metal, null, "Collar");
            for (int s = -1; s <= 1; s += 2) // feed pipes from the ceiling
                Prims.Make(PrimitiveType.Cylinder, t, c + new Vector3(s * 0.42f, Height - 0.3f, 0.3f), new Vector3(0.09f, 0.2f, 0.09f), metal, null, "Feed");

            tube = Prims.Empty("Tube", t, c).transform;
            Prims.Make(PrimitiveType.Cylinder, tube, new Vector3(0, 0.75f, 0), new Vector3(1.05f, 0.68f, 1.05f),
                Mats.Transparent(new Color(0.75f, 0.92f, 1f, 0.18f)), null, "Glass");
            for (int k = 0; k < 2; k++)
                Prims.Make(PrimitiveType.Cylinder, tube, new Vector3(0, 0.12f + k * 1.26f, 0), new Vector3(1.09f, 0.025f, 1.09f), metal, null, "Rib");
            liquid = Prims.Empty("Liquid", t, c + new Vector3(0, 0.03f, 0)).transform;
            var lc = Mats.Liquid; lc.a = 0.55f;
            liquidMat = Mats.Transparent(lc);
            Prims.Make(PrimitiveType.Cylinder, liquid, new Vector3(0, 0.5f, 0), new Vector3(1.0f, 0.5f, 1.0f), liquidMat, null, "Fill");
            liquid.localScale = new Vector3(1f, 0.0001f, 1f);
            liquid.gameObject.SetActive(false);
            tubeY = tubeTarget = -1.5f; // parked under the floor grate
            tube.localPosition = c + Vector3.up * tubeY;
        }

        void BuildBooth()
        {
            var t = transform;
            BuildNozzles();
            // palette strip on the back wall: the current pick pokes out and glows
            var sw = new List<Transform>();
            int n2 = CritterLook.Colors.Length;
            int perRow = n2 <= 12 ? n2 : (n2 + 1) / 2; // two rows once there are lots of colors
            int rowsN = (n2 + perRow - 1) / perRow;
            for (int i = 0; i < n2; i++)
            {
                int row = i / perRow, colI = i % perRow;
                float x = (colI - (perRow - 1) * 0.5f) * 0.11f;
                float y = 1.3f + ((rowsN - 1) * 0.5f - row) * 0.11f;
                var col = CritterLook.Colors[i].color;
                var s = Prims.Make(PrimitiveType.Cube, t, new Vector3(x, y, Depth - 0.02f), new Vector3(0.09f, 0.09f, 0.04f),
                    Mats.Lit(col, 0.5f, 0f, col * 0.4f), null, "Swatch").transform;
                sw.Add(s);
            }
            swatches = sw.ToArray();
            // drips and overspray on the walls (worse when grimy)
            var rng = new System.Random(7);
            for (int i = 0; i < 10; i++)
            {
                var col = CritterLook.Colors[rng.Next(n2)].color;
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                Prims.Make(PrimitiveType.Sphere, t, new Vector3(side * (Width * 0.5f - 0.005f), 0.2f + (float)rng.NextDouble() * 1.1f, 0.2f + (float)rng.NextDouble() * 0.9f),
                    new Vector3(0.02f, 0.05f + (float)rng.NextDouble() * 0.12f, 0.05f + (float)rng.NextDouble() * 0.1f), Mats.Lit(col, 0.6f), null, "Splat");
            }
        }

        void BuildNozzles()
        {
            var t = transform;
            var metal = Mats.Lit(new Color(0.55f, 0.57f, 0.6f), 0.8f, 0.9f);
            var black = Mats.Lit(new Color(0.1f, 0.1f, 0.12f), 0.5f);
            var list = new List<Transform>();
            for (int s = -1; s <= 1; s += 2)
            for (int k = 0; k < 2; k++)
            {
                var n = Prims.Empty("Nozzle", t, new Vector3(s * (Width * 0.5f - 0.08f), 0.45f + k * 0.65f, Depth * 0.5f)).transform;
                n.localRotation = Quaternion.LookRotation(new Vector3(-s, k == 0 ? 0.25f : -0.3f, 0f));
                Prims.Make(PrimitiveType.Cylinder, n, new Vector3(0, 0, 0.05f), new Vector3(0.11f, 0.06f, 0.11f), metal, Quaternion.Euler(90f, 0, 0), "Body");
                Prims.Make(PrimitiveType.Cylinder, n, new Vector3(0, 0, 0.13f), new Vector3(0.06f, 0.03f, 0.06f), black, Quaternion.Euler(90f, 0, 0), "Tip");
                list.Add(n);
            }
            nozzles = list.ToArray();
        }

        /// <summary>All three closets in one: the reactor tube (it only comes up for a body swap), spray nozzles
        /// in the walls, and a coat hook with the player's color on the door.</summary>
        void BuildLocker()
        {
            BuildReactor();
            BuildNozzles();
            var t = transform;
            var metal = Mats.Lit(new Color(0.7f, 0.72f, 0.75f), 0.85f, 1f);
            Prims.Make(PrimitiveType.Cylinder, t, new Vector3(0, 1.42f, Depth - 0.1f), new Vector3(0.03f, Width * 0.4f, 0.03f), metal, Quaternion.Euler(0, 0, 90f), "Rail");
            // player-colored stripes down the door posts
            var stripe = stripeMat = new Material(Mats.Lit(OwnerColor, 0.5f, 0f, OwnerColor * 0.6f));
            for (int s = -1; s <= 1; s += 2)
                Prims.Make(PrimitiveType.Cube, t, new Vector3(s * (Width * 0.5f + 0.09f), Height * 0.5f, -0.095f), new Vector3(0.06f, Height - 0.1f, 0.01f), stripe, null, "OwnerStripe");
        }

        void BuildWardrobe()
        {
            var t = transform;
            var metal = Mats.Lit(new Color(0.7f, 0.72f, 0.75f), 0.85f, 1f);
            // hanging rail with a few garments on hangers along the back
            Prims.Make(PrimitiveType.Cylinder, t, new Vector3(0, 1.38f, Depth - 0.25f), new Vector3(0.03f, Width * 0.48f, 0.03f), metal, Quaternion.Euler(0, 0, 90f), "Rail");
            Color[] cloth = { new Color(0.96f, 0.97f, 0.98f), new Color(0.75f, 0.14f, 0.12f), new Color(0.08f, 0.08f, 0.09f), new Color(0.3f, 0.45f, 0.7f) };
            for (int i = 0; i < cloth.Length; i++)
            {
                float x = -0.55f + i * 0.36f;
                var hanger = Prims.Empty("Hanger", t, new Vector3(x, 1.36f, Depth - 0.25f)).transform;
                hanger.localRotation = Quaternion.Euler(0, 70f + i * 7f, 0);
                Prims.Make(PrimitiveType.Cube, hanger, new Vector3(0, -0.06f, 0), new Vector3(0.3f, 0.02f, 0.02f), metal, null, "Bar");
                Prims.Make(PrimitiveType.Cube, hanger, new Vector3(0, -0.3f, 0), new Vector3(0.32f, 0.44f, 0.07f), Mats.Lit(cloth[i], 0.3f), null, "Garment");
            }
            // mirror on the left wall with a little lamp
            Prims.Make(PrimitiveType.Cube, t, new Vector3(-Width * 0.5f + 0.02f, 0.85f, Depth * 0.5f), new Vector3(0.03f, 0.9f, 0.55f),
                Mats.Lit(new Color(0.85f, 0.9f, 0.95f), 1f, 1f), null, "Mirror");
            // shoe rack on the floor at the back
            Prims.Make(PrimitiveType.Cube, t, new Vector3(0, 0.12f, Depth - 0.12f), new Vector3(Width - 0.1f, 0.03f, 0.22f), Mats.Lit(new Color(0.45f, 0.32f, 0.2f), 0.3f), null, "ShoeRack");
            var shoe = Prims.Empty("RackShoes", t, new Vector3(0.45f, 0.18f, Depth - 0.12f)).transform;
            Accessories.AddFeet(2, shoe);
            var boot = Prims.Empty("RackBoot", t, new Vector3(-0.45f, 0.18f, Depth - 0.12f)).transform;
            Accessories.AddFeet(3, boot);
            // a hat on a hook
            var hook = Prims.Empty("HatHook", t, new Vector3(Width * 0.5f - 0.25f, 1.05f, Depth * 0.5f)).transform;
            hook.localRotation = Quaternion.Euler(0, 0, 70f);
            hook.localScale = Vector3.one * 0.6f;
            Accessories.AddHat(2, hook);
        }

        static void Label(Transform parent, string text, Vector3 localPos, float width, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity; // canvas faces -Z (the room)
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(400, 70);
            rt.localScale = Vector3.one * (width / 400f);
            var tgo = new GameObject("Text", typeof(RectTransform));
            var trt = (RectTransform)tgo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var txt = tgo.AddComponent<Text>();
            txt.font = Popup.Font;
            txt.text = text;
            txt.fontSize = 46;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = color;
        }

        // ================================================================== occupancy

        public bool TryOccupy(PlayerController p)
        {
            if (!UsableBy(p)) return false;
            if (Occupant != null && Occupant != p) return false;
            Occupant = p;
            if (HasTube) tubeTarget = -1.5f; // stays down until you're inside
            return true;
        }

        /// <summary>The critter has finished walking in.</summary>
        public void Settled(PlayerController p)
        {
            if (Kind == ClosetKind.BioReactor)
            {
                tubeTarget = 0f;
                Sfx.Play(Sfx.I != null ? Sfx.I.clunk : null, 0.6f, 0.9f);
            }
            lampPulse = 1f;
        }

        /// <summary>The critter is about to walk out.</summary>
        public void Leaving(PlayerController p)
        {
            if (HasTube)
            {
                tubeTarget = -1.5f;
                fillTarget = 0f;
                Sfx.Play(Sfx.I != null ? Sfx.I.whoosh : null, 0.4f, 1.4f);
            }
        }

        /// <summary>Let go of the occupant. A species swap still in the tank is applied (or just saved if the critter is going away).</summary>
        public void Release(PlayerController p, bool rebuild = true)
        {
            if (Occupant != p) return;
            if (Kind == ClosetKind.Locker) foreach (var k in ShopKinds) Progress.MarkSeen(p.Index, k);
            else Progress.MarkSeen(p.Index, Kind); // been inside: nothing in here is "new" for them any more
            if (pendingRebuild == p)
            {
                if (rebuild) p.ApplyLook(pendingLook);
                else pendingLook.Save(p.Index);
                pendingRebuild = null;
            }
            Occupant = null;
            if (HasTube) { tubeTarget = -1.5f; fillTarget = 0f; }
        }

        /// <summary>Current value text for a row (and a swatch color for the color row, alpha 0 = none).</summary>
        public string Value(PlayerController p, Part part, out Color swatch)
        {
            if (part == Part.Player)
            {
                var slot = JoinManager.Instance != null ? JoinManager.Instance.SlotOf(p.Index) : null;
                swatch = p.Color;
                return slot != null ? slot.ColorName : "-";
            }
            var l = p.Look;
            swatch = part == Part.Color ? l.BodyColor : Color.clear;
            string name = CritterLook.Names(part)[l.Get(part)];
            return name; // (new items get a NEW! sticker on the panel instead)
        }

        /// <summary>Row has something new in it / the item showing right now is the new one.</summary>
        public bool RowNew(PlayerController p, Part part) => part != Part.Player && Progress.AnyNew(p.Index, part);
        public bool ValueNew(PlayerController p, Part part) => part != Part.Player && Progress.IsNew(p.Index, part, p.Look.Get(part));

        static int Wrap(int v, int n) => ((v % n) + n) % n;

        /// <summary>Next option in that direction that this player has unlocked (locked ones are skipped).</summary>
        static int Step(int slot, Part part, int cur, int dir)
        {
            int n = CritterLook.Names(part).Length;
            for (int k = 1; k <= n; k++)
            {
                int v = Wrap(cur + dir * k, n);
                if (Progress.IsUnlocked(slot, part, v)) return v;
            }
            return cur;
        }

        /// <summary>Step a row left or right. Applies to the occupant straight away (with a show).</summary>
        public void Change(PlayerController p, Part part, int dir)
        {
            if (part == Part.Player) { ChangePlayerColor(p, dir); return; }
            var l = p.Look.Clone();
            int before = l.Get(part);
            l.Set(part, Step(p.Index, part, before, dir));
            if (l.Get(part) == before) { Sfx.Play(Sfx.I != null ? Sfx.I.nope : null, 0.4f, 1.2f); return; } // nothing else unlocked yet
            Vector3 mid = Inside + Vector3.up * 0.6f;
            int row = part == Part.Hat ? 0 : part == Part.Feet ? 2 : 1;
            // a locker does whichever closet's show fits the row
            ClosetKind fx = Kind != ClosetKind.Locker ? Kind
                : part == Part.Species || part == Part.Size ? ClosetKind.BioReactor : part == Part.Color ? ClosetKind.SprayBooth : ClosetKind.Wardrobe;
            switch (fx)
            {
                case ClosetKind.BioReactor:
                    // flood the tube, swap the critter at the top of the fill, then drain
                    pendingRebuild = p; pendingLook = l;
                    fillTarget = 1f;
                    tubeTarget = 0f; // (a locker's tube waits under the floor until now)
                    var lc = Mats.Liquid; lc.a = 0.6f;
                    Mats.SetBase(liquidMat, lc);
                    Sfx.Play(Sfx.I != null ? Sfx.I.bloop : null, 0.8f, 0.7f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.hum : null, 0.35f, 1.3f);
                    return;
                case ClosetKind.SprayBooth:
                    var col = l.BodyColor;
                    foreach (var n in nozzles)
                        Fx.Burst(n.position + n.forward * 0.15f, col, Color.Lerp(col, Color.white, 0.4f), 26, 4.5f, 0.12f, 0.45f, 0.3f, false,
                            ParticleSystemShapeType.Cone, 0.02f, n.rotation);
                    Fx.Puff(mid, Color.Lerp(col, Color.white, 0.5f), 0.9f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.whoosh : null, 0.6f, 1.7f, 0.1f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.splat : null, 0.35f, 1.5f);
                    break;
                default:
                    Fx.Burst(row == 0 ? mid + Vector3.up * 0.6f : row == 1 ? mid : Inside + Vector3.up * 0.1f,
                        Color.white, new Color(1f, 0.9f, 0.6f), 14, 2.5f, 0.12f, 0.4f, -0.2f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.pop : null, 0.7f, 1f + row * 0.12f);
                    break;
            }
            p.ApplyLook(l);
            p.ClosetPop();
            lampPulse = 1f;
        }

        /// <summary>Pick another player color (taken ones are skipped): the floor ring, HUD and this locker follow.</summary>
        void ChangePlayerColor(PlayerController p, int dir)
        {
            var jm = JoinManager.Instance;
            if (jm == null || !jm.CycleColor(p.Index, dir)) { Sfx.Play(Sfx.I != null ? Sfx.I.nope : null, 0.4f, 1.2f); return; }
            var c = jm.SlotOf(p.Index).Color;
            p.SetPlayerColor(c);
            Fx.Burst(Inside + Vector3.up * 0.05f, c, Color.white, 22, 3f, 0.1f, 0.45f, -0.3f);
            Sfx.Play(Sfx.I != null ? Sfx.I.pop : null, 0.7f, 1.3f);
            p.ClosetPop();
            ApplyOwnerColor();
            lampPulse = 1f;
        }

        // ================================================================== animation

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            lampPulse = Mathf.MoveTowards(lampPulse, 0f, dt * 2f);
            if (Kind == ClosetKind.Locker && OwnerColor != shownOwner) ApplyOwnerColor(); // someone joined (or left) in this slot

            // NEW! hologram while anyone here has something unseen inside
            badgePoll -= dt;
            if (badgePoll <= 0f && badge != null)
            {
                badgePoll = 0.3f;
                var sb = new System.Text.StringBuilder();
                foreach (var pl in Game.Players)
                {
                    if (pl == null || !UsableBy(pl)) continue;
                    List<string> items;
                    if (Kind == ClosetKind.Locker) { items = new List<string>(); foreach (var k in ShopKinds) items.AddRange(Progress.NewIn(pl.Index, k)); }
                    else items = Progress.NewIn(pl.Index, Kind);
                    if (items.Count == 0) continue;
                    if (sb.Length > 0) sb.Append("   ");
                    sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(pl.Color)}>P{pl.Index + 1}</color> {string.Join(", ", items)}");
                }
                badge.Set(sb.ToString());
            }
            if (header != null) header.localScale = new Vector3(Width + 0.39f, HeaderH, 0.25f) * (1f + lampPulse * 0.04f);

            if (HasTube && tube != null)
            {
                tubeY = Mathf.MoveTowards(tubeY, tubeTarget, dt * 3.2f);
                tube.localPosition = new Vector3(0, tubeY, Depth * 0.5f);
                bool tubeUp = tubeY > -0.05f;
                if (tubeUp || fillTarget < fill) // only flood once the glass is up
                    fill = Mathf.MoveTowards(fill, fillTarget, dt * (fillTarget > fill ? 3f : 1.6f));
                // a locker puts its tube away again once it has drained
                if (Kind == ClosetKind.Locker && pendingRebuild == null && fillTarget <= 0f && fill <= 0.001f && tubeTarget > -1f) tubeTarget = -1.5f;
                liquid.localScale = new Vector3(1f, Mathf.Max(0.0001f, fill * 1.35f), 1f);
                bool wet = fill > 0.001f;
                if (liquid.gameObject.activeSelf != wet) liquid.gameObject.SetActive(wet); // no flat puddle disc covering the feet
                if (pendingRebuild != null && fill >= 0.999f)
                {
                    // fully submerged: out with the old body, in with the new
                    if (pendingRebuild != null && pendingRebuild == Occupant)
                    {
                        pendingRebuild.ApplyLook(pendingLook);
                        pendingRebuild.ClosetPop();
                        Fx.Burst(Inside + Vector3.up * 0.7f, Mats.Liquid, Color.white, 30, 3f, 0.14f, 0.6f, -0.3f, true);
                        Sfx.Play(Sfx.I != null ? Sfx.I.materialize : null, 0.5f, 1.2f);
                        if (Game.Hud != null) Game.Hud.Flash(Mats.Liquid, 0.25f);
                    }
                    pendingRebuild = null;
                    fillTarget = 0f;
                    lampPulse = 1f;
                }
            }

            if (Kind == ClosetKind.SprayBooth && swatches.Length > 0)
            {
                int sel = Occupant != null ? Occupant.Look.color : -1;
                for (int i = 0; i < swatches.Length; i++)
                {
                    var lp = swatches[i].localPosition;
                    lp.z = Mathf.MoveTowards(lp.z, i == sel ? Depth - 0.1f : Depth - 0.02f, dt);
                    swatches[i].localPosition = lp;
                    swatches[i].localScale = Vector3.Lerp(swatches[i].localScale, new Vector3(0.09f, 0.09f, 0.04f) * (i == sel ? 1.35f : 1f), 1f - Mathf.Exp(-14f * dt));
                }
            }
        }
    }
}
