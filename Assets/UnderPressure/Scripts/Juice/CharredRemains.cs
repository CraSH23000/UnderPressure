using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// The teleporter's leftovers, in three beats:
    ///   1. ELECTROCUTED (~0.55 s): classic cartoon x-ray. The critter's silhouette strobes between its own color
    ///      and a dark x-ray blue, with a glowing white skeleton inside, jittering, sparks flying.
    ///   2. CHARRED (~0.4 s): the bones go black, a puff of smoke, it just... stands there.
    ///   3. DUST (~1.2 s): it crumbles and blows away on the wind.
    /// The skeleton is a round skull with horn nubs and an antenna stalk, so you can tell whose it was.
    /// </summary>
    public class CharredRemains : MonoBehaviour
    {
        const float Shock = 0.55f, Char = 0.4f, Dust = 1.2f;

        float age;
        Vector3 wind, basePos;
        Color playerColor;
        Transform body, bones;
        Renderer[] bodyRenderers;
        readonly List<Renderer> boneRenderers = new List<Renderer>();
        Material boneGlow, boneChar, xray, flesh, ember;
        float strobe, dustTimer;
        bool charred;
        readonly System.Random rng = new System.Random();

        public static void Spawn(Vector3 pos, Color playerColor)
        {
            var go = new GameObject("CharredRemains");
            go.transform.SetParent(Game.LevelRoot, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(Vector3.back); // face the camera
            go.transform.localScale = Vector3.one * 1.3f;                  // a touch bigger so it reads
            var r = go.AddComponent<CharredRemains>();
            r.playerColor = playerColor;
            r.Build();
        }

        void Build()
        {
            basePos = transform.position;
            boneGlow = Mats.Lit(Color.white, 0.5f, 0f, new Color(2.4f, 2.6f, 3f));
            boneChar = Mats.Lit(new Color(0.08f, 0.07f, 0.06f), 0.1f);
            ember = new Material(Mats.Lit(new Color(0.15f, 0.08f, 0.05f), 0.2f, 0f, new Color(1.6f, 0.4f, 0.05f)));
            xray = Mats.Transparent(new Color(0.05f, 0.15f, 0.45f, 0.55f));
            flesh = Mats.Transparent(new Color(playerColor.r, playerColor.g, playerColor.b, 0.85f));
            var hole = Mats.Lit(new Color(0.01f, 0.01f, 0.02f), 0.05f);

            // silhouette of the critter body (strobes)
            body = Prims.Empty("Silhouette", transform, Vector3.zero).transform;
            Prims.Make(PrimitiveType.Sphere, body, new Vector3(0, 0.52f, 0), new Vector3(0.95f, 0.9f, 0.88f), xray, null, "Body");
            Prims.Make(PrimitiveType.Sphere, body, new Vector3(-0.08f, 0.98f, 0f), Vector3.one * 0.22f, xray, null, "Tuft");
            Prims.Make(PrimitiveType.Sphere, body, new Vector3(0.07f, 1.0f, 0.03f), Vector3.one * 0.19f, xray, null, "Tuft");
            bodyRenderers = body.GetComponentsInChildren<Renderer>();

            // skeleton
            bones = Prims.Empty("Bones", transform, Vector3.zero).transform;
            Bone(PrimitiveType.Sphere, new Vector3(0, 0.8f, 0), new Vector3(0.5f, 0.44f, 0.46f), null, "Skull");
            for (int s = -1; s <= 1; s += 2)
            {
                Prims.Make(PrimitiveType.Sphere, bones, new Vector3(s * 0.12f, 0.83f, 0.2f), new Vector3(0.15f, 0.17f, 0.1f), hole, null, "EyeSocket");
                Bone(PrimitiveType.Capsule, new Vector3(s * 0.2f, 1.03f, 0f), new Vector3(0.08f, 0.08f, 0.08f), Quaternion.Euler(0, 0, -s * 28f), "HornNub");
                for (int k = 0; k < 3; k++)
                    Bone(PrimitiveType.Capsule, new Vector3(s * 0.12f, 0.46f - k * 0.1f, 0.05f), new Vector3(0.05f, 0.13f, 0.05f), Quaternion.Euler(0, 0, s * 70f), "Rib");
                Bone(PrimitiveType.Capsule, new Vector3(s * 0.33f, 0.42f, 0.05f), new Vector3(0.06f, 0.14f, 0.06f), Quaternion.Euler(0, 0, s * 30f), "Arm");
                Bone(PrimitiveType.Capsule, new Vector3(s * 0.14f, 0.1f, 0f), new Vector3(0.07f, 0.12f, 0.07f), null, "Leg");
            }
            Prims.Make(PrimitiveType.Cube, bones, new Vector3(0, 0.67f, 0.22f), new Vector3(0.18f, 0.03f, 0.05f), hole, null, "Grin");
            Bone(PrimitiveType.Cylinder, new Vector3(0, 0.42f, -0.02f), new Vector3(0.06f, 0.22f, 0.06f), null, "Spine");
            Bone(PrimitiveType.Cylinder, new Vector3(0, 1.14f, -0.05f), new Vector3(0.025f, 0.12f, 0.025f), null, "AntennaStalk");
            Prims.Make(PrimitiveType.Sphere, bones, new Vector3(0, 1.28f, -0.05f), Vector3.one * 0.08f, ember, null, "Ember");

            wind = new Vector3(Random.Range(1.8f, 2.6f), 0.8f, Random.Range(-0.4f, 0.4f));
            Fx.Sparks(transform.position + Vector3.up * 0.7f, 30, 6f);
        }

        void Bone(PrimitiveType t, Vector3 p, Vector3 s, Quaternion? r, string name) =>
            boneRenderers.Add(Prims.Make(t, bones, p, s, boneGlow, r, name).GetComponent<Renderer>());

        void SetBones(Material m) { foreach (var r in boneRenderers) r.sharedMaterial = m; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            age += dt;

            if (age < Shock)
            {
                // cartoon electrocution: strobe flesh <-> x-ray, jitter, sparks
                strobe -= dt;
                if (strobe <= 0f)
                {
                    strobe = 0.055f;
                    bool xr = !bones.gameObject.activeSelf;
                    bones.gameObject.SetActive(xr);
                    foreach (var r in bodyRenderers) r.sharedMaterial = xr ? xray : flesh;
                    if (rng.NextDouble() < 0.5) Fx.Sparks(transform.position + Vector3.up * (0.3f + (float)rng.NextDouble() * 0.9f), 5, 4f);
                }
                transform.position = basePos + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.12f, ((float)rng.NextDouble() - 0.5f) * 0.06f, 0f);
                return;
            }

            if (!charred)
            {
                charred = true;
                transform.position = basePos;
                body.gameObject.SetActive(false);
                bones.gameObject.SetActive(true);
                SetBones(boneChar);
                Fx.Burst(transform.position + Vector3.up * 0.6f, new Color(0.04f, 0.04f, 0.05f), new Color(0.2f, 0.19f, 0.19f), 22, 1.8f, 0.6f, 1.3f, -0.25f);
            }

            ember.SetColor("_EmissionColor", new Color(1.6f, 0.4f, 0.05f) * Mathf.Clamp01(1.4f - age) * (0.7f + 0.3f * Mathf.Sin(age * 25f)));
            if (age < Shock + Char) return; // it just stands there for a beat

            // crumble to dust on the wind
            float t = age - Shock - Char;
            transform.position += wind * dt * Mathf.Clamp01(t * 2f);
            transform.rotation *= Quaternion.Euler(8f * dt, 20f * dt, -35f * dt);
            float s = Mathf.Clamp01(1f - t / Dust);
            transform.localScale = new Vector3(1.3f * s, 1.3f * Mathf.Sqrt(s), 1.3f * s);
            dustTimer -= dt;
            if (dustTimer <= 0f)
            {
                dustTimer = 0.05f;
                Fx.Burst(transform.position + Vector3.up * (0.4f + 0.6f * s), new Color(0.12f, 0.11f, 0.1f), new Color(0.3f, 0.28f, 0.26f), 4, 1.2f, 0.12f, 0.8f, -0.1f);
            }
            if (t > Dust) Destroy(gameObject);
        }
    }
}
