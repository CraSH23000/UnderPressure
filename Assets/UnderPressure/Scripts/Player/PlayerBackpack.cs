using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>Names and looks of the backpack's upgrade levels (bought at the base, for the whole team).</summary>
    public static class Backpacks
    {
        static readonly string[] Stages =
        {
            "BACKPACK", "FANNY PACK ATTACHMENT", "HOVER RIG", "TALLER BEAM", "TWIN EMITTERS",
            "SAFETY TRIM", "STABILIZER FINS", "OVERCLOCKED BEAM", "EXPERIMENTAL FIELD", "HALO TOWER",
        };

        public static string StageName(int level) => level <= 0 ? "" : level <= Stages.Length ? Stages[level - 1] : $"HALO TOWER +{level - Stages.Length}";

        public static int Level => Items.Level(Items.Backpack);

        /// <summary>Spare pieces the backpack holds at the team's current level.</summary>
        public static int Capacity => Level * Mathf.Max(1, Game.Config != null ? Game.Config.backpackPiecesPerLevel : 1);
    }

    /// <summary>
    /// A player's backpack: the pack itself on their back (a camping backpack, then a fanny pack on top, then a white
    /// lab hover rig whose beam holds the pieces floating behind you) and the spare pieces it carries. Stowed pieces are real pipe pieces held
    /// small and following a spot on the stack, so they wobble along behind the critter.
    /// </summary>
    public class PlayerBackpack : MonoBehaviour
    {
        readonly List<PipePiece> stack = new List<PipePiece>();
        Transform pack;
        PlayerRig rig;
        float k = 1f, backZ = 0.35f, baseY = 0.6f, stackY = 1f, step = 0.26f;
        int builtLevel = -1;
        float builtHeight = -1f;
        float bob;

        public int Count => stack.Count;
        public bool HasRoom => stack.Count < Backpacks.Capacity;
        public bool Empty => stack.Count == 0;

        public void Init(PlayerRig r) { rig = r; Rebuild(); }

        /// <summary>Put a piece away (it shrinks and flies onto the top of the stack).</summary>
        public void Stash(PipePiece p)
        {
            if (p == null) return;
            p.SetState(PieceState.Held);
            p.Spring.SetBase(0.3f);
            p.Spring.Squash(-0.4f);
            stack.Add(p);
            Sfx.Play(Sfx.I != null ? Sfx.I.pop : null, 0.5f, 1.4f + stack.Count * 0.05f);
            bob = 1f;
        }

        /// <summary>Take the top piece off the stack (back to full hand size).</summary>
        public PipePiece Pop()
        {
            if (stack.Count == 0) return null;
            var p = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            if (p == null) return null;
            p.Spring.SetBase(0.58f);
            p.Spring.Squash(-0.3f);
            Sfx.Play(Sfx.I != null ? Sfx.I.pickup : null, 0.5f, 1.25f);
            bob = 0.6f;
            return p;
        }

        /// <summary>Teleport / vaporize: the stowed pieces go with the critter.</summary>
        public void Clear()
        {
            foreach (var p in stack) if (p != null) p.gameObject.SetActive(false);
            stack.Clear();
        }

        /// <summary>Hide or show the stowed pieces (while the critter is away: in the pit, mid-arrival).</summary>
        public void SetShown(bool on)
        {
            foreach (var p in stack) if (p != null && p.gameObject.activeSelf != on) p.gameObject.SetActive(on);
            if (pack != null && pack.gameObject.activeSelf != on) pack.gameObject.SetActive(on);
        }

        Transform Anchor => rig != null && rig.BodyPivot != null ? rig.BodyPivot : transform;
        int cols = 1;
        float colGap = 0.25f, stackZ = 0.5f;

        void LateUpdate()
        {
            if (rig == null) return;
            // (a new body rebuilds the rig and takes the pack with it, so check it's still there)
            if (Backpacks.Level != builtLevel || !Mathf.Approximately(rig.Height, builtHeight) || (builtLevel > 0 && pack == null)) Rebuild();
            bob = Mathf.MoveTowards(bob, 0f, Time.deltaTime * 3f);
            if (pack != null) pack.localScale = Vector3.one * (1f + Juice.OutBack(bob) * 0.08f);
            // the stack follows the body (jumps, crawling), swaying a little as you move
            var anchor = Anchor;
            for (int i = 0; i < stack.Count; i++)
            {
                var p = stack[i];
                if (p == null) continue;
                int row = i / cols, col = i % cols;
                float x = cols > 1 ? (col - (cols - 1) * 0.5f) * colGap : 0f;
                float hover = builtLevel >= 3 ? Mathf.Sin(Time.time * 2.2f + i * 0.9f) * 0.03f * k : 0f; // tractor-beam float
                float sway = Mathf.Sin(Time.time * 3f + i * 0.7f) * 0.012f * row;
                p.HoldTarget = anchor.TransformPoint(new Vector3(x + sway, stackY + row * step + hover, -stackZ));
            }
        }

        /// <summary>Build the pack for the team's backpack level, sized to this critter. Levels 1-2 are a camping
        /// backpack (+ fanny pack); from 3 on it's an Aperture-style hover rig: an open white back plate whose
        /// emitters hold the spare pieces floating in a beam behind you, starting down by your legs.</summary>
        public void Rebuild()
        {
            if (pack != null) Destroy(pack.gameObject);
            pack = null;
            builtLevel = Backpacks.Level;
            builtHeight = rig != null ? rig.Height : 1.2f;
            if (builtLevel <= 0 || rig == null) return;

            k = Mathf.Clamp(rig.Height / 1.2f, 0.6f, 1.6f);
            backZ = Mathf.Max(0.22f, 0.36f * Mathf.Clamp(rig.Footprint, 0.6f, 1.8f));
            baseY = Mathf.Max(0.3f, rig.HoldHeight);
            pack = new GameObject("Backpack").transform;
            pack.SetParent(Anchor, false);
            pack.localPosition = new Vector3(0f, baseY, -backZ);
            int L = builtLevel;
            if (L <= 2) BuildCamping(L); else BuildHoverRig(L);
        }

        void BuildCamping(int L)
        {
            var canvas = Mats.Lit(new Color(0.85f, 0.45f, 0.12f), 0.25f);
            var dark = Mats.Lit(new Color(0.35f, 0.2f, 0.1f), 0.3f);
            var strap = Mats.Lit(new Color(0.12f, 0.12f, 0.14f), 0.4f);
            float w = 0.42f * k, h = 0.5f * k, d = 0.22f * k;
            float top = h * 0.5f;
            Prims.Make(PrimitiveType.Cube, pack, new Vector3(0, 0, -d * 0.5f), new Vector3(w, h, d), canvas, null, "Pack");
            Prims.Make(PrimitiveType.Cube, pack, new Vector3(0, h * 0.38f, -d * 0.52f), new Vector3(w * 1.02f, h * 0.28f, d * 1.06f), dark, null, "Flap");
            Prims.Make(PrimitiveType.Cube, pack, new Vector3(0, -h * 0.18f, -d - 0.03f * k), new Vector3(w * 0.7f, h * 0.36f, 0.06f * k), dark, null, "Pocket");
            for (int s = -1; s <= 1; s += 2)
                Prims.Make(PrimitiveType.Cube, pack, new Vector3(s * w * 0.28f, h * 0.45f, 0.02f * k), new Vector3(0.05f * k, 0.04f * k, 0.12f * k), strap, null, "Strap");
            if (L >= 2)
            {
                Prims.Make(PrimitiveType.Cube, pack, new Vector3(0, top + 0.06f * k, -d * 0.5f), new Vector3(w * 0.8f, 0.12f * k, d * 0.75f), Mats.Lit(new Color(0.2f, 0.75f, 0.7f), 0.3f), null, "FannyPack");
                Prims.Make(PrimitiveType.Cube, pack, new Vector3(0, top + 0.06f * k, -d * 0.9f), new Vector3(w * 0.5f, 0.02f * k, 0.01f), strap, null, "Zip");
                top += 0.12f * k;
            }
            cols = 1;
            stackY = baseY + top + 0.16f * k;
            stackZ = backZ + d * 0.5f;
            step = 0.26f * k;
        }

        void BuildHoverRig(int L)
        {
            var white = Mats.Lit(new Color(0.95f, 0.96f, 0.97f), 0.85f);
            var black = Mats.Lit(new Color(0.08f, 0.08f, 0.1f), 0.6f);
            var blue = Mats.Lit(new Color(0.3f, 0.7f, 1f), 0.9f, 0f, new Color(0.5f, 1.3f, 2.4f));
            var orange = Mats.Lit(new Color(1f, 0.55f, 0.1f), 0.9f, 0f, new Color(2.2f, 1.0f, 0.2f));
            var beam = Mats.Transparent(new Color(0.4f, 0.8f, 1f, 0.12f));

            cols = Backpacks.Capacity > 4 ? 2 : 1;
            colGap = 0.3f * k;
            stackZ = backZ + 0.32f * k;                 // far enough back to clear the legs
            stackY = Mathf.Max(0.12f, 0.2f * rig.Height); // starting down by the legs
            step = 0.24f * k;
            int rows = Mathf.Max(1, Mathf.CeilToInt(Backpacks.Capacity / (float)cols));
            float zBeam = -(stackZ - backZ);            // pack space: pack sits at -backZ
            float yBeam0 = stackY - baseY - 0.12f * k;  // pack space, at the emitter

            // the spine plate on your back, with a black seam and a blue status light
            Prims.Make(PrimitiveType.Cube, pack, new Vector3(0, 0.02f * k, -0.05f * k), new Vector3(0.34f * k, 0.5f * k, 0.1f * k), white, null, "Spine");
            Prims.Make(PrimitiveType.Cube, pack, new Vector3(0, 0.02f * k, -0.101f * k), new Vector3(0.02f * k, 0.46f * k, 0.005f), black, null, "Seam");
            Prims.Make(PrimitiveType.Sphere, pack, new Vector3(0, 0.2f * k, -0.105f * k), Vector3.one * 0.045f * k, blue, null, "Status");
            for (int s = -1; s <= 1; s += 2)
                Prims.Make(PrimitiveType.Cube, pack, new Vector3(s * 0.12f * k, 0.25f * k, 0.03f * k), new Vector3(0.07f * k, 0.05f * k, 0.12f * k), black, null, "ShoulderPad");

            // an arm curving down and back to the emitter(s) the pieces float above
            Prims.Make(PrimitiveType.Cube, pack, new Vector3(0, (yBeam0 - 0.2f * k) * 0.5f, (zBeam - 0.1f * k) * 0.5f),
                new Vector3(0.05f * k, Mathf.Abs(yBeam0) + 0.25f * k, 0.05f * k), white, Quaternion.Euler(-22f, 0, 0), "Arm");
            for (int c = 0; c < cols; c++)
            {
                float x = cols > 1 ? (c - 0.5f) * colGap : 0f;
                Prims.Make(PrimitiveType.Cylinder, pack, new Vector3(x, yBeam0, zBeam), new Vector3(0.24f * k, 0.02f * k, 0.24f * k), white, null, "Emitter");
                Prims.Make(PrimitiveType.Cylinder, pack, new Vector3(x, yBeam0 + 0.022f * k, zBeam), new Vector3(0.18f * k, 0.006f, 0.18f * k), blue, null, "EmitterGlow");
                float bh = rows * step + 0.1f * k;
                Prims.Make(PrimitiveType.Cylinder, pack, new Vector3(x, yBeam0 + bh * 0.5f, zBeam), new Vector3(0.2f * k, bh * 0.5f, 0.2f * k), beam, null, "Beam");
            }
            if (cols > 1) // a bar joining the two emitters
                Prims.Make(PrimitiveType.Cube, pack, new Vector3(0, yBeam0, zBeam), new Vector3(colGap, 0.03f * k, 0.04f * k), white, null, "EmitterBar");
            // later upgrades: Aperture orange accents, side fins, and a ring at the top of the beam
            if (L >= 6)
                for (int s = -1; s <= 1; s += 2)
                    Prims.Make(PrimitiveType.Cube, pack, new Vector3(s * 0.15f * k, -0.05f * k, -0.07f * k), new Vector3(0.025f * k, 0.3f * k, 0.03f * k), orange, null, "Accent");
            if (L >= 7)
                for (int s = -1; s <= 1; s += 2)
                    Prims.Make(PrimitiveType.Cube, pack, new Vector3(s * 0.22f * k, 0.05f * k, -0.04f * k), new Vector3(0.12f * k, 0.32f * k, 0.03f * k), white, Quaternion.Euler(0, s * 25f, 0), "Fin");
            if (L >= 10)
            {
                float topY = yBeam0 + rows * step + 0.18f * k;
                for (int c = 0; c < cols; c++)
                {
                    float x = cols > 1 ? (c - 0.5f) * colGap : 0f;
                    Prims.Make(PrimitiveType.Cylinder, pack, new Vector3(x, topY, zBeam), new Vector3(0.26f * k, 0.01f * k, 0.26f * k), orange, null, "Halo");
                }
            }
        }
    }
}
