using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// The liquid source. Occupies one board cell and outputs in one direction.
    /// Looks like a glass light bulb on a screw base: a slime lurks down in the base until a subject comes close
    /// (or the release is near), floats up, dyes the tank its liquid's color, then glares at the subjects while
    /// they work. All of that is cosmetic; the flow only uses the nozzle path, the clamp and the valve.
    /// </summary>
    public class Spout : MonoBehaviour
    {
        public Vector2Int Cell { get; private set; }
        public SpoutHologram Hologram { get; private set; }
        public Dir Out { get; private set; }
        /// <summary>0..1, drives shaking / steam / bubbles as the countdown runs out.</summary>
        public float Pressure { get; set; }

        // tank layout (local heights). The screw base stays inside the tile, since pipes run next to it at its
        // height; above that a short neck opens into a tall glass globe.
        const float PlinthTop = 0.56f, NeckTop = 0.68f;
        const float GlobeV = 1.0f;       // the globe's vertical radius (horizontal: rGlobe)
        const float FillFrac = 0.7f;    // liquid level: this far from the globe's middle up to its top
        const float SlimeHidden = 0.0f, SlimeFloat = 1.18f;
        const float Overhang = 0.06f;    // how far the globe pokes past the tile edge
        const float ArriveRange = 2.2f;  // tiles: a subject this close wakes the slime
        const int PuffCount = 6, BubbleCount = 12;
        static readonly Color ClearWater = new Color(0.78f, 0.92f, 1f, 0.16f);
        const float DyedAlpha = 0.5f;

        float S, d, rBase, rGlobe, globeY, apex, fillTop;
        Transform body, clamp;
        Transform slime, face;
        readonly Transform[] eyes = new Transform[2];
        readonly float[] eyeMirror = new float[2];
        Vector3 eyeBase;
        Material eyeMat, slimeMat, tankMat, surfaceMat;
        Transform surface;
        Springy spring, jelly;
        Renderer[][] nozzle;
        Vector3[] path;
        float steamTimer;
        bool clampShown;

        // dye puffs and bubbles in the tank
        readonly Transform[] puffs = new Transform[PuffCount];
        readonly Material[] puffMats = new Material[PuffCount];
        readonly float[] puffAge = new float[PuffCount];
        readonly Color[] puffCol = new Color[PuffCount];
        readonly Transform[] bubbles = new Transform[BubbleCount];
        readonly float[] bubbleSpeed = new float[BubbleCount];

        public static Spout Create(Transform parent, Vector2Int cell, Dir output, Vector3 worldPos, float S)
        {
            var go = new GameObject("Spout");
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            var s = go.AddComponent<Spout>();
            s.Cell = cell; s.Out = output; s.S = S;
            s.Build();
            return s;
        }

        void Build()
        {
            var white = Mats.Lit(Color.Lerp(new Color(0.96f, 0.97f, 0.98f), Theme.Wall, 0.5f), 0.75f);
            var ink = Mats.Lit(Theme.Ink, 0.6f, 0.4f);
            var steel = Mats.Lit(Color.Lerp(Mats.Plate, Theme.Ink, 0.35f), 0.7f, 0.6f);
            float h = PipeBuilder.TubeHeight(S), r = PipeBuilder.TubeRadius(S);

            Prims.Make(PrimitiveType.Cube, transform, new Vector3(0, 0.02f, 0), new Vector3(0.9f * S, 0.04f, 0.9f * S), Mats.Lit(Mats.Plate, 0.3f));

            body = Prims.Empty("Body", transform, Vector3.zero).transform;
            spring = body.gameObject.AddComponent<Springy>();
            spring.SetBase(1f, true);
            d = 0.72f * S;
            rBase = d * 0.54f;
            rGlobe = S * 0.5f + Overhang;
            // the globe's centre sits so its lower curve narrows to the base's radius right at the neck
            globeY = NeckTop + GlobeV * Mathf.Sqrt(1f - (rBase / rGlobe) * (rBase / rGlobe));
            apex = globeY + GlobeV;
            fillTop = globeY + GlobeV * FillFrac;
            Vector3 fwd = DirUtil.ToVector3(Out);

            // the screw base: the outlet comes out of it, and the slime hides in it until it's time.
            // (the base stops short of the lip ring so no two caps share a plane)
            const float lip = 0.06f;
            Prims.Make(PrimitiveType.Cylinder, body, new Vector3(0, (PlinthTop - lip) * 0.5f, 0), new Vector3(d * 1.08f, (PlinthTop - lip) * 0.5f, d * 1.08f), white);
            Prims.Make(PrimitiveType.Cylinder, body, new Vector3(0, 0.06f, 0), new Vector3(d * 1.12f, 0.06f, d * 1.12f), ink);
            Prims.Make(PrimitiveType.Cylinder, body, new Vector3(0, PlinthTop - lip * 0.5f, 0), new Vector3(d * 1.12f, lip * 0.5f, d * 1.12f), ink);
            foreach (float ty in new[] { 0.2f, 0.33f }) // thread rings
                Prims.Make(PrimitiveType.Cylinder, body, new Vector3(0, ty, 0), new Vector3(d * 1.1f, 0.014f, d * 1.1f), ink, null, "Thread");
            // the tank floor: a dark hatch the slime rises out of
            Prims.Make(PrimitiveType.Cylinder, body, new Vector3(0, PlinthTop + 0.004f, 0), new Vector3(d * 0.85f, 0.004f, d * 0.85f),
                Mats.Lit(Color.Lerp(Theme.Ink, Color.black, 0.5f), 0.8f, 0.3f), null, "Hatch");

            BuildSlime();

            // the tank: liquid, then bubbles and dye puffs, then the glass over everything (fixed draw order).
            // The liquid fills most of the globe, leaving an air pocket at the top with a surface sheen on it.
            tankMat = Mats.Transparent(ClearWater);
            tankMat.renderQueue = 3000;
            const float inset = 0.025f;
            var liquidWall = BulbProfile(rBase - inset, rGlobe - inset, GlobeV - inset, PlinthTop + 0.014f, fillTop);
            float rFill = liquidWall[liquidWall.Count - 1].x;
            var liquidTop = new List<Vector2> { new Vector2(rFill, fillTop), new Vector2(0f, fillTop) };
            LatheObject("TankLiquid", Lathe("SpoutLiquid", liquidWall, liquidTop), tankMat);

            surfaceMat = Mats.Transparent(new Color(1f, 1f, 1f, 0.3f));
            surfaceMat.renderQueue = 3001;
            float rSurf = rFill - 0.05f;
            surface = Prims.Make(PrimitiveType.Cylinder, body, new Vector3(0, fillTop + 0.02f, 0), new Vector3(rSurf * 2f, 0.003f, rSurf * 2f), surfaceMat, null, "Surface").transform;

            var bubbleMat = Mats.Transparent(new Color(1f, 1f, 1f, 0.35f));
            bubbleMat.renderQueue = 3001;
            for (int i = 0; i < BubbleCount; i++)
            {
                bubbles[i] = Prims.Make(PrimitiveType.Sphere, body, RandomBubbleSpot(Random.Range(PlinthTop, fillTop)), Vector3.one * Random.Range(0.025f, 0.06f), bubbleMat, null, "Bubble").transform;
                bubbleSpeed[i] = Random.Range(0.15f, 0.4f);
            }
            for (int i = 0; i < PuffCount; i++)
            {
                puffMats[i] = Mats.Transparent(Color.clear);
                puffMats[i].renderQueue = 3001;
                puffs[i] = Prims.Make(PrimitiveType.Sphere, body, Vector3.zero, Vector3.one, puffMats[i], null, "DyePuff").transform;
                puffs[i].gameObject.SetActive(false);
                puffAge[i] = -1f;
            }

            // the glass: one seamless light bulb, neck into globe, closed at the top
            var glass = Mats.Transparent(new Color(0.85f, 0.95f, 1f, 0.12f));
            Mats.SetEmission(glass, new Color(0.04f, 0.05f, 0.06f));
            glass.renderQueue = 3002;
            if (glass.HasProperty("_Cull")) glass.SetFloat("_Cull", 0f); // both faces: the far wall shows through
            LatheObject("Glass", Lathe("SpoutGlass", BulbProfile(rBase, rGlobe, GlobeV, PlinthTop + 0.002f, apex)), glass);

            // bulb highlights on the camera side: a long streak and a dot above it, lying on the glass
            var shine = Mats.Transparent(new Color(1f, 1f, 1f, 0.4f));
            shine.renderQueue = 3003;
            var az = Quaternion.Euler(0f, 38f, 0f);
            foreach (var (deg, size) in new[] { (8f, new Vector3(0.07f, 0.34f, 0.012f)), (40f, new Vector3(0.06f, 0.08f, 0.012f)) })
            {
                float ph = deg * Mathf.Deg2Rad;
                var outward = az * new Vector3(0f, Mathf.Sin(ph) / GlobeV, -Mathf.Cos(ph) / rGlobe).normalized;
                var at = az * new Vector3(0f, 0f, -(rGlobe + 0.008f) * Mathf.Cos(ph)) + Vector3.up * (globeY + GlobeV * Mathf.Sin(ph));
                Prims.Make(PrimitiveType.Sphere, body, at, size, shine, Quaternion.LookRotation(-outward, Vector3.up), "Shine");
            }

            // nozzle path: centre -> tile edge (what the flow follows; hidden inside the base and the sleeve)
            const int n = 4;
            path = new Vector3[n + 1];
            for (int i = 0; i <= n; i++) path[i] = fwd * (S * 0.5f * i / n) + Vector3.up * h;
            nozzle = new Renderer[n][];
            for (int i = 0; i < n; i++)
                nozzle[i] = new[] { Prims.Segment(transform, path[i], i == n - 1 ? Vector3.MoveTowards(path[i + 1], path[i], 0.03f * S) : path[i + 1], r, Mats.FillMat(0), i == n - 1 ? 1f : 1.06f).GetComponent<Renderer>() };
            Vector3 edge = path[n];

            // outlet sleeve: a black coupling out to the tile edge; the next pipe plugs into its mouth
            float sleeveR = r * 1.38f;
            Prims.Segment(transform, fwd * rBase * 0.8f + Vector3.up * h, edge, sleeveR, ink, 1f);
            Prims.Segment(transform, fwd * rBase * 0.95f + Vector3.up * h, fwd * (rBase * 0.95f + 0.05f * S) + Vector3.up * h, sleeveR * 1.16f, steel, 1f); // collar at the base
            Prims.Segment(transform, edge - fwd * 0.002f, edge + fwd * 0.004f, r * 1.12f, Mats.Lit(Color.black, 0.2f), 1f); // dark opening

            clamp = Prims.Empty("Clamp", transform, edge - fwd * 0.05f * S).transform;
            clamp.localRotation = Quaternion.LookRotation(fwd);
            Prims.Make(PrimitiveType.Cylinder, clamp, Vector3.zero, new Vector3(sleeveR * 2.6f, 0.06f * S, sleeveR * 2.6f),
                Mats.Lit(Mats.Clamp, 0.85f, 0.9f, Mats.Clamp * 0.15f), Quaternion.Euler(90, 0, 0));
            clamp.gameObject.SetActive(false);

            Hologram = SpoutHologram.Create(this);

            var col = gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(0.9f * S, apex, 0.9f * S);
            col.center = new Vector3(0, apex * 0.5f, 0);
            gameObject.layer = Layers.Default; // blocks walking AND climbing
        }

        /// <summary>The specimen: a big jelly blob in the chamber's liquid color with two glowing, scowling eyes.</summary>
        void BuildSlime()
        {
            slime = Prims.Empty("Slime", body, new Vector3(0, SlimeHidden, 0)).transform;
            var jellyRoot = Prims.Empty("Jelly", slime, Vector3.zero).transform;
            jelly = jellyRoot.gameObject.AddComponent<Springy>();
            jelly.SetBase(1f, true);

            slimeColor = Mats.Liquid;
            slimeId = Mats.LiquidId;
            slimeMat = Mats.LiquidMat(Mats.LiquidId, Mats.Liquid, Mats.LiquidGlow, 0.3f, 0.92f, 0f, true);
            Prims.Make(PrimitiveType.Sphere, jellyRoot, Vector3.zero, new Vector3(d * 0.92f, d * 0.66f, d * 0.92f), slimeMat, null, "Body");
            Prims.Make(PrimitiveType.Sphere, jellyRoot, new Vector3(d * 0.18f, d * 0.24f, -d * 0.08f), Vector3.one * d * 0.22f, slimeMat, null, "Lump");
            Prims.Make(PrimitiveType.Sphere, jellyRoot, new Vector3(-d * 0.3f, -d * 0.17f, d * 0.12f), Vector3.one * d * 0.17f, slimeMat, null, "Lump");
            Prims.Make(PrimitiveType.Sphere, jellyRoot, new Vector3(-d * 0.1f, d * 0.26f, -d * 0.18f), Vector3.one * d * 0.13f, slimeMat, null, "Lump");

            // the face turns round the blob (yaw only: the body is round about Y, so the eyes stay on it) to look
            // at things. Dot eyes like the ghost's, but red, glowing, rounded underneath and cut flat across the
            // top, slanting down to the inside: a scowl. They're decals wrapped onto the body's surface, rebuilt
            // each frame so blinking, flaring and glancing up/down all stay on the skin.
            face = Prims.Empty("Face", jellyRoot, Vector3.zero).transform;
            eyeMat = new Material(Mats.Lit(new Color(1f, 0.15f, 0.1f), 0.9f, 0f, new Color(2f, 0.1f, 0.05f)));
            if (eyeMat.HasProperty("_Cull")) eyeMat.SetFloat("_Cull", 0f); // one eye is mirrored
            eyeBase = new Vector3(d * 0.17f, d * 0.2f, 0f);
            bodyRadii = new Vector2(d * 0.46f, d * 0.33f);
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Eye");
                go.transform.SetParent(face, false);
                eyeMeshes[i] = new Mesh { name = "AngryEye" };
                eyeMeshes[i].MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = eyeMeshes[i];
                go.AddComponent<MeshRenderer>().sharedMaterial = eyeMat;
                eyes[i] = go.transform;
                eyeMirror[i] = i == 0 ? 1f : -1f; // the outline's slant drops toward +x: the inside, for the left eye
                ShapeEye(i, 1f, 1f, 0f, true);
            }
        }

        /// <summary>The eye outline (unit size), as a closed loop: a rounded U underneath, then the flat top slanting
        /// down toward +X, split into short steps so no edge is long enough to cut under the curved body.</summary>
        static readonly Vector2[] EyeOutline = BuildEyeOutline();
        static Vector2[] BuildEyeOutline()
        {
            var v = new List<Vector2>();
            const int u = 16, top = 8;
            for (int i = 0; i <= u; i++) // the U: from the left corner, under, round to the right corner
            {
                float a = Mathf.PI + i / (float)u * Mathf.PI;
                v.Add(new Vector2(0.5f * Mathf.Cos(a), 0.6f * Mathf.Sin(a)));
            }
            var inner = new Vector2(0.5f, 0.08f);   // inner top corner: low
            var outer = new Vector2(-0.5f, 0.45f);  // outer top corner: high
            for (int i = 0; i <= top; i++) v.Add(Vector2.Lerp(inner, outer, i / (float)top));
            return v.ToArray();
        }
        static readonly Vector2 EyeCentre = new Vector2(0f, -0.08f);
        const int EyeRings = 4; // concentric rings from the centre out to the outline

        readonly Mesh[] eyeMeshes = new Mesh[2];
        readonly Vector3[] eyeVerts = new Vector3[1 + EyeRings * EyeOutline.Length];
        readonly Vector3[] eyeNorms = new Vector3[1 + EyeRings * EyeOutline.Length];
        Vector2 bodyRadii; // the body ellipsoid's horizontal and vertical radii

        /// <summary>Lays eye i onto the body: a centre point plus rings out to the outline, every point placed on
        /// the face and pushed out onto the ellipsoid's surface (plus a hair, so it never z-fights the skin).</summary>
        void ShapeEye(int i, float grow, float lid, float lift, bool first = false)
        {
            float a = bodyRadii.x, b = bodyRadii.y;
            float cx = (i == 0 ? -1f : 1f) * d * 0.17f, cy = d * 0.08f + lift;
            int n = EyeOutline.Length;
            for (int k = 0; k < eyeVerts.Length; k++)
            {
                Vector2 o;
                if (k == 0) o = EyeCentre;
                else
                {
                    int ring = (k - 1) / n + 1, j = (k - 1) % n;
                    o = Vector2.Lerp(EyeCentre, EyeOutline[j], ring / (float)EyeRings);
                }
                float x = cx + o.x * eyeBase.x * grow * eyeMirror[i];
                float y = cy + o.y * eyeBase.y * grow * lid;
                float q = 1f - (x / a) * (x / a) - (y / b) * (y / b);
                float z = a * Mathf.Sqrt(Mathf.Max(q, 0.02f));
                var nrm = new Vector3(x / (a * a), y / (b * b), z / (a * a)).normalized;
                eyeVerts[k] = new Vector3(x, y, z) + nrm * 0.015f;
                eyeNorms[k] = nrm;
            }
            var m = eyeMeshes[i];
            m.vertices = eyeVerts;
            m.normals = eyeNorms;
            if (first)
            {
                var tris = new List<int>();
                for (int j = 0; j < n; j++) // centre fan into the first ring
                {
                    int jn = (j + 1) % n;
                    tris.Add(0); tris.Add(1 + jn); tris.Add(1 + j);
                }
                for (int ring = 1; ring < EyeRings; ring++) // quads between rings
                    for (int j = 0; j < n; j++)
                    {
                        int jn = (j + 1) % n;
                        int a0 = 1 + (ring - 1) * n + j, a1 = 1 + (ring - 1) * n + jn;
                        int b0 = 1 + ring * n + j, b1 = 1 + ring * n + jn;
                        tris.Add(a0); tris.Add(b1); tris.Add(b0);
                        tris.Add(a0); tris.Add(a1); tris.Add(b1);
                    }
                m.SetTriangles(tris, 0);
            }
            m.RecalculateBounds();
        }

        // ---------------------------------------------------------------- lathe meshes

        /// <summary>
        /// The light bulb's outline, bottom-up: a short straight neck at radius r0, then an elliptical globe
        /// (horizontal radius rG, vertical rV, centred at globeY) whose lower curve meets the neck, cut off at yTop.
        /// </summary>
        List<Vector2> BulbProfile(float r0, float rG, float rV, float yBottom, float yTop)
        {
            float phiB = -Mathf.Acos(Mathf.Clamp01(r0 / rG)); // where the globe is as narrow as the neck
            float phiT = Mathf.Asin(Mathf.Clamp((yTop - globeY) / rV, -1f, 1f));
            var pts = new List<Vector2> { new Vector2(r0, yBottom) };
            float neckY = globeY + rV * Mathf.Sin(phiB);
            if (neckY > yBottom + 0.005f) pts.Add(new Vector2(r0, neckY));
            const int k = 28;
            for (int i = 1; i <= k; i++)
            {
                float ph = Mathf.Lerp(phiB, phiT, i / (float)k);
                pts.Add(new Vector2(rG * Mathf.Cos(ph), globeY + rV * Mathf.Sin(ph)));
            }
            return pts;
        }

        /// <summary>
        /// Spins (radius, height) profiles round the Y axis into one mesh. Each profile gets smooth normals of its own;
        /// start a new one for a hard edge (repeating the corner point). Profiles run bottom-up / outside-in.
        /// </summary>
        static Mesh Lathe(string name, params List<Vector2>[] profiles)
        {
            const int seg = 48;
            int cols = seg + 1;
            var v = new List<Vector3>();
            var nrm = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            foreach (var prof in profiles)
            {
                int start = v.Count / cols, rows = prof.Count;
                for (int j = 0; j < rows; j++)
                {
                    // outward normal from the profile's tangent (dr, dy) -> (dy, -dr)
                    Vector2 tan = prof[Mathf.Min(j + 1, rows - 1)] - prof[Mathf.Max(j - 1, 0)];
                    Vector2 n2 = new Vector2(tan.y, -tan.x).normalized;
                    for (int i = 0; i < cols; i++)
                    {
                        float phi = i / (float)seg * Mathf.PI * 2f, cp = Mathf.Cos(phi), sp = Mathf.Sin(phi);
                        v.Add(new Vector3(prof[j].x * cp, prof[j].y, prof[j].x * sp));
                        nrm.Add(new Vector3(n2.x * cp, n2.y, n2.x * sp));
                        uv.Add(new Vector2(i / (float)seg, j / (float)Mathf.Max(1, rows - 1)));
                    }
                }
                for (int j = 0; j < rows - 1; j++)
                    for (int i = 0; i < seg; i++)
                    {
                        int a = (start + j) * cols + i, b = a + 1, c = a + cols, e = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(e);
                        tris.Add(a); tris.Add(e); tris.Add(b);
                    }
            }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        void LatheObject(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(body, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        Vector3 RandomBubbleSpot(float y)
        {
            var c = Random.insideUnitCircle * rBase * 0.85f;
            return new Vector3(c.x, y, c.y);
        }

        // ---------------------------------------------------------------- flow hooks

        /// <summary>Someone turned the valve (delta = +1 faster, -1 slower, 0 just grabbed it).
        /// The bubbles rise with the flow speed, so they show the change.</summary>
        public void TurnValve(int delta)
        {
            spring.Squash(delta == 0 ? 0.1f : 0.22f);
            jelly.Squash(0.15f);
            eyePunch = Mathf.Max(eyePunch, 0.25f); // who's touching my valve?
            if (delta > 0) bubbleBoost = Mathf.Max(bubbleBoost, 0.6f);
        }

        public void SetFill(float t)
        {
            int n = nozzle.Length;
            for (int j = 0; j < n; j++)
            {
                float f = Mathf.Clamp01(t * n - j);
                var mat = Mats.FillMat(Mathf.RoundToInt(f * (Mats.FillLevels - 1)));
                nozzle[j][0].sharedMaterial = mat;
            }
        }

        public Vector3 PointAt(float t) => transform.TransformPoint(PipeBuilder.Eval(path, t));
        public Vector3 CenterTop => transform.position + Vector3.up * (apex + 0.02f); // the hologram projects from the bulb's tip

        public void SetClamp(bool on, bool animate)
        {
            if (on == clampShown) return;
            clampShown = on;
            if (!animate) { clamp.gameObject.SetActive(on); return; }
            if (on)
            {
                clamp.gameObject.SetActive(true);
                Juice.ScaleTo(clamp, Vector3.one * 0.2f, Vector3.one, 0.28f, Juice.OutElastic);
            }
            else Juice.ScaleTo(clamp, Vector3.one, Vector3.one * 1.5f, 0.12f, Juice.InCubic,
                () => { clamp.gameObject.SetActive(false); clamp.localScale = Vector3.one; });
        }

        public void Burst()
        {
            spring.Squash(-0.6f);
            jelly.Squash(-0.5f);
            eyePunch = 0.5f;
            Fx.Burst(CenterTop, Color.white, new Color(0.85f, 0.95f, 1f), 26, 5f, 0.35f, 0.7f, -0.3f);
        }

        public void Tick()
        {
            spring.Squash(0.18f);
            jelly.Squash(0.12f);
        }

        void Update()
        {
            Mats.TickPatterns(transform.position);
            float p = Pressure;
            float jitter = p * p * 0.035f;
            body.localPosition = new Vector3(Random.Range(-jitter, jitter), 0f, Random.Range(-jitter, jitter));

            if (p > 0.45f)
            {
                steamTimer -= Time.deltaTime;
                if (steamTimer <= 0f)
                {
                    steamTimer = Mathf.Lerp(0.5f, 0.08f, p);
                    var side = Random.insideUnitCircle.normalized * 0.4f;
                    Fx.Puff(CenterTop + new Vector3(side.x, -0.1f, side.y), Color.white, 0.25f + p * 0.2f);
                }
            }

            UpdateSlime(Time.deltaTime, p);
            UpdateTank(Time.deltaTime);
        }

        // ---------------------------------------------------------------- the specimen

        bool arrived;
        float eyeLift, rise, eyePunch, glanceTimer, glanceRoll, blinkTimer = 3f, blinkLeft, happyTimer, bubbleBoost;
        Color slimeColor, tankColor = ClearWater;
        int slimeId;
        const float BlinkTime = 0.14f;

        /// <summary>It stays down in the base until a subject comes close, the release is under ten seconds
        /// away, or the liquid is already out.</summary>
        bool ShouldArrive(GameManager gm, float p)
        {
            if (p > 0.001f) return true;
            if (gm != null && gm.Current != GameManager.State.Countdown && gm.Current != GameManager.State.Lobby) return true;
            float range = ArriveRange * S;
            foreach (var pl in Game.Players)
            {
                if (pl == null) continue;
                var dd = pl.transform.position - transform.position;
                dd.y = 0f;
                if (dd.sqrMagnitude < range * range) return true;
            }
            return false;
        }

        void Arrive()
        {
            arrived = true;
            jelly.Squash(-0.4f);
            eyePunch = 0.4f;
            glanceTimer = 0f; glanceRoll = 0.5f; // first thing it does: glare at whoever's nearest
            bubbleBoost = 1.6f;
            for (int i = 0; i < BubbleCount; i++) bubbles[i].localPosition = RandomBubbleSpot(PlinthTop + Random.Range(0.02f, 0.2f));
            Sfx.Play(Sfx.I != null ? Sfx.I.bloop : null, 0.7f, 0.6f, 0.05f);
        }

        void UpdateSlime(float dt, float p)
        {
            var gm = Game.Manager;
            bool panic = gm != null && gm.Panic;
            bool lost = gm != null && gm.Current == GameManager.State.Lost;
            bool won = gm != null && gm.Current == GameManager.State.Won;

            if (Game.Players.Count == 0) arrived = false; // everyone left: it sinks back down
            else if (!arrived && ShouldArrive(gm, p)) Arrive();

            // new chamber liquid: the slime changes color (and the tank re-dyes to match)
            if (Mats.Liquid != slimeColor || Mats.LiquidId != slimeId)
            {
                slimeColor = Mats.Liquid;
                if (Mats.LiquidId != slimeId)
                {
                    // a different liquid may swap between plain and patterned: give the blob a fresh material
                    slimeId = Mats.LiquidId;
                    var old = slimeMat;
                    slimeMat = Mats.LiquidMat(Mats.LiquidId, Mats.Liquid, Mats.LiquidGlow, 0.3f, 0.92f, 0f, true);
                    foreach (var r in slime.GetComponentsInChildren<Renderer>(true))
                        if (r.sharedMaterial == old) r.sharedMaterial = slimeMat;
                    if (old != null) Destroy(old);
                }
                Mats.SetBase(slimeMat, slimeColor);
                Mats.SetEmission(slimeMat, Mats.LiquidGlow * 0.3f);
            }

            // float up out of the base (overshooting a touch); sulk lower after a spill
            rise = Mathf.MoveTowards(rise, arrived ? 1f : 0f, dt / (arrived ? 1.8f : 4f));
            float e = arrived ? Juice.OutBack(rise) : Mathf.SmoothStep(0f, 1f, rise);
            float e01 = Mathf.Clamp01(e);
            float y = Mathf.LerpUnclamped(SlimeHidden, lost ? (SlimeHidden + SlimeFloat) * 0.5f + 0.2f : SlimeFloat, e);
            y += Mathf.Sin(Time.time * (1.6f + p * 6f)) * 0.035f * S * e01;
            var want = new Vector3(0f, y, panic ? -d * 0.22f : 0f); // in a panic it presses up against the front glass
            // suspended, not parked: a slow drift and a lazy sway, only once it's up in the liquid
            float tt = Time.time;
            want += new Vector3(Mathf.Sin(tt * 0.53f), 0f, Mathf.Sin(tt * 0.41f + 1f)) * d * 0.06f * e01;
            slime.localPosition = Vector3.Lerp(slime.localPosition, want, 1f - Mathf.Exp(-6f * dt));
            slime.localRotation = Quaternion.Euler(Mathf.Sin(tt * 0.7f) * 6f * e01, 0f, Mathf.Sin(tt * 0.9f + 2f) * 6f * e01);

            if (won)
            {
                happyTimer -= dt;
                if (happyTimer <= 0f) { happyTimer = 0.55f; jelly.Squash(0.25f); }
            }

            // where to look
            Vector3 target = LookTarget(dt, panic);
            // turn the face to it (in the blob's own frame, so the sway carries it), and slide the eyes up or down
            // the body for the pitch
            Vector3 dir = face.parent.InverseTransformDirection(target - face.position);
            float flat = new Vector2(dir.x, dir.z).magnitude;
            float k7 = 1f - Mathf.Exp(-7f * dt); // quick, like a saccade
            if (flat > 0.05f)
                face.localRotation = Quaternion.Slerp(face.localRotation, Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f), k7);
            float pitch = Mathf.Atan2(dir.y, Mathf.Max(flat, 0.01f)) * Mathf.Rad2Deg;
            eyeLift = Mathf.Lerp(eyeLift, Mathf.Clamp(pitch / 50f, -1f, 0.6f) * d * 0.07f, k7);

            // lids: random blinks, slits while it's down in the base
            blinkTimer -= dt;
            if (blinkTimer <= 0f) { blinkLeft = BlinkTime; blinkTimer = Random.Range(2.5f, 6f); }
            float lid = 1f;
            if (blinkLeft > 0f)
            {
                blinkLeft -= dt;
                lid = Mathf.Max(0.08f, Mathf.Abs(blinkLeft / BlinkTime * 2f - 1f));
            }
            lid *= Mathf.Lerp(0.25f, 1f, e01);
            eyePunch = Mathf.MoveTowards(eyePunch, 0f, dt * 1.5f);
            float grow = 1f + eyePunch + (panic ? 0.2f : 0f);
            for (int i = 0; i < 2; i++) ShapeEye(i, grow, lid, eyeLift);

            // the eyes burn brighter as the pressure builds
            float glow = 0.7f + p * 2.5f + Mathf.Sin(Time.time * (4f + p * 20f)) * 0.3f * p + eyePunch * 2f + (panic ? 1.5f : 0f);
            Mats.SetEmission(eyeMat, new Color(2f, 0.1f, 0.05f) * glow * Mathf.Lerp(0.3f, 1f, e01));
        }

        Vector3 LookTarget(float dt, bool panic)
        {
            if (!arrived) return face.position + new Vector3(0f, -1.5f, -3f); // dozing: stares at the floor

            glanceTimer -= dt;
            if (glanceTimer <= 0f) { glanceTimer = Random.Range(1.2f, 3.2f); glanceRoll = Random.value; }

            // keeps an eye on the liquid while it flows (always, once it's about to leak)
            var gm = Game.Manager;
            bool flowing = gm != null && gm.Current == GameManager.State.Flowing;
            Vector3? front = Game.Flow != null ? Game.Flow.FrontPosition : null;
            if (front.HasValue && flowing && (panic || glanceRoll < 0.4f)) return front.Value;

            // otherwise a subject: usually the closest, now and then someone else
            var players = Game.Players;
            if (players.Count == 0) return face.position + Vector3.back;
            PlayerController pick = null;
            if (glanceRoll > 0.8f && players.Count > 1)
                pick = players[Mathf.FloorToInt(glanceRoll * 1000f) % players.Count];
            if (pick == null)
            {
                float best = float.MaxValue;
                foreach (var pl in players)
                {
                    if (pl == null) continue;
                    float dd = (pl.transform.position - transform.position).sqrMagnitude;
                    if (dd < best) { best = dd; pick = pl; }
                }
            }
            return pick != null ? pick.transform.position + Vector3.up * 0.6f : face.position + Vector3.back;
        }

        // ---------------------------------------------------------------- the tank

        float puffTimer;

        void UpdateTank(float dt)
        {
            // the slime dyes the water once it's up; the tank clears when nobody's around
            bool dyeing = rise > 0.6f && arrived;
            var goal = dyeing ? new Color(slimeColor.r, slimeColor.g, slimeColor.b, DyedAlpha) : ClearWater;
            tankColor = Color.Lerp(tankColor, goal, 1f - Mathf.Exp(-0.8f * dt));
            Mats.SetBase(tankMat, tankColor);
            float dyed = Mathf.InverseLerp(ClearWater.a, DyedAlpha, tankColor.a);
            Mats.SetEmission(tankMat, Mats.LiquidGlow * 0.12f * dyed);
            // the surface: a lighter sheen that rocks gently (more under pressure)
            var sc = Color.Lerp(tankColor, Color.white, 0.5f); sc.a = 0.3f;
            Mats.SetBase(surfaceMat, sc);
            Mats.SetEmission(surfaceMat, Mats.LiquidGlow * 0.15f * dyed);
            float rock = 1.2f + Pressure * 1.2f, tr = Time.time;
            surface.localRotation = Quaternion.Euler(Mathf.Sin(tr * 1.3f) * rock, 0f, Mathf.Cos(tr * 1.1f) * rock);

            // ink clouds billow off the slime while the color is still spreading
            float gap = Mathf.Abs(tankColor.r - goal.r) + Mathf.Abs(tankColor.g - goal.g) + Mathf.Abs(tankColor.b - goal.b) + Mathf.Abs(tankColor.a - goal.a);
            puffTimer -= dt;
            if (dyeing && gap > 0.08f && puffTimer <= 0f)
            {
                puffTimer = 0.16f;
                SpawnPuff();
            }

            for (int i = 0; i < PuffCount; i++)
            {
                if (puffAge[i] < 0f) continue;
                puffAge[i] += dt;
                float t = puffAge[i] / 1.4f;
                if (t >= 1f) { puffAge[i] = -1f; puffs[i].gameObject.SetActive(false); continue; }
                puffs[i].localScale = Vector3.one * d * Mathf.Lerp(0.18f, 0.9f, Juice.OutCubic(t));
                puffs[i].localPosition += Vector3.up * 0.08f * dt;
                var c = puffCol[i]; c.a = 0.55f * (1f - t);
                Mats.SetBase(puffMats[i], c);
            }

            // bubbles: they rise with the flow speed and the pressure; a rush follows the slime up when it arrives
            bubbleBoost = Mathf.Max(0f, bubbleBoost - dt);
            float flowMul = Game.Flow != null ? Game.Flow.SpeedMultiplier : 1f;
            float speedMul = (1f + bubbleBoost * 2f) * Mathf.Lerp(1f, flowMul, 0.6f) * (1f + Pressure);
            for (int i = 0; i < BubbleCount; i++)
            {
                var b = bubbles[i];
                var lp = b.localPosition + Vector3.up * bubbleSpeed[i] * speedMul * dt;
                lp.x += Mathf.Sin(Time.time * 3f + i) * 0.02f * dt;
                if (lp.y > fillTop - 0.03f) lp = RandomBubbleSpot(PlinthTop + 0.03f);
                b.localPosition = lp;
            }
        }

        void SpawnPuff()
        {
            for (int i = 0; i < PuffCount; i++)
            {
                if (puffAge[i] >= 0f) continue;
                puffAge[i] = 0f;
                puffCol[i] = slimeColor;
                var c = Random.insideUnitCircle * d * 0.2f;
                float y = Mathf.Clamp(slime.localPosition.y + Random.Range(-0.15f, 0.2f), PlinthTop + 0.2f, fillTop - 0.3f);
                puffs[i].localPosition = new Vector3(c.x, y, c.y);
                puffs[i].localScale = Vector3.one * d * 0.18f;
                puffs[i].gameObject.SetActive(true);
                return;
            }
        }
    }
}
