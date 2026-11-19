using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// Outfit pieces built from primitives. Indices match CritterLook.Hats / Torsos / Feet.
    /// Hats sit on a "hat anchor" at the top of the head (local +Z = forward), torsos are a shell around the
    /// lower body, and shoes replace the default soles on each foot pivot.
    /// </summary>
    public static class Accessories
    {
        static Material M(Color c, float smooth = 0.4f, float metal = 0f) => Mats.Lit(c, smooth, metal);

        // =================================================================== hats

        public static void AddHat(int hat, Transform a)
        {
            switch (hat)
            {
                case 1: BaseballCap(a); break;
                case 2: CowboyHat(a); break;
                case 3: Sombrero(a); break;
                case 4: ShowerCap(a); break;
            }
        }

        static void BaseballCap(Transform a)
        {
            var red = M(new Color(0.85f, 0.16f, 0.18f), 0.35f);
            var white = M(Color.white, 0.5f);
            Prims.Make(PrimitiveType.Sphere, a, new Vector3(0, 0.02f, -0.02f), new Vector3(0.6f, 0.36f, 0.6f), red, null, "Crown");
            Prims.Make(PrimitiveType.Cube, a, new Vector3(0, -0.01f, 0.33f), new Vector3(0.44f, 0.03f, 0.34f), red, Quaternion.Euler(8f, 0, 0), "Brim");
            Prims.Make(PrimitiveType.Sphere, a, new Vector3(0, 0.2f, -0.02f), Vector3.one * 0.07f, red, null, "Button");
            Prims.Make(PrimitiveType.Sphere, a, new Vector3(0, 0.08f, 0.27f), new Vector3(0.12f, 0.12f, 0.04f), white, null, "Logo");
        }

        static void CowboyHat(Transform a)
        {
            var leather = M(new Color(0.55f, 0.36f, 0.2f), 0.3f);
            var band = M(new Color(0.18f, 0.12f, 0.08f), 0.4f);
            Prims.Make(PrimitiveType.Cylinder, a, new Vector3(0, 0.02f, 0), new Vector3(0.98f, 0.018f, 0.9f), leather, null, "Brim");
            for (int s = -1; s <= 1; s += 2) // brim curls up at the sides
                Prims.Make(PrimitiveType.Cube, a, new Vector3(s * 0.44f, 0.07f, 0), new Vector3(0.14f, 0.025f, 0.62f), leather, Quaternion.Euler(0, 0, s * 40f), "Curl");
            Prims.Make(PrimitiveType.Cylinder, a, new Vector3(0, 0.18f, 0), new Vector3(0.44f, 0.16f, 0.4f), leather, null, "Crown");
            Prims.Make(PrimitiveType.Cylinder, a, new Vector3(0, 0.07f, 0), new Vector3(0.46f, 0.035f, 0.42f), band, null, "Band");
            Prims.Make(PrimitiveType.Cube, a, new Vector3(0, 0.34f, 0), new Vector3(0.06f, 0.04f, 0.3f), band, null, "Crease");
        }

        static void Sombrero(Transform a)
        {
            var straw = M(new Color(0.93f, 0.8f, 0.45f), 0.25f);
            var red = M(new Color(0.85f, 0.15f, 0.2f), 0.3f);
            var green = M(new Color(0.15f, 0.6f, 0.3f), 0.3f);
            Prims.Make(PrimitiveType.Cylinder, a, new Vector3(0, 0.02f, 0), new Vector3(1.5f, 0.016f, 1.5f), straw, null, "Brim");
            for (int i = 0; i < 18; i++) // pom-pom fringe on the upturned rim
            {
                float t = i / 18f * Mathf.PI * 2f;
                Prims.Make(PrimitiveType.Sphere, a, new Vector3(Mathf.Cos(t) * 0.74f, 0.07f, Mathf.Sin(t) * 0.74f), Vector3.one * 0.08f, (i & 1) == 0 ? red : green, null, "Pom");
            }
            float y = 0.06f, r = 0.5f;
            for (int k = 0; k < 4; k++) // stacked, narrowing crown
            {
                Prims.Make(PrimitiveType.Cylinder, a, new Vector3(0, y + 0.06f, 0), new Vector3(r, 0.06f, r), straw, null, "Crown");
                y += 0.11f; r *= 0.82f;
            }
            Prims.Make(PrimitiveType.Sphere, a, new Vector3(0, y + 0.03f, 0), new Vector3(r, 0.12f, r), straw, null, "Top");
            Prims.Make(PrimitiveType.Cylinder, a, new Vector3(0, 0.1f, 0), new Vector3(0.52f, 0.03f, 0.52f), red, null, "Band");
            Prims.Make(PrimitiveType.Cylinder, a, new Vector3(0, 0.15f, 0), new Vector3(0.46f, 0.02f, 0.46f), green, null, "Band2");
        }

        static void ShowerCap(Transform a)
        {
            var pink = M(new Color(1f, 0.75f, 0.85f), 0.7f);
            var dot = M(Color.white, 0.6f);
            Prims.Make(PrimitiveType.Sphere, a, new Vector3(0, 0.04f, -0.02f), new Vector3(0.74f, 0.42f, 0.74f), pink, null, "Cap");
            for (int i = 0; i < 16; i++) // frilly elastic rim
            {
                float t = i / 16f * Mathf.PI * 2f;
                Prims.Make(PrimitiveType.Sphere, a, new Vector3(Mathf.Cos(t) * 0.36f, -0.04f, Mathf.Sin(t) * 0.36f - 0.02f), new Vector3(0.12f, 0.07f, 0.12f), pink, null, "Frill");
            }
            Vector3[] dots = { new Vector3(0.12f, 0.17f, 0.18f), new Vector3(-0.17f, 0.15f, 0.1f), new Vector3(0.05f, 0.23f, -0.12f), new Vector3(-0.08f, 0.2f, -0.24f), new Vector3(0.22f, 0.12f, -0.16f) };
            foreach (var d in dots) Prims.Make(PrimitiveType.Sphere, a, d, Vector3.one * 0.06f, dot, null, "Dot");
        }

        // =================================================================== torsos

        public static void AddTorso(int torso, Transform body, Vector3 c, Vector3 size)
        {
            switch (torso)
            {
                case 1: LabCoat(body, c, size); break;
                case 2: Flannel(body, c, size); break;
                case 3: BikerJacket(body, c, size); break;
            }
        }

        static Transform Shell(Transform body, Vector3 c, Vector3 size, Material m)
        {
            var root = Prims.Empty("Torso", body, Vector3.zero).transform;
            Prims.Make(PrimitiveType.Sphere, root, c, size * 1.04f, m, null, "Shell");
            return root;
        }

        static float Front(Vector3 size) => size.z * 0.52f;

        static void LabCoat(Transform body, Vector3 c, Vector3 size)
        {
            var white = M(new Color(0.96f, 0.97f, 0.98f), 0.35f);
            var seam = M(new Color(0.75f, 0.78f, 0.82f), 0.3f);
            var pen = M(new Color(0.2f, 0.35f, 0.9f), 0.6f);
            var t = Shell(body, c, size, white);
            float f = Front(size);
            Prims.Make(PrimitiveType.Cube, t, c + new Vector3(0, 0, f), new Vector3(0.025f, size.y * 0.8f, 0.02f), seam, null, "Opening");
            for (int s = -1; s <= 1; s += 2)
                Prims.Make(PrimitiveType.Cube, t, c + new Vector3(s * 0.1f, size.y * 0.32f, f - 0.03f), new Vector3(0.12f, 0.2f, 0.03f), white, Quaternion.Euler(0, 0, s * 25f), "Lapel");
            Prims.Make(PrimitiveType.Cube, t, c + new Vector3(0.17f, 0.02f, f - 0.06f), new Vector3(0.14f, 0.1f, 0.03f), seam, null, "Pocket");
            Prims.Make(PrimitiveType.Cylinder, t, c + new Vector3(0.14f, 0.09f, f - 0.05f), new Vector3(0.025f, 0.06f, 0.025f), pen, null, "Pen");
            Prims.Make(PrimitiveType.Cube, t, c + new Vector3(-0.17f, 0.12f, f - 0.06f), new Vector3(0.1f, 0.05f, 0.02f), M(new Color(0.2f, 0.6f, 0.9f), 0.5f), null, "Badge");
        }

        static void Flannel(Transform body, Vector3 c, Vector3 size)
        {
            var red = M(new Color(0.75f, 0.14f, 0.12f), 0.25f);
            var black = M(new Color(0.08f, 0.07f, 0.08f), 0.25f);
            var button = M(new Color(0.92f, 0.88f, 0.75f), 0.5f);
            var t = Shell(body, c, size, red);
            for (int i = -1; i <= 1; i++) // plaid: horizontal bands around the shell...
                Prims.Make(PrimitiveType.Cylinder, t, c + new Vector3(0, i * size.y * 0.22f, 0), new Vector3(size.x * 1.07f * Mathf.Sqrt(1f - i * i * 0.18f), 0.025f, size.z * 1.07f * Mathf.Sqrt(1f - i * i * 0.18f)), black, null, "Band");
            float f = Front(size);
            for (int i = -2; i <= 2; i++) // ...and vertical stripes down the front
                if (i != 0) Prims.Make(PrimitiveType.Cube, t, c + new Vector3(i * 0.1f, 0, f - Mathf.Abs(i) * 0.03f), new Vector3(0.03f, size.y * 0.75f, 0.02f), black, null, "Stripe");
            for (int k = 0; k < 3; k++)
                Prims.Make(PrimitiveType.Sphere, t, c + new Vector3(0, size.y * (0.2f - k * 0.18f), f + 0.005f), Vector3.one * 0.04f, button, null, "Button");
            for (int s = -1; s <= 1; s += 2)
                Prims.Make(PrimitiveType.Cube, t, c + new Vector3(s * 0.09f, size.y * 0.36f, f - 0.05f), new Vector3(0.11f, 0.08f, 0.03f), red, Quaternion.Euler(0, 0, s * 30f), "Collar");
        }

        static void BikerJacket(Transform body, Vector3 c, Vector3 size)
        {
            var leather = M(new Color(0.07f, 0.07f, 0.08f), 0.85f);
            var silver = M(new Color(0.82f, 0.84f, 0.88f), 0.9f, 1f);
            var t = Shell(body, c, size, leather);
            float f = Front(size);
            Prims.Make(PrimitiveType.Cube, t, c + new Vector3(0.03f, 0, f + 0.005f), new Vector3(0.025f, size.y * 0.8f, 0.02f), silver, Quaternion.Euler(0, 0, 18f), "Zip");
            for (int s = -1; s <= 1; s += 2)
            {
                Prims.Make(PrimitiveType.Cube, t, c + new Vector3(s * 0.14f, size.y * 0.3f, f - 0.04f), new Vector3(0.16f, 0.22f, 0.03f), leather, Quaternion.Euler(0, 0, s * 30f), "Lapel");
                for (int k = 0; k < 3; k++) // shoulder studs
                    Prims.Make(PrimitiveType.Sphere, t, c + new Vector3(s * (0.22f + k * 0.07f), size.y * (0.34f - k * 0.08f), f * 0.7f - k * 0.05f), Vector3.one * 0.045f, silver, null, "Stud");
            }
            Prims.Make(PrimitiveType.Cube, t, c + new Vector3(-0.15f, -0.08f, f - 0.03f), new Vector3(0.12f, 0.015f, 0.02f), silver, Quaternion.Euler(0, 0, -20f), "PocketZip");
        }

        // =================================================================== arms + legs (secret unlocks)

        public static void AddArms(int arms, Transform handL, Transform handR)
        {
            if (arms != 1) return;
            foreach (var hand in new[] { handL, handR }) Floatie(hand);
        }

        /// <summary>An inflatable arm band around a paw (hands are 0.17 spheres, so sizes are divided by that).</summary>
        static void Floatie(Transform hand)
        {
            float k = 1f / Mathf.Max(0.01f, hand.localScale.x);
            var orange = M(new Color(1f, 0.5f, 0.1f), 0.85f);
            var white = M(Color.white, 0.8f);
            var root = Prims.Empty("Floatie", hand, Vector3.zero).transform;
            for (int i = 0; i < 10; i++) // a puffy ring of beads makes the torus
            {
                float t = i / 10f * Mathf.PI * 2f;
                Prims.Make(PrimitiveType.Sphere, root, new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t)) * 0.13f * k, Vector3.one * 0.12f * k,
                    i % 5 == 0 ? white : orange, null, "Puff");
            }
            Prims.Make(PrimitiveType.Cylinder, root, new Vector3(0.18f * k, 0.03f * k, 0f), new Vector3(0.03f, 0.03f, 0.03f) * k, white, null, "Valve");
        }

        public static void AddLegs(int legs, Transform body, Vector3 c, Vector3 size)
        {
            if (legs != 1) return;
            var teal = M(new Color(0.1f, 0.75f, 0.8f), 0.45f);
            var white = M(Color.white, 0.5f);
            var yellow = M(new Color(1f, 0.85f, 0.25f), 0.4f);
            var root = Prims.Empty("Trunks", body, Vector3.zero).transform;
            Prims.Make(PrimitiveType.Sphere, root, c, size * 1.05f, teal, null, "Trunks");
            // waistband, side stripes, drawstring, and a couple of little flowers
            Prims.Make(PrimitiveType.Cylinder, root, c + new Vector3(0, size.y * 0.3f, 0), new Vector3(size.x * 0.97f, 0.02f, size.z * 0.97f), white, null, "Waistband");
            for (int s = -1; s <= 1; s += 2)
            {
                Prims.Make(PrimitiveType.Cube, root, c + new Vector3(s * size.x * 0.525f, -0.01f, 0), new Vector3(0.02f, size.y * 0.7f, 0.06f), white, null, "Stripe");
                Prims.Make(PrimitiveType.Cylinder, root, c + new Vector3(s * 0.04f, size.y * 0.12f, size.z * 0.53f), new Vector3(0.015f, 0.05f, 0.015f), white,
                    Quaternion.Euler(0, 0, s * 15f), "String");
            }
            Vector2[] flowers = { new Vector2(-0.2f, -0.2f), new Vector2(0.22f, 0.1f), new Vector2(0.05f, -0.35f) };
            foreach (var f in flowers)
            {
                // sit each flower right on the curved surface (fractions of the half-size in x and y)
                float rx = f.x, ry = f.y;
                float z = 0.525f * size.z * Mathf.Sqrt(Mathf.Max(0f, 1f - rx * rx / 0.2756f - ry * ry / 0.2756f)) + 0.004f;
                var fp = c + new Vector3(rx * size.x, ry * size.y, z);
                Prims.Make(PrimitiveType.Sphere, root, fp, new Vector3(0.07f, 0.07f, 0.03f), yellow, null, "Flower");
            }
        }

        // =================================================================== feet

        /// <summary>Returns true if a shoe was added (so the default sole should be skipped).</summary>
        public static bool AddFeet(int feet, Transform foot)
        {
            switch (feet)
            {
                case 1: ClownShoe(foot); return true;
                case 2: Sneaker(foot); return true;
                case 3: Boot(foot); return true;
            }
            return false;
        }

        static void ClownShoe(Transform f)
        {
            Prims.Make(PrimitiveType.Sphere, f, new Vector3(0, 0.01f, 0.14f), new Vector3(0.32f, 0.2f, 0.62f), M(new Color(0.9f, 0.1f, 0.12f), 0.8f), null, "Clown");
            Prims.Make(PrimitiveType.Sphere, f, new Vector3(0, 0.1f, 0.08f), Vector3.one * 0.1f, M(new Color(1f, 0.88f, 0.2f), 0.4f), null, "Pom");
        }

        static void Sneaker(Transform f)
        {
            Prims.Make(PrimitiveType.Cube, f, new Vector3(0, -0.04f, 0.03f), new Vector3(0.25f, 0.05f, 0.38f), M(new Color(0.25f, 0.25f, 0.28f), 0.3f), null, "Sole");
            Prims.Make(PrimitiveType.Sphere, f, new Vector3(0, 0.02f, 0.03f), new Vector3(0.24f, 0.15f, 0.36f), M(Color.white, 0.5f), null, "Upper");
            for (int s = -1; s <= 1; s += 2)
                Prims.Make(PrimitiveType.Cube, f, new Vector3(s * 0.115f, 0.01f, 0.0f), new Vector3(0.01f, 0.04f, 0.18f), M(new Color(0.2f, 0.6f, 1f), 0.5f), Quaternion.Euler(-15f, 0, 0), "Swoosh");
            Prims.Make(PrimitiveType.Cube, f, new Vector3(0, 0.085f, 0.08f), new Vector3(0.1f, 0.02f, 0.1f), M(new Color(0.85f, 0.85f, 0.85f), 0.4f), null, "Laces");
        }

        static void Boot(Transform f)
        {
            var olive = M(new Color(0.28f, 0.3f, 0.18f), 0.3f);
            var tan = M(new Color(0.15f, 0.12f, 0.09f), 0.3f);
            Prims.Make(PrimitiveType.Cube, f, new Vector3(0, -0.045f, 0.04f), new Vector3(0.26f, 0.05f, 0.38f), tan, null, "Sole");
            Prims.Make(PrimitiveType.Sphere, f, new Vector3(0, 0.02f, 0.06f), new Vector3(0.25f, 0.15f, 0.34f), olive, null, "Toe");
            Prims.Make(PrimitiveType.Cylinder, f, new Vector3(0, 0.1f, -0.03f), new Vector3(0.22f, 0.09f, 0.22f), olive, null, "Shaft");
            for (int k = 0; k < 3; k++)
                Prims.Make(PrimitiveType.Cube, f, new Vector3(0, 0.07f + k * 0.045f, 0.08f - k * 0.02f), new Vector3(0.12f, 0.012f, 0.02f), tan, null, "Lace");
        }
    }
}
