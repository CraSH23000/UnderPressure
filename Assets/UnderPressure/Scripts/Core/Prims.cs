using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>Builds collider-free primitive meshes for the placeholder art.</summary>
    public static class Prims
    {
        static readonly Dictionary<PrimitiveType, Mesh> meshes = new Dictionary<PrimitiveType, Mesh>();

        public static Mesh GetMesh(PrimitiveType type)
        {
            if (meshes.TryGetValue(type, out var m) && m != null) return m;
            var go = GameObject.CreatePrimitive(type);
            go.SetActive(false);
            m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            meshes[type] = m;
            return m;
        }

        public static GameObject Make(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale,
            Material mat, Quaternion? localRot = null, string name = null)
        {
            var go = new GameObject(name ?? type.ToString());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = GetMesh(type);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return go;
        }

        /// <summary>A cylinder spanning two local points.</summary>
        public static GameObject Segment(Transform parent, Vector3 a, Vector3 b, float radius, Material mat, float overlap = 1.06f)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            var rot = len > 0.0001f ? Quaternion.FromToRotation(Vector3.up, d / len) : Quaternion.identity;
            // Unity's cylinder is 2 units tall and 1 unit wide.
            return Make(PrimitiveType.Cylinder, parent, (a + b) * 0.5f,
                new Vector3(radius * 2f, len * 0.5f * overlap, radius * 2f), mat, rot, "Seg");
        }

        public static GameObject Empty(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go;
        }
    }
}
