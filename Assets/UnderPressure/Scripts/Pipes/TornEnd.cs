using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// A burst pipe's torn end, like a cartoon exploding cigar: the tube splits into ragged strips that peel back
    /// and curl outward. It pops open with an overshoot. The outside of each strip is the tube (colored by the
    /// liquid that filled it), the inside is dark.
    /// </summary>
    public class TornEnd : MonoBehaviour
    {
        struct Petal { public float angle, width, length, curl; }

        const int Rows = 7;          // along each strip
        const int Cols = 3;          // across each strip
        const float OpenTime = 0.22f;

        Vector3 fwd, side, up;
        float radius, age;
        Petal[] petals;
        Mesh mesh;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> norms = new List<Vector3>();
        readonly List<int> outerTris = new List<int>();
        readonly List<int> innerTris = new List<int>();

        /// <summary>'pos' and 'forward' are local to 'parent'; forward points out of the intact pipe into the gap.</summary>
        public static TornEnd Spawn(Transform parent, Vector3 pos, Vector3 forward, float radius, Material outer, Material inner, int seed)
        {
            var go = new GameObject("TornEnd");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var t = go.AddComponent<TornEnd>();
            t.Init(forward, radius, outer, inner, seed);
            return t;
        }

        void Init(Vector3 forward, float r, Material outer, Material inner, int seed)
        {
            radius = r;
            fwd = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            side = Vector3.Cross(Vector3.up, fwd);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side.Normalize();
            up = Vector3.Cross(fwd, side).normalized;

            var rng = new System.Random(seed ^ Random.Range(0, int.MaxValue));
            float R() => (float)rng.NextDouble();
            int count = 6 + rng.Next(2);
            float spacing = Mathf.PI * 2f / count, offset = R() * spacing;
            petals = new Petal[count];
            for (int i = 0; i < count; i++)
                petals[i] = new Petal
                {
                    angle = offset + i * spacing + (R() - 0.5f) * spacing * 0.25f,
                    width = spacing * (0.75f + R() * 0.2f),
                    length = r * (1.3f + R() * 1.1f),
                    curl = Mathf.Deg2Rad * (110f + R() * 110f),
                };

            mesh = new Mesh { name = "TornEnd" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { outer, inner };
            Build(0f);
        }

        void Update()
        {
            age += Time.deltaTime;
            float k = age / OpenTime;
            if (k >= 1f) { Build(1f); enabled = false; return; }
            Build(BackOut(k));
        }

        static float BackOut(float k)
        {
            const float c1 = 1.9f, c3 = c1 + 1f;
            float x = k - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        void OnDestroy() { if (mesh != null) Destroy(mesh); }

        void Build(float open)
        {
            verts.Clear(); norms.Clear(); outerTris.Clear(); innerTris.Clear();
            var outward = new Vector3[Rows + 1, Cols];
            foreach (var p in petals)
            {
                int baseIndex = verts.Count;
                for (int i = 0; i <= Rows; i++)
                {
                    float s = i / (float)Rows;
                    float taper = Mathf.Lerp(1f, 0.3f, s);
                    for (int c = 0; c < Cols; c++)
                    {
                        float u = c / (float)(Cols - 1) * 2f - 1f;                 // -1..1 across the strip
                        float theta = p.angle + u * p.width * 0.5f * taper;
                        Vector3 n = side * Mathf.Cos(theta) + up * Mathf.Sin(theta);
                        float len = p.length * open * (c == 1 ? 1f : 0.82f);    // the middle runs longest: a ragged point
                        float curl = p.curl * open;
                        float phi = s * curl;
                        Vector3 pos = n * radius;
                        if (curl < 1e-3f) pos += fwd * (s * len);
                        else
                        {
                            float rho = len / curl;                              // peel along a circle bending outward
                            pos += rho * (Mathf.Sin(phi) * fwd + (1f - Mathf.Cos(phi)) * n);
                        }
                        Vector3 nOut = Mathf.Cos(phi) * n - Mathf.Sin(phi) * fwd;
                        outward[i, c] = nOut;
                        verts.Add(pos); norms.Add(nOut);     // outer face
                        verts.Add(pos); norms.Add(-nOut);    // inner face
                    }
                }
                for (int i = 0; i < Rows; i++)
                for (int c = 0; c < Cols - 1; c++)
                {
                    int v00 = baseIndex + (i * Cols + c) * 2, v01 = baseIndex + (i * Cols + c + 1) * 2;
                    int v10 = baseIndex + ((i + 1) * Cols + c) * 2, v11 = baseIndex + ((i + 1) * Cols + c + 1) * 2;
                    AddQuad(v00, v10, v01, v11, outward[i, c] + outward[i + 1, c + 1]);
                }
            }
            mesh.Clear();
            mesh.subMeshCount = 2;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(outerTris, 0);
            mesh.SetTriangles(innerTris, 1);
            mesh.RecalculateBounds();
        }

        /// <summary>Two triangles each side; winding picked so the outer copy faces 'outward' (Unity's front face
        /// normal is cross(b - a, c - a)). Inner vertices are each outer index + 1.</summary>
        void AddQuad(int a, int b, int c, int d, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            bool flip = Vector3.Dot(n, outward) < 0f;
            AddTri(a, b, c, flip);
            AddTri(b, d, c, flip);
        }

        void AddTri(int a, int b, int c, bool flip)
        {
            if (flip) { int t = b; b = c; c = t; }
            outerTris.Add(a); outerTris.Add(b); outerTris.Add(c);
            innerTris.Add(a + 1); innerTris.Add(c + 1); innerTris.Add(b + 1);
        }
    }
}
