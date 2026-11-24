using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>One liquid path through a piece, between two (local) openings.</summary>
    public class PipeChannel
    {
        public Dir a, b;
        public Vector3[] points;      // local path from opening a to opening b
        public Renderer[][] segments; // per path segment, the renderers that change color as it fills
        public float fill;            // 0..1
        public bool fromA = true;     // which end the liquid entered from
        public bool Full => fill >= 0.999f;
    }

    /// <summary>The procedural placeholder geometry for one pipe piece.</summary>
    public class PipeModel
    {
        public Transform root;
        public PipeType type;
        public float topHeight;
        public Renderer plate;
        public Transform baseRoot;                     // plate + bolts: only shown once the piece is installed
        public readonly List<Renderer> flanges = new List<Renderer>();
        public readonly List<PipeChannel> channels = new List<PipeChannel>();
        public readonly List<Transform> staples = new List<Transform>();
        public readonly Dictionary<Dir, Transform> clamps = new Dictionary<Dir, Transform>();
        /// <summary>Each opening's flange and dark mouth disc (local direction), e.g. to blow a cross's bridge away.</summary>
        public readonly Dictionary<Dir, List<GameObject>> openingParts = new Dictionary<Dir, List<GameObject>>();
    }

    public static class PipeBuilder
    {
        public const int Segments = 8;

        public static float TubeRadius(float S) => 0.13f * S;
        public static float TubeHeight(float S) => 0.2f * S;
        public static float BridgeBump(float S) => 0.22f * S;
        /// <summary>How far the lower channel of a cross dips under the bridge (into a trench in the plate).</summary>
        public static float CrossDip(float S) => 0.065f * S;

        public static float TopHeight(PipeType t, float S) =>
            TubeHeight(S) + TubeRadius(S) + (t == PipeType.Cross ? BridgeBump(S) : t == PipeType.Filter ? 0.16f * S : 0f);

        /// <summary>The filter's canister in the middle of the T (the arms stop at its wall).</summary>
        public static float CanisterRadius(float S) => 0.17f * S;

        // an 'arm' channel runs from opening a into the middle of the piece (the filter's T)
        struct ChannelDef { public Dir a, b; public float bump; public bool arm; }

        static List<ChannelDef> Defs(PipeType t, float S)
        {
            var list = new List<ChannelDef>();
            switch (t)
            {
                case PipeType.Straight: list.Add(new ChannelDef { a = Dir.N, b = Dir.S }); break;
                case PipeType.Corner: list.Add(new ChannelDef { a = Dir.N, b = Dir.E }); break;
                case PipeType.Cross:
                    list.Add(new ChannelDef { a = Dir.N, b = Dir.S, bump = -CrossDip(S) });   // dips under...
                    list.Add(new ChannelDef { a = Dir.E, b = Dir.W, bump = BridgeBump(S) }); // ...so the bridge can arch gently over
                    break;
                case PipeType.Filter:
                    // a T: three arms into a canister. Liquid fills the arm it came in by, then splits out the other two
                    foreach (var d in new[] { Dir.N, Dir.E, Dir.W }) list.Add(new ChannelDef { a = d, b = d, arm = true });
                    break;
            }
            return list;
        }

        /// <summary>Local-space centreline from the middle of edge a to the middle of edge b.</summary>
        public static Vector3[] Path(Dir a, Dir b, float S, float h, float bump, int n)
        {
            var pts = new Vector3[n + 1];
            Vector3 pa = DirUtil.ToVector3(a) * S * 0.5f;
            Vector3 pb = DirUtil.ToVector3(b) * S * 0.5f;
            bool straight = DirUtil.Opposite(a) == b;
            Vector3 corner = pa + pb;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                Vector3 p = straight ? Vector3.Lerp(pa, pb, t) : corner + Vector3.Slerp(pa - corner, pb - corner, t);
                p.y = h + bump * Profile(t);
                pts[i] = p;
            }
            return pts;
        }

        /// <summary>0 at the ends, 1 in the middle. Flat for the first/last 12% so the tube leaves each flange dead level.</summary>
        public static float Profile(float t)
        {
            const float e = 0.12f;
            float u = Mathf.Clamp01((t - e) / (1f - 2f * e));
            float s = Mathf.Sin(Mathf.PI * u);
            return s * s * (3f - 2f * s);
        }

        /// <summary>Direction of travel along a channel path at point i (ends point straight in/out of the edges).</summary>
        static Vector3 Tangent(Vector3[] pts, int i, Dir a, Dir b)
        {
            if (i == 0) return -DirUtil.ToVector3(a);
            if (i == pts.Length - 1) return DirUtil.ToVector3(b);
            return (pts[i + 1] - pts[i - 1]).normalized;
        }

        static readonly Dictionary<string, Mesh> tubeCache = new Dictionary<string, Mesh>();

        /// <summary>
        /// One open tube section between two rings, each perpendicular to the path there. Neighbouring sections
        /// share their rings exactly, so bends are seamless (no joint spheres bulging out).
        /// </summary>
        static Mesh TubeMesh(string key, Vector3 a, Vector3 b, Vector3 ta, Vector3 tb, float r)
        {
            if (tubeCache.TryGetValue(key, out var cached) && cached != null) return cached;
            const int sides = 18;
            var verts = new Vector3[(sides + 1) * 2];
            var norms = new Vector3[verts.Length];
            var tris = new int[sides * 6];
            for (int ring = 0; ring < 2; ring++)
            {
                Vector3 c = ring == 0 ? a : b, t = ring == 0 ? ta : tb;
                Vector3 side = Vector3.Cross(Vector3.up, t);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
                side.Normalize();
                Vector3 up = Vector3.Cross(t, side).normalized;
                for (int k = 0; k <= sides; k++)
                {
                    float ang = k / (float)sides * Mathf.PI * 2f;
                    Vector3 n = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                    verts[ring * (sides + 1) + k] = c + n * r;
                    norms[ring * (sides + 1) + k] = n;
                }
            }
            for (int k = 0; k < sides; k++)
            {
                int i0 = k, i1 = k + 1, j0 = sides + 1 + k, j1 = sides + 2 + k;
                int o = k * 6;
                // clockwise seen from outside (Unity's front face)
                tris[o] = i0; tris[o + 1] = i1; tris[o + 2] = j0;
                tris[o + 3] = i1; tris[o + 4] = j1; tris[o + 5] = j0;
            }
            var m = new Mesh { name = "Tube" };
            m.vertices = verts;
            m.normals = norms;
            m.triangles = tris;
            m.RecalculateBounds();
            tubeCache[key] = m;
            return m;
        }

        public static Vector3 Eval(Vector3[] pts, float t)
        {
            t = Mathf.Clamp01(t) * (pts.Length - 1);
            int i = Mathf.Min(Mathf.FloorToInt(t), pts.Length - 2);
            return Vector3.Lerp(pts[i], pts[i + 1], t - i);
        }

        public static PipeModel Build(PipeType type, Transform parent, float S, Material overrideMat = null)
        {
            var m = new PipeModel { type = type, topHeight = TopHeight(type, S) };
            m.root = Prims.Empty("Model", parent, Vector3.zero).transform;
            float r = TubeRadius(S), h = TubeHeight(S);

            var body = overrideMat != null ? overrideMat : Mats.FillMat(0);
            var flange = overrideMat != null ? overrideMat : Mats.Lit(Mats.PipeFlange, 0.55f, 0.4f);
            var clamp = overrideMat != null ? overrideMat : Mats.Lit(Mats.Clamp, 0.85f, 0.9f, Mats.Clamp * 0.15f);
            var staple = overrideMat != null ? overrideMat : Mats.Lit(Mats.Staple, 0.45f, 0.7f);
            var plate = overrideMat != null ? overrideMat : Mats.Lit(Mats.Plate, 0.3f);

            // Base plate + corner bolts (a cross gets a trench down the middle for the dipping channel)
            m.baseRoot = Prims.Empty("Base", m.root, Vector3.zero).transform;
            if (type == PipeType.Cross)
            {
                float trench = r * 1.3f, half = 0.45f * S;
                float w = half - trench;
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    var half1 = Prims.Make(PrimitiveType.Cube, m.baseRoot, new Vector3(sx * (trench + w * 0.5f), 0.02f, 0), new Vector3(w, 0.04f, 0.9f * S), plate, null, "Plate");
                    if (m.plate == null) m.plate = half1.GetComponent<Renderer>();
                    else half1.GetComponent<Renderer>().sharedMaterial = plate;
                }
                Prims.Make(PrimitiveType.Cube, m.baseRoot, new Vector3(0, 0.004f, 0), new Vector3(trench * 2f, 0.008f, 0.9f * S),
                    overrideMat != null ? overrideMat : Mats.Lit(Theme.Ink, 0.3f), null, "Trench");
            }
            else
                m.plate = Prims.Make(PrimitiveType.Cube, m.baseRoot, new Vector3(0, 0.02f, 0), new Vector3(0.9f * S, 0.04f, 0.9f * S), plate, null, "Plate")
                    .GetComponent<Renderer>();
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                Prims.Make(PrimitiveType.Cylinder, m.baseRoot, new Vector3(sx * 0.38f * S, 0.05f, sz * 0.38f * S),
                    new Vector3(0.09f, 0.015f, 0.09f), staple, null, "Bolt");

            var openings = new HashSet<Dir>();
            int ci = 0;
            foreach (var def in Defs(type, S))
            {
                var ch = new PipeChannel { a = def.a, b = def.b };
                ch.points = def.arm ? ArmPath(def.a, S, h, Segments) : Path(def.a, def.b, S, h, def.bump, Segments);
                ch.segments = new Renderer[Segments][];
                for (int i = 0; i < Segments; i++)
                {
                    // the tube starts/ends a little inside each flange, so its open end never shows
                    Vector3 ta = def.arm ? -DirUtil.ToVector3(def.a) : Tangent(ch.points, i, def.a, def.b);
                    Vector3 tb = def.arm ? -DirUtil.ToVector3(def.a) : Tangent(ch.points, i + 1, def.a, def.b);
                    Vector3 a = ch.points[i], b = ch.points[i + 1];
                    if (i == 0) a += ta * 0.03f * S;
                    if (i == Segments - 1 && !def.arm) b -= tb * 0.03f * S;
                    var go = new GameObject("Tube");
                    go.transform.SetParent(m.root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = TubeMesh($"{type}|{ci}|{i}|{S:0.###}", a, b, ta, tb, r);
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = body;
                    ch.segments[i] = new Renderer[] { mr };
                }
                ci++;
                m.channels.Add(ch);
                openings.Add(def.a);
                openings.Add(def.b);
            }

            // Flanges (always) and gold clamps (only when latched to the network) at every opening.
            foreach (var d in openings)
            {
                Vector3 dv = DirUtil.ToVector3(d);
                Vector3 edge = dv * S * 0.5f + Vector3.up * h;
                var flangeGo = Prims.Segment(m.root, edge - dv * 0.07f * S, edge, r * 1.25f, flange, 1f);
                m.flanges.Add(flangeGo.GetComponent<Renderer>());
                // the pipe's opening: a dark disc just proud of the flange face (it tucks inside a neighbour's flange)
                var mouth = Prims.Segment(m.root, edge - dv * 0.004f, edge + dv * 0.012f, r * 0.82f, overrideMat != null ? overrideMat : Mats.Lit(Theme.Ink, 0.4f), 1f);
                m.openingParts[d] = new List<GameObject> { flangeGo.gameObject, mouth.gameObject };

                var clampRoot = Prims.Empty("Clamp_" + d, m.root, edge - dv * 0.085f * S).transform;
                clampRoot.localRotation = Quaternion.LookRotation(dv);
                Prims.Make(PrimitiveType.Cylinder, clampRoot, Vector3.zero, new Vector3(r * 2.9f, 0.06f * S, r * 2.9f), clamp,
                    Quaternion.Euler(90, 0, 0), "Ring");
                Prims.Make(PrimitiveType.Cube, clampRoot, new Vector3(0, r * 1.5f, 0), new Vector3(0.12f, 0.1f, 0.1f), clamp, null, "Latch");
                clampRoot.gameObject.SetActive(false);
                m.clamps[d] = clampRoot;
            }

            if (type == PipeType.Filter) FilterCanister(m.root, S, h, r, flange, clamp, overrideMat);

            // Ground brackets ("staples") that tie an unconnected piece to its tile.
            var ch0 = m.channels[0];
            float[] stapleTs = type == PipeType.Corner || type == PipeType.Filter ? new[] { 0.5f } : type == PipeType.Cross ? new[] { 0.18f, 0.82f } : new[] { 0.3f, 0.7f };
            foreach (float t in stapleTs)
            {
                Vector3 p = Eval(ch0.points, t);
                Vector3 tangent = Eval(ch0.points, t + 0.02f) - Eval(ch0.points, t - 0.02f);
                tangent.y = 0;
                m.staples.Add(MakeStaple(m.root, p, tangent.normalized, r, h, staple));
            }
            return m;
        }

        /// <summary>A straight run from the middle of edge 'a' in to the canister wall.</summary>
        static Vector3[] ArmPath(Dir a, float S, float h, int n)
        {
            var pts = new Vector3[n + 1];
            Vector3 pa = DirUtil.ToVector3(a) * S * 0.5f;
            Vector3 pc = DirUtil.ToVector3(a) * CanisterRadius(S) * 0.6f;
            for (int i = 0; i <= n; i++)
            {
                var p = Vector3.Lerp(pa, pc, i / (float)n);
                p.y = h;
                pts[i] = p;
            }
            return pts;
        }

        /// <summary>The filter's canister: a glass cylinder with a striped cartridge inside, metal caps and a handle.</summary>
        static void FilterCanister(Transform root, float S, float h, float r, Material flange, Material clamp, Material overrideMat)
        {
            float cr = CanisterRadius(S), top = h + r + 0.16f * S, bottom = 0.05f;
            var cap = overrideMat != null ? overrideMat : Mats.Lit(new Color(0.75f, 0.78f, 0.82f), 0.85f, 0.9f);
            var glass = overrideMat != null ? overrideMat : Mats.Transparent(new Color(0.8f, 0.92f, 1f, 0.3f));
            var cartridge = overrideMat != null ? overrideMat : Mats.Lit(new Color(0.95f, 0.95f, 0.92f), 0.3f);
            var pleat = overrideMat != null ? overrideMat : Mats.Lit(new Color(0.3f, 0.6f, 0.95f), 0.4f);
            float mid = (top + bottom) * 0.5f, len = top - bottom;
            Prims.Make(PrimitiveType.Cylinder, root, new Vector3(0, mid, 0), new Vector3(cr * 2f, len * 0.5f, cr * 2f), glass, null, "CanisterGlass");
            Prims.Make(PrimitiveType.Cylinder, root, new Vector3(0, mid, 0), new Vector3(cr * 1.2f, len * 0.42f, cr * 1.2f), cartridge, null, "Cartridge");
            for (int i = 0; i < 4; i++) // pleat bands
                Prims.Make(PrimitiveType.Cylinder, root, new Vector3(0, bottom + len * (0.2f + i * 0.2f), 0), new Vector3(cr * 1.25f, 0.012f * S, cr * 1.25f), pleat, null, "Pleat");
            Prims.Make(PrimitiveType.Cylinder, root, new Vector3(0, top, 0), new Vector3(cr * 2.2f, 0.035f * S, cr * 2.2f), cap, null, "CapTop");
            Prims.Make(PrimitiveType.Cylinder, root, new Vector3(0, bottom + 0.02f, 0), new Vector3(cr * 2.2f, 0.03f * S, cr * 2.2f), cap, null, "CapBottom");
            Prims.Make(PrimitiveType.Cube, root, new Vector3(0, top + 0.05f * S, 0), new Vector3(cr * 1.4f, 0.05f * S, 0.05f * S), clamp, null, "Handle");
        }

        static Transform MakeStaple(Transform parent, Vector3 p, Vector3 tangent, float r, float h, Material mat)
        {
            var root = Prims.Empty("Staple", parent, new Vector3(p.x, 0f, p.z)).transform;
            root.localRotation = Quaternion.LookRotation(tangent);
            float height = h + r + 0.035f;
            float off = r + 0.04f;
            for (int s = -1; s <= 1; s += 2)
            {
                Prims.Make(PrimitiveType.Cube, root, new Vector3(s * off, height * 0.5f, 0), new Vector3(0.05f, height, 0.08f), mat);
                // feet sit ON the base plate (top at 0.04), not flush with it, so they don't z-fight
                Prims.Make(PrimitiveType.Cube, root, new Vector3(s * (off + 0.04f), 0.055f, 0), new Vector3(0.14f, 0.03f, 0.16f), mat);
            }
            Prims.Make(PrimitiveType.Cube, root, new Vector3(0, height, 0), new Vector3(off * 2f + 0.05f, 0.05f, 0.08f), mat);
            root.gameObject.SetActive(false);
            return root;
        }
    }
}
