using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// A lab vending machine that sells one item (an ItemDef): bought from the shared bank for the whole team, and
    /// for upgradeable items (the backpack) again and again at a rising price. Out-of-order machines are placeholders.
    /// Local space: +Z faces into the room (the front), origin on the floor.
    /// </summary>
    public class VendingMachine : MonoBehaviour
    {
        public static readonly List<VendingMachine> All = new List<VendingMachine>();

        const float W = 0.95f, D = 0.7f, H = 2.05f;

        public Vector3 Front => transform.TransformPoint(new Vector3(0, 0, D * 0.5f + 0.55f));
        public Vector3 PromptAnchor => transform.TransformPoint(new Vector3(0, H + 0.35f, D * 0.5f));

        Text priceText;
        Transform body; // everything visual hangs off this, so a thump can wobble the whole machine

        // the window: a real recess in the cabinet, so the shelves behind the glass can be seen
        const float WinX = -0.1f, WinW = W * 0.68f, WinY0 = 0.56f, WinY1 = 1.44f, Recess = 0.26f;

        /// <summary>The cabinet with a recessed window: a back block, a frame around the window, a dark back panel.</summary>
        void Cabinet(Material shell, Material dark)
        {
            var t = body;
            float zf = D * 0.5f, x0 = WinX - WinW * 0.5f, x1 = WinX + WinW * 0.5f;
            float backD = D - Recess;
            Prims.Make(PrimitiveType.Cube, t, new Vector3(0, H * 0.5f, -D * 0.5f + backD * 0.5f), new Vector3(W, H, backD), shell, null, "Cabinet");
            float fz = zf - Recess * 0.5f;
            Prims.Make(PrimitiveType.Cube, t, new Vector3((-W * 0.5f + x0) * 0.5f, H * 0.5f, fz), new Vector3(x0 + W * 0.5f, H, Recess), shell, null, "FrameL");
            Prims.Make(PrimitiveType.Cube, t, new Vector3((x1 + W * 0.5f) * 0.5f, H * 0.5f, fz), new Vector3(W * 0.5f - x1, H, Recess), shell, null, "FrameR");
            Prims.Make(PrimitiveType.Cube, t, new Vector3(WinX, WinY0 * 0.5f, fz), new Vector3(WinW, WinY0, Recess), shell, null, "FrameBottom");
            Prims.Make(PrimitiveType.Cube, t, new Vector3(WinX, (WinY1 + H) * 0.5f, fz), new Vector3(WinW, H - WinY1, Recess), shell, null, "FrameTop");
            Prims.Make(PrimitiveType.Cube, t, new Vector3(WinX, (WinY0 + WinY1) * 0.5f, zf - Recess + 0.006f), new Vector3(WinW, WinY1 - WinY0, 0.01f), dark, null, "Back");
            Prims.Make(PrimitiveType.Cube, t, new Vector3(0, 0.04f, 0), new Vector3(W + 0.04f, 0.08f, D + 0.04f), dark, null, "Plinth");
        }
        Transform tray;
        Material signMat;
        /// <summary>What this machine sells (null when it's out of order).</summary>
        public ItemDef Item { get; private set; }
        Color SignColor => Item != null ? Item.signColor : new Color(0.25f, 0.6f, 1f);
        float shake, signPulse;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        /// <summary>True for the placeholder machines at the base: nothing in them yet.</summary>
        public bool OutOfOrder { get; private set; }

        /// <summary>'outOfOrder' = an empty placeholder machine with a paper sign; 'sideStickers' = how many sides get the
        /// big bottle (1 = the -X side, which faces the camera on the right wall; 2 = both sides).</summary>
        public static VendingMachine Create(Transform parent, Vector3 pos, Quaternion rot, ItemDef item, int sideStickers = 1, int variant = 0)
        {
            bool outOfOrder = item == null;
            var go = new GameObject(outOfOrder ? "VendingMachine_OutOfOrder" : "VendingMachine_" + item.id);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var v = go.AddComponent<VendingMachine>();
            v.OutOfOrder = outOfOrder;
            v.Item = item;
            if (outOfOrder) v.BuildBroken(variant); else v.Build(sideStickers);
            // solid: critters walk around it, not through it
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0, H * 0.5f, 0);
            col.size = new Vector3(W, H, D);
            return v;
        }

        public string PromptText()
        {
            if (OutOfOrder) return "OUT OF ORDER";
            if (Item.consumable) return $"BUY A {Item.name}  ${Items.NextPrice(Item.id):N0}";
            string id = Item.id;
            int lvl = Items.Level(id), max = Items.MaxLevel(id);
            if (lvl >= max) return max > 1 ? $"{Item.name} MAXED OUT (LV {lvl})" : "SOLD OUT (YOU ALL HAVE ONE)";
            if (lvl == 0 && Item.requires != null && !Items.TeamOwns(Item.requires)) return $"{Item.name}: NEEDS A {Items.Get(Item.requires)?.name}";
            if (lvl == 0) return $"BUY {Item.name}  ${Items.NextPrice(id):N0}";
            return $"UPGRADE {Item.name} TO LV {lvl + 1}  ${Items.NextPrice(id):N0}";
        }

        // ================================================================== build

        void Build(int sideStickers)
        {
            body = Prims.Empty("Body", transform, Vector3.zero).transform;
            var t = body;
            var shell = Mats.Lit(Color.Lerp(new Color(0.9f, 0.92f, 0.95f), Theme.Stain, Theme.Grime * 0.4f), 0.55f, 0.2f);
            var dark = Mats.Lit(new Color(0.12f, 0.13f, 0.15f), 0.6f, 0.4f);
            var chrome = Mats.Lit(new Color(0.75f, 0.78f, 0.82f), 0.9f, 1f);
            var blue = SignColor;
            signMat = new Material(Mats.Lit(blue, 0.8f, 0f, blue * 1.1f)); // its own copy: it pulses

            Cabinet(shell, dark);
            // big lit sign on top: the item's icon on a white plate, and its short name
            float sy = H - 0.29f;
            Prims.Make(PrimitiveType.Cube, t, new Vector3(0, sy, D * 0.5f + 0.005f), new Vector3(W - 0.08f, 0.46f, 0.02f), signMat, null, "Sign");
            Prims.Make(PrimitiveType.Cube, t, new Vector3(-0.2f, sy, D * 0.5f + 0.012f), new Vector3(0.4f, 0.4f, 0.01f),
                Mats.Lit(new Color(0.95f, 0.97f, 1f), 0.6f, 0f, new Color(0.35f, 0.38f, 0.42f)), null, "IconPlate");
            Icon(t, new Vector3(-0.2f, sy, D * 0.5f + 0.02f), Quaternion.Euler(0f, 180f, 0f), 0.38f, Item.icon);
            Label(t, Item.signText, new Vector3(0.2f, sy, D * 0.5f + 0.02f), 0.42f, SignColor * 0.25f + new Color(0, 0, 0, 1f), Item.signText.Length > 3 ? 54 : 70);
            // rows of the product on shelves in the recess, behind the glass
            for (int row = 0; row < 3; row++)
            {
                float y = 0.72f + row * 0.25f;
                Prims.Make(PrimitiveType.Cube, t, new Vector3(WinX, y - 0.11f, D * 0.5f - Recess * 0.5f), new Vector3(WinW, 0.015f, Recess - 0.02f), chrome, null, "Shelf");
                for (int c = 0; c < 4; c++)
                    MakeProduct(Item, t, new Vector3(WinX - W * 0.25f + c * W * 0.165f, y - 0.1f, D * 0.5f - Recess * 0.5f), 0.8f);
            }
            Prims.Make(PrimitiveType.Cube, t, new Vector3(WinX, (WinY0 + WinY1) * 0.5f, D * 0.5f - 0.01f), new Vector3(WinW, WinY1 - WinY0, 0.01f),
                Mats.Transparent(new Color(0.8f, 0.9f, 1f, 0.12f)), null, "Glass");
            // side panel: price, coin slot, buttons
            float px = W * 0.5f - 0.13f;
            Prims.Make(PrimitiveType.Cube, t, new Vector3(px, 1.0f, D * 0.5f + 0.004f), new Vector3(0.2f, 0.85f, 0.012f), dark, null, "Panel");
            priceText = Label(t, "", new Vector3(px, 1.28f, D * 0.5f + 0.015f), 0.18f, new Color(1f, 0.85f, 0.3f), 44);
            Prims.Make(PrimitiveType.Cube, t, new Vector3(px, 1.12f, D * 0.5f + 0.012f), new Vector3(0.05f, 0.08f, 0.01f), chrome, null, "CoinSlot");
            for (int b = 0; b < 3; b++)
                Prims.Make(PrimitiveType.Cylinder, t, new Vector3(px, 0.95f - b * 0.1f, D * 0.5f + 0.015f), new Vector3(0.06f, 0.01f, 0.06f),
                    Mats.Lit(b == 0 ? blue : new Color(0.4f, 0.42f, 0.45f), 0.6f, 0f, b == 0 ? blue : Color.black), Quaternion.Euler(90f, 0, 0), "Button");
            // dispenser tray at the bottom
            Prims.Make(PrimitiveType.Cube, t, new Vector3(-0.05f, 0.33f, D * 0.5f - 0.02f), new Vector3(W * 0.75f, 0.22f, 0.06f), dark, null, "TrayHole");
            tray = Prims.Empty("Tray", t, new Vector3(-0.05f, 0.3f, D * 0.5f - 0.06f)).transform;
            if (sideStickers >= 1) SideSticker(t, dark, -1);
            if (sideStickers >= 2) SideSticker(t, dark, 1);
            RefreshPrice();
        }

        static readonly Color[] BrokenShells =
        {
            new Color(0.75f, 0.42f, 0.38f), new Color(0.45f, 0.6f, 0.5f), new Color(0.82f, 0.7f, 0.42f), new Color(0.5f, 0.52f, 0.7f),
        };

        /// <summary>A placeholder machine: faded paint, dead sign, empty dusty shelves, and a taped-on OUT OF ORDER sign.</summary>
        void BuildBroken(int variant)
        {
            body = Prims.Empty("Body", transform, Vector3.zero).transform;
            var t = body;
            var rng = new System.Random(variant * 17 + 3);
            Color paint = BrokenShells[((variant % BrokenShells.Length) + BrokenShells.Length) % BrokenShells.Length];
            var shell = Mats.Lit(Color.Lerp(paint, Theme.Stain, 0.25f + Theme.Grime * 0.4f), 0.35f, 0.2f);
            var dark = Mats.Lit(new Color(0.1f, 0.1f, 0.12f), 0.5f, 0.4f);
            var chrome = Mats.Lit(new Color(0.6f, 0.62f, 0.65f), 0.7f, 1f);
            Cabinet(shell, dark);
            // dead sign with a faded "???"
            float sy = H - 0.29f;
            Prims.Make(PrimitiveType.Cube, t, new Vector3(0, sy, D * 0.5f + 0.005f), new Vector3(W - 0.08f, 0.46f, 0.02f), Mats.Lit(new Color(0.2f, 0.21f, 0.24f), 0.7f), null, "DeadSign");
            Label(t, "???", new Vector3(0, sy, D * 0.5f + 0.02f), 0.6f, new Color(0.45f, 0.47f, 0.5f), 70);
            // empty window: bare shelves, a couple of dust bunnies
            for (int row = 0; row < 3; row++)
            {
                float y = 0.72f + row * 0.25f;
                Prims.Make(PrimitiveType.Cube, t, new Vector3(WinX, y - 0.11f, D * 0.5f - Recess * 0.5f), new Vector3(WinW, 0.015f, Recess - 0.02f), chrome, null, "Shelf");
                if (rng.NextDouble() < 0.5)
                    Prims.Make(PrimitiveType.Sphere, t, new Vector3(WinX + ((float)rng.NextDouble() - 0.5f) * 0.4f, y - 0.085f, D * 0.5f - Recess * 0.5f),
                        new Vector3(0.06f, 0.035f, 0.05f), Mats.Lit(new Color(0.55f, 0.53f, 0.5f), 0.05f), null, "DustBunny");
            }
            Prims.Make(PrimitiveType.Cube, t, new Vector3(WinX, (WinY0 + WinY1) * 0.5f, D * 0.5f - 0.01f), new Vector3(WinW, WinY1 - WinY0, 0.01f),
                Mats.Transparent(new Color(0.75f, 0.78f, 0.75f, 0.22f)), null, "Glass");
            float px = W * 0.5f - 0.13f;
            Prims.Make(PrimitiveType.Cube, t, new Vector3(px, 1.0f, D * 0.5f + 0.004f), new Vector3(0.2f, 0.85f, 0.012f), dark, null, "Panel");
            Prims.Make(PrimitiveType.Cube, t, new Vector3(px, 1.12f, D * 0.5f + 0.012f), new Vector3(0.05f, 0.08f, 0.01f), chrome, null, "CoinSlot");
            Prims.Make(PrimitiveType.Cube, t, new Vector3(-0.05f, 0.33f, D * 0.5f - 0.02f), new Vector3(W * 0.75f, 0.22f, 0.06f), dark, null, "TrayHole");
            // the paper sign, taped on crooked
            float tilt = (float)(rng.NextDouble() * 14.0 - 7.0);
            var sign = Prims.Empty("OutOfOrderSign", t, new Vector3(-0.08f, 1.08f, D * 0.5f + 0.022f)).transform;
            sign.localRotation = Quaternion.Euler(0f, 0f, tilt);
            Prims.Make(PrimitiveType.Cube, sign, Vector3.zero, new Vector3(0.5f, 0.32f, 0.004f), Mats.Lit(new Color(0.96f, 0.95f, 0.9f), 0.1f), null, "Paper");
            var tape = Mats.Transparent(new Color(0.95f, 0.92f, 0.7f, 0.6f));
            for (int s2 = -1; s2 <= 1; s2 += 2)
                Prims.Make(PrimitiveType.Cube, sign, new Vector3(s2 * 0.22f, 0.15f, 0.003f), new Vector3(0.12f, 0.04f, 0.002f), tape, Quaternion.Euler(0, 0, s2 * 35f), "Tape");
            var txt = Label(sign, "OUT OF\nORDER", new Vector3(0f, 0f, 0.004f), 0.44f, new Color(0.8f, 0.1f, 0.1f), 36);
            txt.lineSpacing = 0.85f;
        }

        /// <summary>An item icon as a world-space image (local +Z of 'rot' is the way it's read).</summary>
        static void Icon(Transform parent, Vector3 localPos, Quaternion localRot, float size, string icon)
        {
            var go = new GameObject("Icon", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(256, 256);
            rt.localScale = Vector3.one * (size / 256f);
            var igo = new GameObject("Image", typeof(RectTransform));
            var irt = (RectTransform)igo.transform;
            irt.SetParent(rt, false);
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = irt.offsetMax = Vector2.zero;
            var img = igo.AddComponent<Image>();
            img.sprite = Glyphs.Icon(icon);
            img.preserveAspect = true;
            img.raycastTarget = false;
            if (img.sprite == null) img.enabled = false;
        }

        /// <summary>A big icon on one side: side -1 = local -X, +1 = local +X.</summary>
        void SideSticker(Transform t, Material dark, int side)
        {
            float x = side * W * 0.5f, o = side; // offsets push outward from that side
            var face = Quaternion.Euler(0f, side < 0 ? 90f : -90f, 0f); // readable from that side
            Prims.Make(PrimitiveType.Cube, t, new Vector3(x + o * 0.006f, 1.28f, 0), new Vector3(0.012f, 1.0f, D - 0.08f), dark, null, "SidePanel");
            Prims.Make(PrimitiveType.Cube, t, new Vector3(x + o * 0.008f, 1.28f, 0), new Vector3(0.012f, 0.96f, D - 0.12f), Mats.Lit(new Color(0.9f, 0.95f, 1f), 0.6f, 0f, new Color(0.25f, 0.3f, 0.35f)), null, "SideGlow");
            Icon(t, new Vector3(x + o * 0.02f, 1.36f, 0f), face, D - 0.14f, Item.icon);
            var lbl = Label(t, Item.signText, new Vector3(x + o * 0.02f, 0.92f, 0f), D - 0.2f, SignColor * 0.7f + new Color(0, 0, 0, 1f), Item.signText.Length > 3 ? 42 : 54);
            lbl.transform.parent.localRotation = face;
        }

        /// <summary>A little model of the item, for the shelves and the tray.</summary>
        public static Transform MakeProduct(ItemDef item, Transform parent, Vector3 localPos, float scale)
        {
            if (item != null)
                switch (item.id)
                {
                    case Items.Backpack: return MakeMiniPack(parent, localPos, scale);
                    case Items.CountAnt: return Mini(parent, localPos, scale, "MiniAnt", (t, k) =>
                    {
                        var black = Mats.Lit(new Color(0.08f, 0.07f, 0.09f), 0.7f);
                        for (int i = 0; i < 3; i++)
                            Prims.Make(PrimitiveType.Sphere, t, new Vector3(0, 0.035f, -0.045f + i * 0.045f), Vector3.one * (i == 1 ? 0.04f : 0.05f), black, null, "Segment");
                        Prims.Make(PrimitiveType.Cube, t, new Vector3(0, 0.07f, 0.05f), new Vector3(0.05f, 0.035f, 0.01f), Mats.Lit(new Color(0.85f, 0.85f, 0.8f), 0.4f), Quaternion.Euler(-30, 0, 0), "Calculator");
                    });
                    case Items.PirateFoot: return Mini(parent, localPos, scale, "MiniPeg", (t, k) =>
                    {
                        var wood = Mats.Lit(new Color(0.5f, 0.32f, 0.16f), 0.3f);
                        Prims.Make(PrimitiveType.Cylinder, t, new Vector3(0, 0.07f, 0), new Vector3(0.025f, 0.07f, 0.025f), wood, null, "Peg");
                        Prims.Make(PrimitiveType.Cylinder, t, new Vector3(0, 0.15f, 0), new Vector3(0.055f, 0.025f, 0.055f), Mats.Lit(new Color(0.3f, 0.18f, 0.1f), 0.4f), null, "Cuff");
                    });
                    case Items.CrossingSign: return Mini(parent, localPos, scale, "MiniSign", (t, k) =>
                    {
                        Prims.Make(PrimitiveType.Cylinder, t, new Vector3(0, 0.06f, 0), new Vector3(0.012f, 0.06f, 0.012f), Mats.Lit(new Color(0.8f, 0.8f, 0.82f), 0.8f, 1f), null, "Pole");
                        Prims.Make(PrimitiveType.Cylinder, t, new Vector3(0, 0.15f, 0), new Vector3(0.1f, 0.006f, 0.1f), Mats.Lit(new Color(0.9f, 0.12f, 0.12f), 0.5f), Quaternion.Euler(90, 0, 0), "Paddle");
                    });
                    case Items.FilterPiece: return Mini(parent, localPos, scale, "MiniFilter", (t, k) =>
                    {
                        var pipe = Mats.Lit(new Color(0.85f, 0.87f, 0.9f), 0.6f, 0.3f);
                        Prims.Make(PrimitiveType.Cylinder, t, new Vector3(0, 0.04f, 0), new Vector3(0.03f, 0.08f, 0.03f), pipe, Quaternion.Euler(0, 0, 90), "Bar");
                        Prims.Make(PrimitiveType.Cylinder, t, new Vector3(0, 0.04f, 0.04f), new Vector3(0.03f, 0.04f, 0.03f), pipe, Quaternion.Euler(90, 0, 0), "Stem");
                        Prims.Make(PrimitiveType.Cylinder, t, new Vector3(0, 0.07f, 0), new Vector3(0.05f, 0.05f, 0.05f), Mats.Lit(new Color(0.3f, 0.6f, 0.95f), 0.5f), null, "Canister");
                    });
                    case Items.Funnel: return Mini(parent, localPos, scale, "MiniFunnel", (t, k) =>
                    {
                        var tin = Mats.Lit(new Color(0.85f, 0.75f, 0.3f), 0.7f, 0.8f);
                        Prims.Make(PrimitiveType.Cylinder, t, new Vector3(0, 0.03f, 0), new Vector3(0.025f, 0.03f, 0.025f), tin, null, "Spout");
                        Prims.Make(PrimitiveType.Cylinder, t, new Vector3(0, 0.09f, 0), new Vector3(0.06f, 0.03f, 0.06f), tin, null, "Cone");
                        Prims.Make(PrimitiveType.Cylinder, t, new Vector3(0, 0.13f, 0), new Vector3(0.11f, 0.015f, 0.11f), tin, null, "Mouth");
                    });
                }
            return MakeBottle(parent, localPos, scale);
        }

        static Transform Mini(Transform parent, Vector3 localPos, float scale, string name, System.Action<Transform, float> build)
        {
            var b = Prims.Empty(name, parent, localPos).transform;
            b.localScale = Vector3.one * scale;
            build(b, scale);
            return b;
        }

        /// <summary>A tiny backpack: body, flap, front pocket.</summary>
        public static Transform MakeMiniPack(Transform parent, Vector3 localPos, float scale)
        {
            var b = Prims.Empty("MiniPack", parent, localPos).transform;
            b.localScale = Vector3.one * scale;
            var canvas = Mats.Lit(new Color(0.85f, 0.45f, 0.12f), 0.25f);
            var dark = Mats.Lit(new Color(0.35f, 0.2f, 0.1f), 0.3f);
            Prims.Make(PrimitiveType.Cube, b, new Vector3(0, 0.08f, 0), new Vector3(0.12f, 0.16f, 0.08f), canvas, null, "Body");
            Prims.Make(PrimitiveType.Cube, b, new Vector3(0, 0.15f, 0.005f), new Vector3(0.125f, 0.04f, 0.085f), dark, null, "Flap");
            Prims.Make(PrimitiveType.Cube, b, new Vector3(0, 0.05f, 0.045f), new Vector3(0.08f, 0.06f, 0.02f), dark, null, "Pocket");
            return b;
        }

        /// <summary>A bottle model (for drinking animations and the dispenser).</summary>
        public static Transform MakeBottle(Transform parent, Vector3 localPos, float scale)
        {
            var b = Prims.Empty("Bottle", parent, localPos).transform;
            b.localScale = Vector3.one * scale;
            Prims.Make(PrimitiveType.Cylinder, b, new Vector3(0, 0.08f, 0), new Vector3(0.07f, 0.08f, 0.07f), Mats.Transparent(new Color(0.55f, 0.8f, 1f, 0.55f)), null, "Body");
            Prims.Make(PrimitiveType.Cylinder, b, new Vector3(0, 0.07f, 0), new Vector3(0.072f, 0.025f, 0.072f), Mats.Lit(new Color(0.25f, 0.55f, 1f), 0.5f), null, "Label");
            Prims.Make(PrimitiveType.Cylinder, b, new Vector3(0, 0.175f, 0), new Vector3(0.035f, 0.015f, 0.035f), Mats.Lit(Color.white, 0.5f), null, "Cap");
            return b;
        }

        static Text Label(Transform parent, string text, Vector3 localPos, float width, Color color, int size)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0, 180f, 0); // readable from the front (+Z)
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(300, 90);
            rt.localScale = Vector3.one * (width / 300f);
            var tgo = new GameObject("Text", typeof(RectTransform));
            var trt = (RectTransform)tgo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var txt = tgo.AddComponent<Text>();
            txt.font = Popup.Font;
            txt.text = text;
            txt.fontSize = size;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = color;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow; // two-line labels (SOLD / OUT) aren't cut off
            return txt;
        }

        void RefreshPrice()
        {
            if (priceText == null || Game.Config == null) return;
            if (Item == null) return;
            if (Item.consumable) { priceText.text = "$" + ShortMoney(Items.NextPrice(Item.id)); return; }
            int lvl = Items.Level(Item.id), max = Items.MaxLevel(Item.id);
            priceText.text = lvl >= max ? (max > 1 ? "MAX" : "SOLD\nOUT") : "$" + ShortMoney(Items.NextPrice(Item.id));
        }

        /// <summary>$500, $12.5K, $1.6M, $977M, $4.9B: fits the little price display.</summary>
        public static string ShortMoney(long v)
        {
            if (v < 10000) return v.ToString("N0");
            if (v < 1000000) return (v / 1000.0).ToString("0.#") + "K";
            if (v < 1000000000) return (v / 1000000.0).ToString("0.#") + "M";
            return (v / 1000000000.0).ToString("0.#") + "B";
        }

        // ================================================================== buying

        public void TryBuy(PlayerController p)
        {
            if (OutOfOrder)
            {
                Popup.Spawn(PromptAnchor, "OUT OF ORDER", Mats.Danger, 34, 0.9f);
                Sfx.Play(Sfx.I != null ? Sfx.I.clunk : null, 0.6f, 0.7f);
                shake = 0.6f; // give it a thump anyway
                return;
            }
            string id = Item.id;
            if (Item.consumable) { BuyOne(p); return; }
            int lvl = Items.Level(id), max = Items.MaxLevel(id);
            if (lvl >= max)
            {
                Popup.Spawn(PromptAnchor, max > 1 ? "MAXED OUT" : "EVERYONE HAS ONE", Color.white, 34, 0.9f);
                Sfx.Play(Sfx.I != null ? Sfx.I.nope : null, 0.6f, 1f);
                shake = 0.3f;
                return;
            }
            if (lvl == 0 && Item.requires != null && !Items.TeamOwns(Item.requires))
            {
                Popup.Spawn(PromptAnchor, $"NEEDS A {Items.Get(Item.requires)?.name}", Color.white, 34, 1f);
                Sfx.Play(Sfx.I != null ? Sfx.I.nope : null, 0.6f, 1f);
                shake = 0.3f;
                return;
            }
            long price = Items.NextPrice(id);
            if (Game.Bootstrap.Bank < price)
            {
                Popup.Spawn(PromptAnchor, $"NEED ${price - Game.Bootstrap.Bank:N0} MORE", Mats.Danger, 36, 1f);
                Sfx.Play(Sfx.I != null ? Sfx.I.nope : null, 0.7f, 0.9f);
                shake = 0.5f;
                return;
            }
            Game.Bootstrap.Bank -= (int)price;
            Items.BuyForTeam(id);
            int now = Items.Level(id);
            RefreshPrice();
            shake = 1f;
            signPulse = 1f;
            Popup.Spawn(PromptAnchor, $"-${price:N0}", Mats.Danger, 44, 1.1f);
            Sfx.Play(Sfx.I != null ? Sfx.I.coin : null, 0.9f, 0.9f);
            Juice.Delay(0.25f, () => Sfx.Play(Sfx.I != null ? Sfx.I.clunk : null, 0.8f, 1.2f));
            string banner = BuyBanner(Item, now);
            Color bc = Color.Lerp(SignColor, Color.white, 0.3f);
            Juice.Delay(0.4f, () =>
            {
                Banner.Show(banner, bc, 80, 2.5f, 60f);
                Sfx.Play(Sfx.I != null ? Sfx.I.fanfare : null, 0.7f, 1.15f, 0f);
            });
            // the product drops into the tray, and every critter gets a little splash of excitement
            var drop = MakeProduct(Item, tray, new Vector3(0, 0.5f, 0), 1.2f);
            Juice.ScaleTo(drop, Vector3.one * 0.2f, Vector3.one * 1.2f, 0.2f, Juice.OutBack);
            StartCoroutine(DropBottle(drop));
            foreach (var pl in Game.Players)
            {
                if (pl == null) continue;
                pl.Cheer();
                pl.OnItemBought(Item);
                Fx.Confetti(pl.transform.position + Vector3.up, 18, 4f);
                string line = Item.id == Items.WaterBottle ? $"+ BOTTLE  [{pl.InventoryLabel}]" : now == 1 ? $"+ {Item.name}" : $"{Item.name} LV {now}";
                Popup.Spawn(pl.transform.position + Vector3.up * 1.8f, line, bc, 30, 1.8f);
            }
        }

        /// <summary>Consumables (the filter piece): the buyer pays and it drops straight into their hands.</summary>
        void BuyOne(PlayerController p)
        {
            long price = Items.NextPrice(Item.id);
            if (Game.Bootstrap.Bank < price)
            {
                Popup.Spawn(PromptAnchor, $"NEED ${price - Game.Bootstrap.Bank:N0} MORE", Mats.Danger, 36, 1f);
                Sfx.Play(Sfx.I != null ? Sfx.I.nope : null, 0.7f, 0.9f);
                shake = 0.5f;
                return;
            }
            Game.Bootstrap.Bank -= (int)price;
            shake = 0.8f;
            signPulse = 1f;
            Popup.Spawn(PromptAnchor, $"-${price:N0}", Mats.Danger, 40, 1f);
            Sfx.Play(Sfx.I != null ? Sfx.I.coin : null, 0.9f, 1f);
            Juice.Delay(0.2f, () => Sfx.Play(Sfx.I != null ? Sfx.I.clunk : null, 0.8f, 1.3f));
            if (Item.id == Items.FilterPiece && p != null)
            {
                var piece = PipePiece.Create(PipeType.Filter, 0, tray.position + Vector3.up * 0.2f);
                p.ReceivePiece(piece);
                Popup.Spawn(p.transform.position + Vector3.up * 1.8f, "+ FILTER", Color.Lerp(SignColor, Color.white, 0.3f), 30, 1.2f);
            }
        }

        static string BuyBanner(ItemDef item, int level)
        {
            if (item.id == Items.WaterBottle) return "WATER BOTTLES FOR EVERYONE!";
            if (item.id == Items.Backpack) return level == 1 ? "BACKPACKS FOR EVERYONE!" : $"BACKPACK LV {level}: {Backpacks.StageName(level)}";
            return level == 1 ? $"{item.name} FOR EVERYONE!" : $"{item.name} LV {level}!";
        }

        System.Collections.IEnumerator DropBottle(Transform b)
        {
            float t = 0f;
            while (t < 0.35f && b != null)
            {
                t += Time.deltaTime;
                b.localPosition = new Vector3(0, Mathf.Lerp(0.5f, 0.02f, Juice.InCubic(t / 0.35f)), 0);
                yield return null;
            }
            if (b != null)
            {
                Fx.Dust(b.position, 0.2f, 4);
                Juice.Delay(2.5f, () => { if (b != null) Destroy(b.gameObject); });
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            shake = Mathf.MoveTowards(shake, 0f, dt * 2.5f);
            signPulse = Mathf.MoveTowards(signPulse, 0f, dt * 1.5f);
            if (body != null) body.localPosition = new Vector3(Mathf.Sin(Time.time * 60f) * 0.015f * shake, 0f, 0f);
            if (signMat != null)
            {
                float flick = Theme.Grime > 0.2f && Random.value < Theme.Grime * 0.02f ? 0.3f : 1f;
                var blue = SignColor;
                Mats.SetEmission(signMat, blue * (1.1f + signPulse * 2.5f + Mathf.Sin(Time.time * 3f) * 0.15f) * flick); // not so bright the name washes out
            }
        }
    }
}
