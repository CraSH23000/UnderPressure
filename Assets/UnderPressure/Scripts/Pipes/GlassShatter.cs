using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>Broken glass: the jagged stump a shattered cylinder leaves behind, and flying shards.</summary>
    public static class GlassShatter
    {
        /// <summary>
        /// A ring of glass from 'bottom' up to a jagged, spiky rim (local to 'parent'), visible from both sides.
        /// 'minH'..'maxH' are the rim's lowest and highest points above the bottom.
        /// </summary>
        public static GameObject Stump(Transform parent, Vector3 center, float radius, float bottom, float minH, float maxH, Material mat)
        {
            const int sides = 18;
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            // alternate low and high rim points so it reads as broken teeth, with some randomness
            var rim = new float[sides + 1];
            for (int i = 0; i < sides; i++)
            {
                float k = (i & 1) == 0 ? Random.Range(0f, 0.35f) : Random.Range(0.55f, 1f);
                rim[i] = Mathf.Lerp(minH, maxH, k);
            }
            rim[sides] = rim[0];
            for (int face = 0; face < 2; face++)
            {
                int start = verts.Count;
                float nSign = face == 0 ? 1f : -1f;
                for (int i = 0; i <= sides; i++)
                {
                    float a = i / (float)sides * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    verts.Add(center + dir * radius + Vector3.up * bottom);
                    verts.Add(center + dir * radius + Vector3.up * (bottom + rim[i]));
                    norms.Add(dir * nSign);
                    norms.Add(dir * nSign);
                }
                for (int i = 0; i < sides; i++)
                {
                    int b0 = start + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                    // outer face: clockwise seen from outside; inner face: the reverse
                    if (face == 0) { tris.Add(b0); tris.Add(t0); tris.Add(b1); tris.Add(t0); tris.Add(t1); tris.Add(b1); }
                    else { tris.Add(b0); tris.Add(b1); tris.Add(t0); tris.Add(t0); tris.Add(b1); tris.Add(t1); }
                }
            }
            var mesh = new Mesh { name = "GlassStump" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("GlassStump");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static Mesh shardMesh;

        /// <summary>A thin double-sided sliver of glass.</summary>
        static Mesh ShardMesh()
        {
            if (shardMesh != null) return shardMesh;
            var v = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, -0.15f), new Vector3(0.1f, 0f, 1f) };
            shardMesh = new Mesh { name = "Shard" };
            shardMesh.vertices = new[] { v[0], v[1], v[2], v[0], v[1], v[2] };
            shardMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.down, Vector3.down, Vector3.down };
            shardMesh.triangles = new[] { 0, 2, 1, 3, 4, 5 };
            shardMesh.RecalculateBounds();
            return shardMesh;
        }

        /// <summary>Glass slivers flying out of a cylinder of 'radius' at world 'center', between 'y0' and 'y1'.</summary>
        public static void Shards(Vector3 center, float radius, float y0, float y1, int count, Material mat)
        {
            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var go = new GameObject("Shard");
                if (Game.LevelRoot != null) go.transform.SetParent(Game.LevelRoot, false);
                go.transform.position = center + dir * radius + Vector3.up * Random.Range(y0, y1);
                go.transform.rotation = Random.rotation;
                go.transform.localScale = Vector3.one * Random.Range(0.05f, 0.11f);
                go.AddComponent<MeshFilter>().sharedMesh = ShardMesh();
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                Tossed.Launch(go, dir * Random.Range(1.5f, 4f) + Vector3.up * Random.Range(2.5f, 5f), Random.insideUnitSphere * 900f, 0.01f);
            }
        }
    }
}
