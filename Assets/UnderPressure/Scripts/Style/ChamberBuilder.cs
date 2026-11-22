using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// The room around the board: white panelled test chamber, observation window, chamber number,
    /// fluorescent strips and the belt's wall slot. Grime adds stains, missing/tilted panels and flicker.
    /// </summary>
    public static class ChamberBuilder
    {
        const float WallH = 6.4f, PanelSize = 1.6f, Gap = 0.07f;

        /// <summary>Where the scoreboard monitor goes on the back wall (x/y in world units), filled by Build.</summary>
        public static Rect MonitorRect { get; private set; }
        public static float BackWallZ { get; private set; }

        public struct Layout
        {
            public Rect walk;                    // walkable area (xz), inside the belt loop
            public float xLeft, xRight, zBack, zFront; // room walls
            public float slotZ, slotHalfWidth;    // where the belt comes out of the left wall
            public float[] closetXs;              // doorway centres of cosmetic closets set into the back wall
            public bool hasVending; public float vendingZ; // where the vending machine stands on the right wall
            public Vector2 backClear;             // x range along the back wall kept free of junk (the base's machine row)
        }

        public static void Build(Transform parent, Layout L, int chamberNumber, int seed)
        {
            var rng = new System.Random(seed);
            float g = Theme.Grime;
            var walk = L.walk;
            var root = Prims.Empty("Chamber", parent, Vector3.zero).transform;

            BuildFloor(root, L, rng);
            BuildBackWall(root, L, rng, chamberNumber);
            BuildSideWall(root, L, rng, L.xRight, -1f);
            BuildSideWall(root, L, rng, L.xLeft, 1f);
            BuildColliders(root, walk);
            if (g > 0.05f) BuildGrime(root, L, rng);
        }

        // ------------------------------------------------------------------ floor

        static void BuildFloor(Transform root, Layout L, System.Random rng)
        {
            var seam = Mats.Lit(Theme.FloorSeam, 0.2f);
            var panel = Mats.Lit(Theme.Floor, 0.55f);
            var panelAlt = Mats.Lit(Color.Lerp(Theme.Floor, Theme.FloorSeam, 0.12f), 0.5f);
            float w = L.xRight - L.xLeft, d = L.zBack - L.zFront;
            Prims.Make(PrimitiveType.Cube, root, new Vector3((L.xLeft + L.xRight) * 0.5f, -0.08f, (L.zBack + L.zFront) * 0.5f),
                new Vector3(w, 0.1f, d), seam, null, "FloorBase");

            const float P = 2f;
            for (float x = L.xLeft; x < L.xRight - 0.01f; x += P)
            for (float z = L.zFront; z < L.zBack - 0.01f; z += P)
            {
                float pw = Mathf.Min(P, L.xRight - x), pd = Mathf.Min(P, L.zBack - z);
                if (Theme.Grime > 0.4f && rng.NextDouble() < (Theme.Grime - 0.4f) * 0.12f) continue; // a missing floor panel
                Prims.Make(PrimitiveType.Cube, root, new Vector3(x + pw * 0.5f, -0.04f, z + pd * 0.5f),
                    new Vector3(pw - 0.06f, 0.08f, pd - 0.06f), rng.NextDouble() < 0.15 ? panelAlt : panel, null, "FloorPanel");
            }

        }

        static void BuildColliders(Transform root, Rect walk)
        {
            var floor = new GameObject("FloorCollider");
            floor.transform.SetParent(root, false);
            floor.transform.position = new Vector3(walk.center.x, -0.5f, walk.center.y);
            floor.AddComponent<BoxCollider>().size = new Vector3(walk.width + 60f, 1f, walk.height + 60f);

            float h = 4f, th = 0.5f;
            AddWall(root, new Vector3(walk.xMin - th * 0.5f, h * 0.5f, walk.center.y), new Vector3(th, h, walk.height + 2f));
            AddWall(root, new Vector3(walk.xMax + th * 0.5f, h * 0.5f, walk.center.y), new Vector3(th, h, walk.height + 2f));
            AddWall(root, new Vector3(walk.center.x, h * 0.5f, walk.yMin - th * 0.5f), new Vector3(walk.width + 2f, h, th));
            AddWall(root, new Vector3(walk.center.x, h * 0.5f, walk.yMax + th * 0.5f), new Vector3(walk.width + 2f, h, th));
        }

        static void AddWall(Transform parent, Vector3 pos, Vector3 size)
        {
            var w = new GameObject("InvisibleWall");
            w.transform.SetParent(parent, false);
            w.transform.position = pos;
            w.AddComponent<BoxCollider>().size = size;
        }

        // ------------------------------------------------------------------ walls

        static Material PanelMat(System.Random rng)
        {
            float shade = (float)rng.NextDouble() * 0.05f + Theme.Grime * (float)rng.NextDouble() * 0.25f;
            return Mats.Lit(Color.Lerp(Theme.Wall, Theme.Stain, Mathf.Round(shade * 20f) / 20f), 0.6f);
        }

        static void BuildBackWall(Transform root, Layout L, System.Random rng, int chamberNumber)
        {
            float z = L.zBack;
            float w = L.xRight - L.xLeft;
            var backing = Mats.Lit(Theme.WallBacking, 0.2f);
            // closet doorways cut the bottom row of panels (closets are exactly one panel tall)
            var doors = new System.Collections.Generic.List<Rect>();
            if (L.closetXs != null)
                foreach (float cx in L.closetXs)
                    doors.Add(Rect.MinMaxRect(cx - Closet.Width * 0.5f - 0.18f, 0f, cx + Closet.Width * 0.5f + 0.18f, Closet.Height));
            Prims.Make(PrimitiveType.Cube, root, new Vector3((L.xLeft + L.xRight) * 0.5f, (WallH + PanelSize) * 0.5f, z + 0.35f), new Vector3(w + 0.4f, WallH - PanelSize, 0.3f), backing, null, "BackWallBacking");
            foreach (var seg in Segments(L.xLeft - 0.2f, L.xRight + 0.2f, doors))
                Prims.Make(PrimitiveType.Cube, root, new Vector3((seg.x + seg.y) * 0.5f, PanelSize * 0.5f, z + 0.35f), new Vector3(seg.y - seg.x, PanelSize, 0.3f), backing, null, "BackWallBackingLow");

            // features that cut holes in the panel grid
            bool roomy = w > 19f;
            Rect window = roomy ? Rect.MinMaxRect(L.xRight - 8.5f, 2.4f, L.xRight - 2.2f, 4.9f) : new Rect(-9999f, -9999f, 0f, 0f);
            Rect sign = Rect.MinMaxRect(L.xLeft + 2.2f, 2.0f, L.xLeft + 7.0f, 5.4f);

            var removed = new System.Collections.Generic.List<Rect>();
            for (float x = L.xLeft; x < L.xRight - 0.01f; x += PanelSize)
            for (float y = 0f; y < WallH - 0.01f; y += PanelSize)
            {
                var r = new Rect(x, y, PanelSize, PanelSize);
                if (roomy && r.Overlaps(window)) continue;
                if (OverlapsAny(r, doors)) { removed.Add(r); continue; }
                WallPanel(root, new Vector3(x + PanelSize * 0.5f, y + PanelSize * 0.5f, z + 0.12f), Quaternion.identity, rng);
            }

            // fill what's left of the removed panels around the doorways
            if (removed.Count > 0)
            {
                float bx0 = float.MaxValue, bx1 = float.MinValue;
                foreach (var r in removed) { bx0 = Mathf.Min(bx0, r.xMin); bx1 = Mathf.Max(bx1, r.xMax); }
                var fill = PanelMat(rng);
                foreach (var seg in Segments(bx0, bx1, doors))
                    if (seg.y - seg.x > 0.02f)
                        Prims.Make(PrimitiveType.Cube, root, new Vector3((seg.x + seg.y) * 0.5f, PanelSize * 0.5f, z + 0.12f),
                            new Vector3(seg.y - seg.x - Gap * 0.5f, PanelSize - Gap, 0.16f), fill, null, "ClosetFacade");
            }

            // kick plate + light strip along the top
            foreach (var seg in Segments(L.xLeft, L.xRight, doors))
                Prims.Make(PrimitiveType.Cube, root, new Vector3((seg.x + seg.y) * 0.5f, 0.15f, z), new Vector3(seg.y - seg.x, 0.3f, 0.14f), Mats.Lit(Theme.WallTrim, 0.4f, 0.4f), null, "KickPlate");
            LightStrip(root, new Vector3((L.xLeft + L.xRight) * 0.5f, WallH - 0.3f, z - 0.02f), new Vector3(w - 1f, 0.14f, 0.1f), rng);

            if (roomy) ObservationWindow(root, window, z);

            // space for the scoreboard monitor between the sign and the window
            float mx0 = sign.xMax + 0.5f, mx1 = roomy ? window.xMin - 0.5f : L.xRight - 0.8f;
            if (mx1 - mx0 < 4f) { float c = (mx0 + mx1) * 0.5f; mx0 = c - 2f; mx1 = c + 2f; }
            float mw = Mathf.Min(mx1 - mx0, 9f);
            float mcx = (mx0 + mx1) * 0.5f;
            float my0 = 1.7f;
            foreach (var d in doors) // keep the monitor clear of closet headers
                if (d.xMax > mcx - mw * 0.5f && d.xMin < mcx + mw * 0.5f) my0 = Closet.Height + Closet.HeaderH + 0.15f;
            MonitorRect = Rect.MinMaxRect(mcx - mw * 0.5f, my0, mcx + mw * 0.5f, Mathf.Min(my0 + mw * 0.5f, 5.6f));
            BackWallZ = z;
            ChamberSign(root, sign, z, chamberNumber);
        }

        static bool NearCloset(Layout L, float x, float margin)
        {
            if (L.closetXs == null) return false;
            foreach (float cx in L.closetXs) if (Mathf.Abs(x - cx) < Closet.Width * 0.5f + 0.2f + margin) return true;
            return false;
        }

        static bool OverlapsAny(Rect r, System.Collections.Generic.List<Rect> rs)
        {
            foreach (var o in rs) if (r.Overlaps(o)) return true;
            return false;
        }

        /// <summary>The x-intervals of [x0, x1] not covered by any of the rects (x = start, y = end).</summary>
        static System.Collections.Generic.List<Vector2> Segments(float x0, float x1, System.Collections.Generic.List<Rect> holes)
        {
            var sorted = new System.Collections.Generic.List<Rect>(holes);
            sorted.Sort((a, b) => a.xMin.CompareTo(b.xMin));
            var outList = new System.Collections.Generic.List<Vector2>();
            float cur = x0;
            foreach (var h in sorted)
            {
                if (h.xMax <= cur || h.xMin >= x1) continue;
                if (h.xMin > cur) outList.Add(new Vector2(cur, h.xMin));
                cur = Mathf.Max(cur, h.xMax);
            }
            if (cur < x1) outList.Add(new Vector2(cur, x1));
            return outList;
        }

        static void BuildSideWall(Transform root, Layout L, System.Random rng, float x, float inward)
        {
            float d = L.zBack - L.zFront;
            Prims.Make(PrimitiveType.Cube, root, new Vector3(x - inward * 0.35f, WallH * 0.5f, (L.zBack + L.zFront) * 0.5f), new Vector3(0.3f, WallH, d + 0.4f),
                Mats.Lit(Theme.WallBacking, 0.2f), null, "SideWallBacking");
            var rot = Quaternion.Euler(0f, inward > 0 ? 90f : -90f, 0f);
            bool hasSlot = inward > 0f; // the belt enters through the left wall
            Rect slot = Rect.MinMaxRect(L.slotZ - L.slotHalfWidth - 0.25f, 0f, L.slotZ + L.slotHalfWidth + 0.25f, 1.7f);
            for (float z = L.zFront; z < L.zBack - 0.01f; z += PanelSize)
            for (float y = 0f; y < WallH - 0.01f; y += PanelSize)
            {
                if (hasSlot && new Rect(z, y, PanelSize, PanelSize).Overlaps(slot)) continue;
                WallPanel(root, new Vector3(x - inward * 0.12f, y + PanelSize * 0.5f, z + PanelSize * 0.5f), rot, rng);
            }
            if (hasSlot) BeltSlot(root, slot, x);
            Prims.Make(PrimitiveType.Cube, root, new Vector3(x, 0.15f, (L.zBack + L.zFront) * 0.5f), new Vector3(0.14f, 0.3f, d), Mats.Lit(Theme.WallTrim, 0.4f, 0.4f), null, "KickPlate");
            LightStrip(root, new Vector3(x + inward * 0.02f, WallH - 0.3f, (L.zBack + L.zFront) * 0.5f), new Vector3(0.1f, 0.14f, d - 1f), rng);
        }

        static void WallPanel(Transform root, Vector3 pos, Quaternion rot, System.Random rng)
        {
            float g = Theme.Grime;
            if (g > 0.2f && rng.NextDouble() < g * 0.14f) return; // missing panel shows the dark guts
            var t = Prims.Make(PrimitiveType.Cube, root, pos, new Vector3(PanelSize - Gap, PanelSize - Gap, 0.16f), PanelMat(rng), rot, "WallPanel").transform;
            if (g > 0.2f && rng.NextDouble() < g * 0.2f)
                t.localRotation = rot * Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 18f * g, (float)(rng.NextDouble() - 0.5) * 10f * g, (float)(rng.NextDouble() - 0.5) * 14f * g);
        }

        static void LightStrip(Transform root, Vector3 pos, Vector3 size, System.Random rng)
        {
            var on = Mats.Lit(Color.white, 0.9f, 0f, Theme.LightColor * 2.2f);
            var go = Prims.Make(PrimitiveType.Cube, root, pos, size, on, null, "LightStrip");
            if (Theme.Grime > 0.15f)
            {
                var f = go.AddComponent<Flicker>();
                f.Setup(on, Mats.Lit(Theme.WallTrim, 0.5f), Theme.Grime, rng.Next());
            }
        }

        // ------------------------------------------------------------------ set dressing

        /// <summary>Dark slot in the left wall that the belt comes out of. slot.x/xMax are Z coordinates here.</summary>
        static void BeltSlot(Transform root, Rect slot, float wallX)
        {
            var ink = Mats.Lit(Theme.Ink, 0.1f);
            float zc = slot.center.x;
            Prims.Make(PrimitiveType.Cube, root, new Vector3(wallX - 2.2f, slot.height * 0.5f, zc), new Vector3(4f, slot.height, slot.width), ink, null, "Tunnel");
            var black = Mats.Lit(Theme.Ink, 0.3f);
            var yellow = Mats.Lit(Theme.Hazard, 0.4f);
            int i = 0;
            for (float z = slot.xMin; z < slot.xMax - 0.01f; z += 0.3f, i++)
                Prims.Make(PrimitiveType.Cube, root, new Vector3(wallX + 0.02f, slot.yMax + 0.12f, z + 0.15f), new Vector3(0.2f, 0.24f, 0.3f), (i & 1) == 0 ? black : yellow);
            for (int s = -1; s <= 1; s += 2)
                Prims.Make(PrimitiveType.Cube, root, new Vector3(wallX + 0.02f, slot.height * 0.5f, s < 0 ? slot.xMin : slot.xMax), new Vector3(0.22f, slot.height + 0.48f, 0.2f), black);
            Prims.Make(PrimitiveType.Sphere, root, new Vector3(wallX + 0.05f, slot.yMax + 0.55f, zc), Vector3.one * 0.3f,
                Mats.Lit(Theme.AccentOrange, 0.8f, 0f, Theme.AccentOrange * 2f), null, "SlotLamp");
        }

        static void ObservationWindow(Transform root, Rect r, float z)
        {
            var frame = Mats.Lit(Theme.WallTrim, 0.5f, 0.6f);
            var glass = Mats.Lit(new Color(0.02f, 0.04f, 0.05f), 0.98f, 0.2f, new Color(0.01f, 0.03f, 0.04f));
            Vector3 c = new Vector3(r.center.x, r.center.y, z);
            Prims.Make(PrimitiveType.Cube, root, c + Vector3.forward * 0.2f, new Vector3(r.width, r.height, 0.1f), glass, null, "Glass");
            float t = 0.22f;
            Prims.Make(PrimitiveType.Cube, root, c + new Vector3(0, r.height * 0.5f, 0), new Vector3(r.width + t, t, 0.4f), frame);
            Prims.Make(PrimitiveType.Cube, root, c - new Vector3(0, r.height * 0.5f, 0), new Vector3(r.width + t, t * 1.4f, 0.5f), frame);
            Prims.Make(PrimitiveType.Cube, root, c + new Vector3(r.width * 0.5f, 0, 0), new Vector3(t, r.height + t, 0.4f), frame);
            Prims.Make(PrimitiveType.Cube, root, c - new Vector3(r.width * 0.5f, 0, 0), new Vector3(t, r.height + t, 0.4f), frame);
            // mullion
            Prims.Make(PrimitiveType.Cube, root, c, new Vector3(0.08f, r.height, 0.2f), frame);
            // glass glare streaks
            var glare = Mats.Lit(new Color(0.5f, 0.6f, 0.65f), 0.9f, 0f, new Color(0.08f, 0.1f, 0.11f));
            Prims.Make(PrimitiveType.Cube, root, c + new Vector3(-r.width * 0.25f, 0.2f, 0.12f), new Vector3(0.12f, r.height * 0.9f, 0.01f), glare, Quaternion.Euler(0, 0, -25f));
            Prims.Make(PrimitiveType.Cube, root, c + new Vector3(r.width * 0.2f, -0.1f, 0.12f), new Vector3(0.06f, r.height * 0.7f, 0.01f), glare, Quaternion.Euler(0, 0, -25f));
            if (Theme.Grime > 0.5f)
            {
                var crack = Mats.Lit(new Color(0.75f, 0.8f, 0.8f), 0.5f, 0f, new Color(0.2f, 0.22f, 0.22f));
                for (int i = 0; i < 5; i++)
                    Prims.Make(PrimitiveType.Cube, root, c + new Vector3(r.width * 0.3f, -0.3f, 0.13f), new Vector3(0.025f, 0.9f + i * 0.15f, 0.01f), crack, Quaternion.Euler(0, 0, i * 72f + 10f));
            }

            // Something is watching.
            var watcher = Prims.Empty("Watcher", root, c + Vector3.forward * 0.6f);
            watcher.AddComponent<Watcher>().Build(r.width);
        }

        static void ChamberSign(Transform root, Rect r, float z, int number)
        {
            var white = Mats.Lit(new Color(0.98f, 0.98f, 0.98f), 0.4f);
            var ink = Mats.Lit(Theme.Ink, 0.3f);
            Vector3 c = new Vector3(r.center.x, r.center.y, z - 0.05f);
            Prims.Make(PrimitiveType.Cube, root, c, new Vector3(r.width, r.height, 0.1f), white, null, "SignPanel");
            Prims.Make(PrimitiveType.Cube, root, c + new Vector3(0, r.height * 0.5f - 0.12f, -0.06f), new Vector3(r.width - 0.2f, 0.06f, 0.02f), ink);
            Prims.Make(PrimitiveType.Cube, root, c + new Vector3(0, -r.height * 0.5f + 0.95f, -0.06f), new Vector3(r.width - 0.2f, 0.04f, 0.02f), ink);

            // big chamber number, built from segment bars (no font needed)
            string digits = Mathf.Clamp(number, 0, 99).ToString("00");
            float digitW = 0.95f;
            for (int i = 0; i < digits.Length; i++)
                SevenSeg.Digit(root, c + new Vector3(-r.width * 0.5f + 0.75f + i * (digitW + 0.35f), 0.55f, -0.07f), digits[i] - '0', digitW, 1.7f, 0.2f, ink);

            // little pictogram row: pipe, drop, critter, warning
            float py = c.y - r.height * 0.5f + 0.5f;
            float px = c.x - r.width * 0.5f + 0.55f;
            var orange = Mats.Lit(Theme.AccentOrange, 0.5f);
            // pipe elbow icon
            Prims.Make(PrimitiveType.Cube, root, new Vector3(px, py + 0.08f, z - 0.12f), new Vector3(0.12f, 0.45f, 0.02f), ink);
            Prims.Make(PrimitiveType.Cube, root, new Vector3(px + 0.17f, py - 0.08f, z - 0.12f), new Vector3(0.45f, 0.12f, 0.02f), ink);
            px += 0.9f;
            // liquid drop icon
            Prims.Make(PrimitiveType.Sphere, root, new Vector3(px, py - 0.04f, z - 0.12f), new Vector3(0.3f, 0.3f, 0.02f), ink);
            Prims.Make(PrimitiveType.Cube, root, new Vector3(px, py + 0.12f, z - 0.12f), new Vector3(0.15f, 0.15f, 0.02f), ink, Quaternion.Euler(0, 0, 45f));
            px += 0.9f;
            // critter icon (circle head, two big eye holes)
            Prims.Make(PrimitiveType.Sphere, root, new Vector3(px, py, z - 0.12f), new Vector3(0.42f, 0.38f, 0.02f), ink);
            Prims.Make(PrimitiveType.Sphere, root, new Vector3(px - 0.08f, py + 0.03f, z - 0.14f), new Vector3(0.11f, 0.13f, 0.02f), white);
            Prims.Make(PrimitiveType.Sphere, root, new Vector3(px + 0.08f, py + 0.03f, z - 0.14f), new Vector3(0.11f, 0.13f, 0.02f), white);
            px += 0.9f;
            // warning triangle (approximated with a rotated square)
            Prims.Make(PrimitiveType.Cube, root, new Vector3(px, py, z - 0.12f), new Vector3(0.36f, 0.36f, 0.02f), orange, Quaternion.Euler(0, 0, 45f));
            Prims.Make(PrimitiveType.Cube, root, new Vector3(px, py + 0.03f, z - 0.14f), new Vector3(0.05f, 0.18f, 0.02f), ink);
        }

        // ------------------------------------------------------------------ grime

        static void BuildGrime(Transform root, Layout L, System.Random rng)
        {
            float g = Theme.Grime;
            var stain = Mats.Lit(Color.Lerp(Theme.Floor, Theme.Stain, 0.55f), 0.15f);
            var stainDark = Mats.Lit(Color.Lerp(Theme.Floor, Theme.Stain, 0.85f), 0.1f);
            var rust = Mats.Lit(Theme.Rust, 0.2f);
            var slime = Mats.Lit(Color.Lerp(Mats.Liquid, Theme.Stain, 0.5f), 0.9f, 0f, Mats.Liquid * 0.15f);

            int floorStains = Mathf.RoundToInt(g * 40);
            for (int i = 0; i < floorStains; i++)
            {
                float x = Mathf.Lerp(L.xLeft + 0.5f, L.xRight - 0.5f, (float)rng.NextDouble());
                float z = Mathf.Lerp(L.zFront + 2f, L.zBack - 0.5f, (float)rng.NextDouble());
                float s = 0.3f + (float)rng.NextDouble() * 1.4f * g;
                var mat = rng.NextDouble() < 0.15 * g ? slime : (rng.NextDouble() < 0.5 ? stain : stainDark);
                Prims.Make(PrimitiveType.Cylinder, root, new Vector3(x, 0.006f + i * 0.0002f, z), new Vector3(s, 0.002f, s * (0.6f + (float)rng.NextDouble() * 0.8f)), mat,
                    Quaternion.Euler(0, (float)rng.NextDouble() * 180f, 0), "Stain");
            }

            // drips and rust streaks down the back wall
            int streaks = Mathf.RoundToInt(g * 26);
            for (int i = 0; i < streaks; i++)
            {
                float x = Mathf.Lerp(L.xLeft + 0.5f, L.xRight - 0.5f, (float)rng.NextDouble());
                float h = 0.6f + (float)rng.NextDouble() * 3f * g;
                float top = Mathf.Lerp(2f, WallH - 0.6f, (float)rng.NextDouble());
                if (NearCloset(L, x, 0.2f)) h = Mathf.Min(h, top - Closet.Height - Closet.HeaderH);
                if (h < 0.1f) continue;
                Prims.Make(PrimitiveType.Cube, root, new Vector3(x, top - h * 0.5f, L.zBack - 0.05f), new Vector3(0.06f + (float)rng.NextDouble() * 0.12f, h, 0.02f),
                    rng.NextDouble() < 0.5 ? rust : stainDark, null, "Streak");
            }

            // junk along the walls
            int junk = Mathf.RoundToInt(g * 18);
            var junkMat = Mats.Lit(Color.Lerp(Theme.WallTrim, Theme.Rust, 0.4f), 0.3f, 0.3f);
            for (int i = 0; i < junk; i++)
            {
                bool back = rng.NextDouble() < 0.6;
                float x = back ? Mathf.Lerp(L.walk.xMin + 0.4f, L.xRight - 0.6f, (float)rng.NextDouble()) : L.xRight - 0.6f;
                float z = back ? L.zBack - 0.45f : Mathf.Lerp(L.walk.yMin, L.zBack - 0.6f, (float)rng.NextDouble());
                float s = 0.12f + (float)rng.NextDouble() * 0.3f;
                if (back && NearCloset(L, x, 0.4f)) continue; // keep the doorways clear
                if (back && x > L.backClear.x - 0.3f && x < L.backClear.y) continue; // and the machine row
                if (!back && L.hasVending && Mathf.Abs(z - L.vendingZ) < 1.0f) continue; // and the vending machine
                Prims.Make(PrimitiveType.Cube, root, new Vector3(x, s * 0.5f, z), Vector3.one * s, junkMat,
                    Quaternion.Euler((float)rng.NextDouble() * 30f, (float)rng.NextDouble() * 360f, 0), "Junk");
            }
        }
    }

    /// <summary>Seven-segment digits from cubes: clinical signage with no font dependency.</summary>
    public static class SevenSeg
    {
        //                     a      b      c      d      e      f      g
        static readonly bool[][] Masks =
        {
            new[] { true,  true,  true,  true,  true,  true,  false }, // 0
            new[] { false, true,  true,  false, false, false, false }, // 1
            new[] { true,  true,  false, true,  true,  false, true  }, // 2
            new[] { true,  true,  true,  true,  false, false, true  }, // 3
            new[] { false, true,  true,  false, false, true,  true  }, // 4
            new[] { true,  false, true,  true,  false, true,  true  }, // 5
            new[] { true,  false, true,  true,  true,  true,  true  }, // 6
            new[] { true,  true,  true,  false, false, false, false }, // 7
            new[] { true,  true,  true,  true,  true,  true,  true  }, // 8
            new[] { true,  true,  true,  true,  false, true,  true  }, // 9
        };

        public static void Digit(Transform parent, Vector3 center, int digit, float w, float h, float t, Material mat)
        {
            var m = Masks[Mathf.Clamp(digit, 0, 9)];
            Vector3 H = new Vector3(w, t, 0.02f), V = new Vector3(t, h * 0.5f, 0.02f);
            Vector3[] pos =
            {
                new Vector3(0, h * 0.5f, 0), new Vector3(w * 0.5f, h * 0.25f, 0), new Vector3(w * 0.5f, -h * 0.25f, 0),
                new Vector3(0, -h * 0.5f, 0), new Vector3(-w * 0.5f, -h * 0.25f, 0), new Vector3(-w * 0.5f, h * 0.25f, 0), Vector3.zero
            };
            bool[] horizontal = { true, false, false, true, false, false, true };
            for (int i = 0; i < 7; i++)
                if (m[i]) Prims.Make(PrimitiveType.Cube, parent, center + pos[i], horizontal[i] ? H : V, mat, null, "Seg");
        }
    }

    /// <summary>Fluorescent tube that stutters more the grimier the level.</summary>
    public class Flicker : MonoBehaviour
    {
        Material on, off;
        Renderer r;
        float grime, timer;
        System.Random rng;

        public void Setup(Material onMat, Material offMat, float g, int seed)
        {
            on = onMat; off = offMat; grime = g;
            r = GetComponent<Renderer>();
            rng = new System.Random(seed);
            timer = 1f + (float)rng.NextDouble() * 4f;
        }

        void Update()
        {
            if (r == null) return;
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            bool isOn = r.sharedMaterial == on;
            if (isOn)
            {
                r.sharedMaterial = off;
                timer = 0.03f + (float)rng.NextDouble() * 0.12f * grime;
            }
            else
            {
                r.sharedMaterial = on;
                // grimier = more frequent stutters, and sometimes a burst of them
                timer = rng.NextDouble() < 0.4 * grime ? 0.05f : Mathf.Lerp(8f, 1.2f, grime) * (0.5f + (float)rng.NextDouble());
            }
        }
    }

    /// <summary>A pair of eyes that sometimes appears behind the observation glass. Never explained.</summary>
    public class Watcher : MonoBehaviour
    {
        Transform eyes;
        Transform[] eye = new Transform[2];
        float timer, visibleFor, blink;
        bool visible;
        float width;

        public void Build(float windowWidth)
        {
            width = windowWidth;
            eyes = Prims.Empty("Eyes", transform, Vector3.zero).transform;
            var glow = Mats.Lit(new Color(1f, 0.95f, 0.7f), 0.5f, 0f, new Color(1.6f, 1.4f, 0.8f));
            for (int i = 0; i < 2; i++)
                eye[i] = Prims.Make(PrimitiveType.Sphere, eyes, new Vector3((i == 0 ? -1 : 1) * 0.13f, 0f, 0f), new Vector3(0.11f, 0.07f, 0.04f), glow).transform;
            eyes.gameObject.SetActive(false);
            timer = Random.Range(8f, 16f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            timer -= dt;
            if (!visible && timer <= 0f)
            {
                visible = true;
                visibleFor = Random.Range(1.2f, 3f);
                eyes.localPosition = new Vector3(Random.Range(-width * 0.35f, width * 0.35f), Random.Range(-0.4f, 0.3f), 0f);
                eyes.gameObject.SetActive(true);
            }
            if (!visible) return;

            visibleFor -= dt;
            blink -= dt;
            if (blink <= 0f) blink = Random.Range(0.6f, 1.6f);
            float open = blink < 0.08f ? 0.1f : 1f;
            foreach (var e in eye) e.localScale = new Vector3(0.11f, 0.07f * open, 0.04f);
            // drift slowly toward the nearest player
            if (Game.Players.Count > 0 && Game.Players[0] != null)
            {
                float tx = Mathf.Clamp(Game.Players[0].transform.position.x - transform.position.x, -width * 0.35f, width * 0.35f);
                var p = eyes.localPosition;
                p.x = Mathf.MoveTowards(p.x, tx, dt * 0.4f);
                eyes.localPosition = p;
            }
            if (visibleFor <= 0f)
            {
                visible = false;
                eyes.gameObject.SetActive(false);
                timer = Random.Range(10f, 22f) * Mathf.Lerp(1f, 0.4f, Theme.Grime);
            }
        }
    }
}
