using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// The critter: an original round little monster — one squishy body, enormous eyes that follow what you're
    /// about to grab, stubby horns, a single fang, and a glowing antenna (in the player's color) that lags
    /// behind with secondary motion. Procedurally animated so it can later be swapped for a rigged model;
    /// the controller only calls Tick / Squash / Hop / HeadShake / WorkPulse / LookAt / Cheer / Sad.
    /// </summary>
    public class PlayerRig : MonoBehaviour
    {
        Transform lift, pitch, body, face, handL, handR, footL, footR, antenna, tail;
        Transform[] eyeballs = new Transform[0], pupils = new Transform[0];
        float pupilZ = 0.08f, lookScale = 1f;
        Springy spring;
        float phase, blinkTimer = 2f, blinkT, shakeT, workPulse, hopT = -1f;
        float curPitch, curTilt;
        float cheerT, sadT;
        Vector3 lookTarget, lastPos, lastVel, antennaAngle, antennaVel;
        bool hasLast;

        // ---- model-based critters (FBX from the Character Factory) ----
        bool modelMode;
        bool blob;          // legless goo (the slime): squashes instead of stepping, cosmetics float inside, leaves a trail
        float lastSinP;
        float k = 1f, pivotY = 0.25f;          // size relative to the original 1.2 m critter; pitch pivot height
        Vector3 restHandL, restHandR;
        float holdLocalY, holdLocalZ;           // where hands go when carrying (pitch space): chest height, in front of the belly
        Transform armL, armR, legL, legR;
        Vector3 armRestDirL, armRestDirR;
        Quaternion armRestRotL, armRestRotR, legRestL, legRestR, headRest = Quaternion.identity;
        Vector3[] eyeRest = new Vector3[0];

        // ---- the ghost (mouse player): a wisp, its tail puffs trail and sway ----
        bool ghostMode;
        Transform[] wisps;
        Vector3[] wispRest;

        // ---- sipping: a prop comes out into the right hand, goes up to the mouth, glug glug, then is put away ----
        Transform mouth, sipGrip;
        float sipT = -1f, sipGlugT, sipScale = 1f, sipHeadUp, sipLean;
        const float SipDur = 1.9f;
        /// <summary>True while the critter is drinking.</summary>
        public bool Sipping => sipT >= 0f;

        /// <summary>Height above the feet where carried pieces float (the controller's hold point).</summary>
        public float HoldHeight { get; private set; } = 0.62f;
        /// <summary>How far in front of the critter a carried piece floats.</summary>
        public float HoldReach { get; private set; } = 0.58f;
        /// <summary>Body width relative to the original critter (sizes the floor ring and collider).</summary>
        public float Footprint { get; private set; } = 1f;
        public float Height { get; private set; } = 1.2f;
        /// <summary>The body pivot: it rises with jumps and tips forward when crawling, so things worn on the back
        /// (the backpack) hang off this. Origin at the feet.</summary>
        public Transform BodyPivot => body;

        static readonly string[] SizeSuffix = { "S", "M", "L" };

        /// <summary>The FBX model for this look, if the species has one (loaded from Resources/Characters).</summary>
        static readonly string[] ModelNames = { "Yeti", "Skeleton", "Chihuahua", "Sharc", "Slime" }; // by species index (CritterLook.Species)

        static GameObject ModelFor(CritterLook look)
        {
            if (look.species < 0 || look.species >= ModelNames.Length) return null;
            string file = ModelNames[look.species] + "_" + SizeSuffix[Mathf.Clamp(look.size, 0, 2)];
            var go = Resources.Load<GameObject>("Characters/" + file);
            if (go == null) Debug.LogWarning("[UnderPressure] Missing model Resources/Characters/" + file + ".fbx, using the old placeholder critter.");
            return go;
        }

        /// <summary>Build (or rebuild) the critter from a look: species, body color and outfit.</summary>
        public void Build(CritterLook look)
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            var oldTrail = GetComponent<SlimeTrail>();
            if (oldTrail != null) Destroy(oldTrail);
            hasLast = false;
            modelMode = false;
            blob = false;
            k = 1f; pivotY = 0.25f;
            HoldHeight = 0.62f; HoldReach = 0.58f; Footprint = 1f; Height = 1.2f;
            antenna = tail = null;
            armL = armR = legL = legR = null;
            mouth = sipGrip = null; sipT = -1f;
            ghostMode = false; wisps = null;

            var model = ModelFor(look);
            if (model != null) { BuildModel(model, look); return; }

            Color color = look.BodyColor;
            var fur = Mats.Lit(color, 0.35f);
            var furDark = Mats.Lit(Color.Lerp(color, Color.black, 0.3f), 0.3f);
            var belly = Mats.Lit(Color.Lerp(color, Color.white, 0.55f), 0.3f);
            var bulb = Mats.Lit(Color.Lerp(color, Color.white, 0.3f), 0.8f, 0f, color * 2.2f);

            lift = Prims.Empty("Lift", transform, Vector3.zero).transform;
            pitch = Prims.Empty("Pitch", lift, new Vector3(0, 0.25f, 0)).transform;
            body = Prims.Empty("Body", pitch, new Vector3(0, -0.25f, 0)).transform;
            spring = body.gameObject.AddComponent<Springy>();
            spring.stiffness = 300f; spring.damping = 11f;
            spring.SetBase(1f, true);

            float hatY;
            Vector3 shellCenter, shellSize, hipCenter, hipSize;
            if (look.species == 3) BuildSharc(fur, furDark, belly, color, out hatY, out shellCenter, out shellSize, out hipCenter, out hipSize);
            else BuildBlorb(fur, furDark, belly, bulb, color, out hatY, out shellCenter, out shellSize, out hipCenter, out hipSize); // fallback if a model is missing

            handL = Prims.Make(PrimitiveType.Sphere, pitch, Vector3.zero, Vector3.one * 0.17f, furDark, null, "HandL").transform;
            handR = Prims.Make(PrimitiveType.Sphere, pitch, Vector3.zero, Vector3.one * 0.17f, furDark, null, "HandR").transform;
            // feet are pivots, so shoes can replace the default soles
            footL = Prims.Empty("FootL", lift, Vector3.zero).transform;
            footR = Prims.Empty("FootR", lift, Vector3.zero).transform;
            for (int i = 0; i < 2; i++)
            {
                var f = i == 0 ? footL : footR;
                if (!Accessories.AddFeet(look.feet, f))
                    Prims.Make(PrimitiveType.Sphere, f, Vector3.zero, new Vector3(0.22f, 0.12f, 0.28f), furDark, null, "Sole");
            }

            var hatAnchor = Prims.Empty("HatAnchor", body, new Vector3(0, hatY, 0)).transform;
            Accessories.AddHat(look.hat, hatAnchor);
            Accessories.AddLegs(look.legs, body, hipCenter, hipSize);
            Accessories.AddTorso(look.torso, body, shellCenter, shellSize);
            Accessories.AddArms(look.arms, handL, handR);
            // the procedural SHARC comes in sizes by plain scaling
            if (look.species == 3)
            {
                float sc = look.size == 0 ? 0.8f : look.size == 2 ? 1.4f : 1f;
                lift.localScale = Vector3.one * sc;
                HoldHeight = 0.62f * sc; HoldReach = 0.58f * Mathf.Max(1f, sc); Footprint = sc; Height = 1.2f * sc;
            }
        }

        /// <summary>
        /// The ghost: a see-through wisp in the player's color with a glowing heart, deadpan dot eyes, a little "o"
        /// mouth, floating mitten hands and a tail of shrinking puffs that trails and sways as it drifts.
        /// </summary>
        public void BuildGhost(Color color)
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            hasLast = false;
            modelMode = false;
            blob = false;
            ghostMode = true;
            k = 1f; pivotY = 0.25f;
            HoldHeight = 0.5f; HoldReach = 0.6f; Footprint = 0.85f; Height = 1.05f;
            antenna = tail = null;
            armL = armR = legL = legR = null;
            mouth = sipGrip = null; sipT = -1f;

            Color pale = Color.Lerp(color, Color.white, 0.45f);
            var mist = Mats.Transparent(new Color(pale.r, pale.g, pale.b, 0.5f));
            var wispMat = Mats.Transparent(new Color(pale.r, pale.g, pale.b, 0.32f));
            var heart = Mats.Lit(Color.Lerp(color, Color.white, 0.6f), 0.8f, 0f, color * 1.8f);
            var ink = Mats.Lit(new Color(0.04f, 0.04f, 0.07f), 0.95f);

            lift = Prims.Empty("Lift", transform, Vector3.zero).transform;
            pitch = Prims.Empty("Pitch", lift, new Vector3(0, 0.25f, 0)).transform;
            body = Prims.Empty("Body", pitch, new Vector3(0, -0.25f, 0)).transform;
            spring = body.gameObject.AddComponent<Springy>();
            spring.stiffness = 220f; spring.damping = 8f; // a bit wobblier than a critter
            spring.SetBase(1f, true);

            Prims.Make(PrimitiveType.Sphere, body, new Vector3(0, 0.64f, 0), new Vector3(0.8f, 0.8f, 0.76f), mist, null, "Head");
            Prims.Make(PrimitiveType.Sphere, body, new Vector3(0, 0.6f, -0.02f), new Vector3(0.36f, 0.38f, 0.34f), heart, null, "Heart");

            // the tail: puffs getting smaller, trailing down and behind
            wisps = new Transform[4];
            wispRest = new Vector3[4];
            Vector3[] at = { new Vector3(0, 0.36f, -0.03f), new Vector3(0, 0.2f, -0.1f), new Vector3(0, 0.09f, -0.19f), new Vector3(0, 0.02f, -0.3f) };
            float[] size = { 0.64f, 0.46f, 0.3f, 0.18f };
            for (int i = 0; i < wisps.Length; i++)
            {
                wisps[i] = Prims.Empty("Wisp" + i, body, at[i]).transform;
                wispRest[i] = at[i];
                Prims.Make(PrimitiveType.Sphere, wisps[i], Vector3.zero, new Vector3(size[i], size[i] * 0.8f, size[i]), i == 0 ? mist : wispMat, null, "Puff");
            }

            face = Prims.Empty("Face", body, new Vector3(0, 0.68f, 0)).transform;
            eyeballs = new Transform[2]; pupils = new Transform[2];
            pupilZ = 0.02f; lookScale = 0.6f;
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;
                var ball = Prims.Empty("Eye", face, new Vector3(s * 0.14f, 0.04f, 0.35f)).transform;
                var pupil = Prims.Empty("Pupil", ball, new Vector3(0, 0, pupilZ)).transform;
                Prims.Make(PrimitiveType.Sphere, pupil, Vector3.zero, new Vector3(0.09f, 0.15f, 0.05f), ink, null, "Dot");
                eyeballs[i] = ball;
                pupils[i] = pupil;
            }
            Prims.Make(PrimitiveType.Sphere, face, new Vector3(0, -0.13f, 0.36f), new Vector3(0.07f, 0.09f, 0.03f), ink, null, "Mouth");

            handL = Prims.Make(PrimitiveType.Sphere, pitch, Vector3.zero, Vector3.one * 0.16f, mist, null, "HandL").transform;
            handR = Prims.Make(PrimitiveType.Sphere, pitch, Vector3.zero, Vector3.one * 0.16f, mist, null, "HandR").transform;
            footL = Prims.Empty("FootL", lift, Vector3.zero).transform; // no feet, but the animation still moves these
            footR = Prims.Empty("FootR", lift, Vector3.zero).transform;
        }

        // ================================================================== FBX characters

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        /// <summary>Axis-aligned bounds of a renderer, measured in another transform's space.</summary>
        static Bounds LocalBounds(Renderer r, Transform space)
        {
            var mf = r.GetComponent<MeshFilter>();
            var mb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            var b = new Bounds();
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = space.InverseTransformPoint(r.transform.TransformPoint(c));
                if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
            }
            return b;
        }

        /// <summary>The critter's body material for its color: matte, metal, glowing, or (iridescent) its own animated copy.</summary>
        Material BodyMat(Color tint, CritterLook look, float shade)
        {
            var f = look.BodyFinish;
            bool glows = f.glow.maxColorComponent > 0.001f || f.iridescent;
            var m = Mats.Lit(tint * shade, Mathf.Max(0.1f, f.smooth - (shade < 1f ? 0.05f : 0f)), f.metal, glows ? f.glow * shade : (Color?)null);
            if (!f.iridescent) return m;
            m = new Material(m);
            var irid = lift.GetComponent<Iridescent>();
            if (irid == null) irid = lift.gameObject.AddComponent<Iridescent>();
            irid.Add(m, shade);
            return m;
        }

        /// <summary>Wraps part of a model in a fresh pivot whose axes match the rig's (so code can animate it
        /// without caring how the FBX's own axes were converted).</summary>
        Transform Pivot(string name, Transform part, Transform parent, Vector3 worldPos)
        {
            var piv = new GameObject(name).transform;
            piv.SetParent(parent, false);
            piv.position = worldPos;
            piv.rotation = transform.rotation;
            part.SetParent(piv, true);
            return piv;
        }

        void BuildModel(GameObject prefab, CritterLook look)
        {
            modelMode = true;
            var inst = Instantiate(prefab, transform);
            inst.name = prefab.name;
            inst.transform.localPosition = Vector3.zero;
            Transform mBody = Find(inst.transform, "Body"), mHead = Find(inst.transform, "Head"),
                mEyeL = Find(inst.transform, "Eye_L"), mEyeR = Find(inst.transform, "Eye_R"), mHat = Find(inst.transform, "Anchor_Hat");
            if (mBody == null || mHead == null || mEyeL == null || mEyeR == null)
            {
                Debug.LogWarning("[UnderPressure] " + prefab.name + " is missing Body/Head/Eye parts.");
                return;
            }

            // 1. turn the model so it stands up (+Y) and looks forward (+Z), whatever axes the FBX came in with
            Vector3 up = transform.InverseTransformDirection((mHat != null ? mHat.position : mHead.position) - mBody.position);
            Vector3 fwd = transform.InverseTransformDirection((mEyeL.position + mEyeR.position) * 0.5f - mHead.position);
            // a hunched or leaning character shouldn't come out tilted: take "up" square to the hips and the gaze
            Transform lL = Find(inst.transform, "Leg_L"), lR = Find(inst.transform, "Leg_R");
            if (lL != null && lR != null)
            {
                Vector3 right = transform.InverseTransformDirection(lR.position - lL.position);
                Vector3 up2 = Vector3.Cross(fwd, right);
                if (up2.sqrMagnitude > 1e-8f) up = Vector3.Dot(up2, up) < 0f ? -up2 : up2;
            }
            fwd = Vector3.ProjectOnPlane(fwd, up);
            if (fwd.sqrMagnitude > 1e-6f && up.sqrMagnitude > 1e-6f)
                inst.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(fwd, up)) * inst.transform.localRotation;

            // 2. feet on the floor; guard against centimetre imports
            var rends = inst.GetComponentsInChildren<Renderer>(true);
            Bounds all = LocalBounds(rends[0], transform);
            foreach (var r in rends) all.Encapsulate(LocalBounds(r, transform));
            if (all.size.y > 20f) { inst.transform.localScale *= 0.01f; all = LocalBounds(rends[0], transform); foreach (var r in rends) all.Encapsulate(LocalBounds(r, transform)); }
            inst.transform.localPosition += new Vector3(-all.center.x, -all.min.y, -all.center.z);
            Height = all.size.y;
            k = Height / 1.2f;
            pivotY = 0.25f * k;

            // 3. our animation pivots
            lift = Prims.Empty("Lift", transform, Vector3.zero).transform;
            pitch = Prims.Empty("Pitch", lift, new Vector3(0, pivotY, 0)).transform;
            body = Prims.Empty("Body", pitch, new Vector3(0, -pivotY, 0)).transform;
            spring = body.gameObject.AddComponent<Springy>();
            spring.stiffness = 300f; spring.damping = 11f;
            spring.SetBase(1f, true);

            // 4. colors: swap the imported materials for ours, by material name
            Color tint = look.BodyColor;
            foreach (var r in rends)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string n = mats[i].name.Replace(" (Instance)", "");
                    Material m = null;
                    switch (n)
                    {
                        case "Tint": m = BodyMat(tint, look, 1f); break;
                        case "Slime":
                            blob = true;
                            m = Mats.Transparent(new Color(tint.r, tint.g, tint.b, 0.55f));
                            break;
                        case "TintShade": m = BodyMat(tint, look, 0.9f); break;
                        case "Skin": m = Mats.Lit(new Color(0.4f, 0.45f, 0.56f), 0.3f); break;
                        case "Dark": m = Mats.Lit(new Color(0.05f, 0.05f, 0.07f), 0.7f); break;
                        case "Bone": m = Mats.Lit(new Color(0.93f, 0.9f, 0.8f), 0.5f); break;
                        case "Pupil": m = Mats.Lit(new Color(0.02f, 0.02f, 0.03f), 0.9f); break;
                        case "Glow": m = Mats.Lit(new Color(0.85f, 1f, 0.95f), 0.9f, 0f, new Color(0.7f, 1.2f, 1.05f)); break;
                        default:
                            // any other fixed color (Fur, FurLight, EarInner, Shoe, Sole, Accent ...): keep the modelled color
                            Color c = mats[i].HasProperty("_BaseColor") ? mats[i].GetColor("_BaseColor") : mats[i].color;
                            m = Mats.Lit(new Color(c.r, c.g, c.b, 1f), 0.3f);
                            break;
                    }
                    if (m != null) mats[i] = m;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            // 5. re-home the parts under the pivots (world positions kept)
            mBody.SetParent(body, true);
            face = mHead;
            headRest = face.localRotation;
            var eyes = new System.Collections.Generic.List<Transform>();
            foreach (var e in new[] { mEyeL, mEyeR }) eyes.Add(Pivot("EyePivot", e, mHead, e.position));
            eyeballs = eyes.ToArray();
            pupils = new Transform[0];
            eyeRest = new Vector3[eyeballs.Length];
            for (int i = 0; i < eyeballs.Length; i++) eyeRest[i] = eyeballs[i].localPosition;

            Transform hL = Find(inst.transform, "Hand_L"), hR = Find(inst.transform, "Hand_R");
            handL = hL != null ? Pivot("HandL", hL, pitch, hL.position) : Prims.Empty("HandL", pitch, Vector3.zero).transform;
            handR = hR != null ? Pivot("HandR", hR, pitch, hR.position) : Prims.Empty("HandR", pitch, Vector3.zero).transform;
            if (handL.localPosition.x > handR.localPosition.x) { var t = handL; handL = handR; handR = t; } // mirrored import: keep L on the left
            restHandL = handL.localPosition;
            restHandR = handR.localPosition;

            foreach (var side in new[] { "L", "R" })
            {
                var a = Find(inst.transform, "Arm_" + side);
                if (a == null) continue;
                var piv = Pivot("Arm" + side, a, body, a.position);
                var hand = Vector3.Distance(piv.position, handL.position) < Vector3.Distance(piv.position, handR.position) ? handL : handR;
                Vector3 dir = body.InverseTransformPoint(hand.position) - piv.localPosition;
                if (hand == handL) { armL = piv; armRestDirL = dir; armRestRotL = piv.localRotation; }
                else { armR = piv; armRestDirR = dir; armRestRotR = piv.localRotation; }
            }
            foreach (var side in new[] { "L", "R" })
            {
                var l = Find(inst.transform, "Leg_" + side);
                if (l == null) continue;
                var piv = Pivot("Leg" + side, l, lift, l.position);
                if (piv.localPosition.x < 0f) { legL = piv; legRestL = piv.localRotation; } else { legR = piv; legRestR = piv.localRotation; }
            }
            footL = footR = null;

            // 6. sizes the rest of the game cares about
            var bodyR = mBody.GetComponent<Renderer>();
            Bounds torso = bodyR != null ? LocalBounds(bodyR, body) : new Bounds(new Vector3(0, Height * 0.4f, 0), Vector3.one * 0.5f);
            Footprint = Mathf.Max(torso.size.x, torso.size.z) / 0.9f;
            holdLocalY = torso.center.y + torso.extents.y * 0.25f - pivotY;
            holdLocalZ = torso.max.z + 0.06f * k;
            HoldHeight = pivotY + holdLocalY - 0.05f;
            HoldReach = Mathf.Max(0.58f, torso.max.z + 0.22f);

            // 7. cosmetics
            var headR = mHead.GetComponent<Renderer>();
            {
                // the mouth: low on the front of the head (or the upper front of a headless blob), riding with the head
                Vector3 mp;
                if (headR != null) { var hb = LocalBounds(headR, body); mp = new Vector3(hb.center.x, hb.min.y + hb.size.y * 0.3f, hb.max.z - hb.size.z * 0.04f); }
                else mp = new Vector3(torso.center.x, torso.min.y + torso.size.y * 0.62f, torso.max.z);
                mouth = new GameObject("Mouth").transform;
                mouth.SetParent(mHead, false);
                mouth.position = body.TransformPoint(mp);
            }
            float headW = headR != null ? LocalBounds(headR, body).size.x : torso.size.x * 0.55f;
            var hatAnchor = new GameObject("HatAnchor").transform;
            hatAnchor.SetParent(mHead, false);
            hatAnchor.position = mHat != null ? mHat.position : mHead.position + Vector3.up * 0.4f * k;
            hatAnchor.rotation = transform.rotation;
            // hats were made for the old 0.92 m wide head: size them to this one (and cancel any import scale on the head)
            Vector3 hs = mHead.lossyScale;
            hatAnchor.localScale = new Vector3(1f / Mathf.Max(1e-4f, Mathf.Abs(hs.x)), 1f / Mathf.Max(1e-4f, Mathf.Abs(hs.y)), 1f / Mathf.Max(1e-4f, Mathf.Abs(hs.z)))
                                   * (headW / 0.92f);
            Accessories.AddHat(look.hat, hatAnchor);

            Vector3 tc = torso.center, ts = torso.size;
            if (blob)
            {
                // see-through goo: shirts and trunks float inside it, shrunk to fit, shoes sit on the floor in there
                Vector3 inner = new Vector3(ts.x * 0.42f, ts.y * 0.5f, ts.z * 0.42f);
                Vector3 ic = new Vector3(tc.x, ts.y * 0.42f + torso.min.y, tc.z);
                Accessories.AddLegs(look.legs, body, ic - new Vector3(0, inner.y * 0.32f, 0), new Vector3(inner.x, inner.y * 0.5f, inner.z));
                Accessories.AddTorso(look.torso, body, ic, inner);
                if (look.feet != 0)
                    for (int sd = -1; sd <= 1; sd += 2)
                    {
                        var fp = new GameObject("FootCosmetic").transform;
                        fp.SetParent(body, false);
                        fp.localPosition = new Vector3(sd * ts.x * 0.16f, torso.min.y + 0.03f * k, tc.z);
                        fp.localScale = Vector3.one * (ts.x * 0.2f / 0.22f);
                        Accessories.AddFeet(look.feet, fp);
                    }
                var trail = gameObject.AddComponent<SlimeTrail>();
                trail.Init(tint, ts.x * 0.5f);
            }
            else
            {
                Accessories.AddLegs(look.legs, body, tc - new Vector3(0, ts.y * 0.28f, 0), new Vector3(ts.x * 0.95f, ts.y * 0.45f, ts.z * 0.95f));
                Accessories.AddTorso(look.torso, body, tc, ts * 0.96f);
            }

            if (look.arms != 0)
            {
                Accessories.AddArms(look.arms, handL, handR);
                foreach (var h in new[] { handL, handR })
                {
                    var fl = h.Find("Floatie");
                    var hr = h.childCount > 0 ? h.GetChild(0).GetComponent<Renderer>() : null;
                    float hw = hr != null ? LocalBounds(hr, h).size.x : 0.17f;
                    if (fl != null) fl.localScale = Vector3.one * (hw / 0.17f);
                }
            }
            if (look.feet != 0)
                foreach (var leg in new[] { legL, legR })
                {
                    if (leg == null) continue;
                    var shoe = leg.GetComponentsInChildren<Transform>(true);
                    Transform s0 = null;
                    foreach (var t in shoe) if (t.name.StartsWith("Shoe_")) s0 = t;
                    if (s0 == null) continue;
                    var sr = s0.GetComponent<Renderer>();
                    Bounds fb = sr != null ? LocalBounds(sr, lift) : new Bounds(leg.localPosition, Vector3.one * 0.2f);
                    s0.gameObject.SetActive(false);
                    var fp = new GameObject("FootCosmetic").transform;
                    fp.SetParent(leg, false);
                    fp.position = lift.TransformPoint(new Vector3(fb.center.x, 0.06f * k, fb.center.z - fb.size.z * 0.05f));
                    fp.rotation = transform.rotation;
                    fp.localScale = Vector3.one * (fb.size.x / 0.22f) / Mathf.Max(0.0001f, leg.lossyScale.x);
                    Accessories.AddFeet(look.feet, fp);
                }

            Destroy(inst);
        }

        /// <summary>The original: one round squishy body, two huge eyes, stubby horns, a fang, a tail.</summary>
        void BuildBlorb(Material fur, Material furDark, Material belly, Material bulb, Color color, out float hatY, out Vector3 shellCenter, out Vector3 shellSize, out Vector3 hipCenter, out Vector3 hipSize)
        {
            var white = Mats.Lit(Color.white, 0.95f);
            var ink = Mats.Lit(new Color(0.05f, 0.04f, 0.07f), 0.95f);
            var horn = Mats.Lit(new Color(1f, 0.93f, 0.8f), 0.6f);
            var blush = Mats.Lit(Color.Lerp(color, new Color(1f, 0.4f, 0.5f), 0.6f), 0.3f);

            Prims.Make(PrimitiveType.Sphere, body, new Vector3(0, 0.52f, 0), new Vector3(0.92f, 0.86f, 0.86f), fur, null, "Body");
            Prims.Make(PrimitiveType.Sphere, body, new Vector3(0, 0.38f, 0.16f), new Vector3(0.6f, 0.5f, 0.6f), belly, null, "Belly");
            Prims.Make(PrimitiveType.Sphere, body, new Vector3(-0.08f, 0.96f, -0.02f), Vector3.one * 0.2f, fur, null, "Tuft");
            Prims.Make(PrimitiveType.Sphere, body, new Vector3(0.07f, 0.98f, 0.03f), Vector3.one * 0.17f, fur, null, "Tuft");

            face = Prims.Empty("Face", body, new Vector3(0, 0.6f, 0)).transform;
            eyeballs = new Transform[2]; pupils = new Transform[2];
            pupilZ = 0.08f; lookScale = 1f;
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;
                var ball = Prims.Empty("Eye", face, new Vector3(s * 0.17f, 0.08f, 0.36f)).transform;
                Prims.Make(PrimitiveType.Sphere, ball, Vector3.zero, new Vector3(0.34f, 0.38f, 0.22f), white, null, "Sclera");
                var pupil = Prims.Empty("Pupil", ball, new Vector3(0, 0, pupilZ)).transform;
                Prims.Make(PrimitiveType.Sphere, pupil, Vector3.zero, new Vector3(0.22f, 0.26f, 0.1f), ink, null, "Iris");
                Prims.Make(PrimitiveType.Sphere, pupil, new Vector3(0.05f, 0.06f, 0.045f), Vector3.one * 0.075f, white, null, "Sparkle");
                Prims.Make(PrimitiveType.Sphere, pupil, new Vector3(-0.04f, -0.06f, 0.045f), Vector3.one * 0.035f, white, null, "Sparkle2");
                eyeballs[i] = ball;
                pupils[i] = pupil;
                Prims.Make(PrimitiveType.Sphere, face, new Vector3(s * 0.3f, -0.1f, 0.35f), new Vector3(0.12f, 0.06f, 0.05f), blush, null, "Blush");
                Prims.Make(PrimitiveType.Capsule, body, new Vector3(s * 0.27f, 0.93f, -0.02f), new Vector3(0.1f, 0.09f, 0.1f), horn,
                    Quaternion.Euler(0, 0, -s * 28f), "Horn");
            }
            Prims.Make(PrimitiveType.Sphere, face, new Vector3(0, -0.15f, 0.43f), new Vector3(0.16f, 0.05f, 0.05f), ink, null, "Mouth");
            Prims.Make(PrimitiveType.Cube, face, new Vector3(0.04f, -0.185f, 0.45f), new Vector3(0.035f, 0.05f, 0.02f), white, Quaternion.Euler(0, 0, 8f), "Fang");

            antenna = Prims.Empty("Antenna", body, new Vector3(0, 0.95f, -0.08f)).transform;
            Prims.Make(PrimitiveType.Cylinder, antenna, new Vector3(0, 0.18f, 0), new Vector3(0.035f, 0.18f, 0.035f), furDark, null, "Stalk");
            Prims.Make(PrimitiveType.Sphere, antenna, new Vector3(0, 0.4f, 0), Vector3.one * 0.14f, bulb, null, "Bulb");

            tail = Prims.Empty("Tail", body, new Vector3(0, 0.3f, -0.4f)).transform;
            Prims.Make(PrimitiveType.Sphere, tail, new Vector3(0, 0.02f, -0.06f), new Vector3(0.12f, 0.12f, 0.2f), fur, null, "TailBase");
            Prims.Make(PrimitiveType.Sphere, tail, new Vector3(0, 0.1f, -0.17f), Vector3.one * 0.13f, belly, null, "TailTuft");

            hatY = 0.94f;
            shellCenter = new Vector3(0, 0.36f, 0);
            shellSize = new Vector3(0.97f, 0.56f, 0.92f);
            hipCenter = new Vector3(0, 0.22f, 0);
            hipSize = new Vector3(0.9f, 0.34f, 0.86f);
        }

        /// <summary>SHARC: a chubby upright shark pup. Rounded snout, beady eyes, a grin full of tiny teeth,
        /// a dorsal fin that wobbles behind you (it's the "antenna"), gill slits and a little forked tail.</summary>
        void BuildSharc(Material fur, Material furDark, Material belly, Color color, out float hatY, out Vector3 shellCenter, out Vector3 shellSize, out Vector3 hipCenter, out Vector3 hipSize)
        {
            var white = Mats.Lit(Color.white, 0.95f);
            var ink = Mats.Lit(new Color(0.03f, 0.03f, 0.05f), 0.95f);
            var tummy = Mats.Lit(Color.Lerp(color, Color.white, 0.8f), 0.35f);
            var blush = Mats.Lit(Color.Lerp(color, new Color(1f, 0.45f, 0.55f), 0.65f), 0.3f);

            Prims.Make(PrimitiveType.Sphere, body, new Vector3(0, 0.55f, 0), new Vector3(0.86f, 1.0f, 0.9f), fur, null, "Body");
            Prims.Make(PrimitiveType.Sphere, body, new Vector3(0, 0.46f, 0.19f), new Vector3(0.62f, 0.74f, 0.54f), tummy, null, "Tummy");
            Prims.Make(PrimitiveType.Sphere, body, new Vector3(0, 0.84f, 0.2f), new Vector3(0.66f, 0.44f, 0.56f), fur, null, "Snout");

            face = Prims.Empty("Face", body, new Vector3(0, 0.84f, 0)).transform;
            eyeballs = new Transform[2]; pupils = new Transform[2];
            pupilZ = 0.04f; lookScale = 0.45f;
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;
                var ball = Prims.Empty("Eye", face, new Vector3(s * 0.22f, 0.06f, 0.36f)).transform;
                ball.localRotation = Quaternion.Euler(0, s * 25f, 0);
                Prims.Make(PrimitiveType.Sphere, ball, Vector3.zero, new Vector3(0.14f, 0.17f, 0.1f), ink, null, "Bead");
                var pupil = Prims.Empty("Shine", ball, new Vector3(0, 0, pupilZ)).transform;
                Prims.Make(PrimitiveType.Sphere, pupil, new Vector3(0.025f, 0.035f, 0f), Vector3.one * 0.05f, white, null, "Sparkle");
                Prims.Make(PrimitiveType.Sphere, pupil, new Vector3(-0.02f, -0.03f, 0f), Vector3.one * 0.022f, white, null, "Sparkle2");
                eyeballs[i] = ball; pupils[i] = pupil;
                Prims.Make(PrimitiveType.Sphere, face, new Vector3(s * 0.25f, -0.08f, 0.33f), new Vector3(0.1f, 0.05f, 0.05f), blush, null, "Blush");
                for (int g = 0; g < 3; g++) // gill slits
                    Prims.Make(PrimitiveType.Cube, body, new Vector3(s * 0.415f, 0.66f - g * 0.07f, 0.02f - g * 0.03f), new Vector3(0.02f, 0.035f, 0.14f), furDark,
                        Quaternion.Euler(0, s * -12f, 0), "Gill");
            }
            // big grin with a row of tiny triangle teeth
            Prims.Make(PrimitiveType.Sphere, face, new Vector3(0, -0.15f, 0.43f), new Vector3(0.38f, 0.08f, 0.07f), ink, null, "Mouth");
            for (int k = 0; k < 5; k++)
            {
                float x = (k - 2) * 0.06f;
                Prims.Make(PrimitiveType.Cube, face, new Vector3(x, -0.125f, 0.465f - Mathf.Abs(k - 2) * 0.012f), new Vector3(0.03f, 0.03f, 0.015f), white,
                    Quaternion.Euler(0, 0, 45f), "Tooth");
            }

            // dorsal fin lags behind like the other critters' antennae
            antenna = Prims.Empty("Antenna", body, new Vector3(0, 0.98f, -0.14f)).transform;
            Prims.Make(PrimitiveType.Sphere, antenna, new Vector3(0, 0.12f, -0.06f), new Vector3(0.07f, 0.34f, 0.3f), fur, Quaternion.Euler(-28f, 0, 0), "DorsalFin");

            tail = Prims.Empty("Tail", body, new Vector3(0, 0.32f, -0.42f)).transform;
            Prims.Make(PrimitiveType.Sphere, tail, new Vector3(0, 0, -0.05f), new Vector3(0.14f, 0.16f, 0.18f), fur, null, "TailBase");
            Prims.Make(PrimitiveType.Sphere, tail, new Vector3(0, 0.1f, -0.16f), new Vector3(0.05f, 0.26f, 0.14f), fur, Quaternion.Euler(-40f, 0, 0), "TailUpper");
            Prims.Make(PrimitiveType.Sphere, tail, new Vector3(0, -0.04f, -0.15f), new Vector3(0.05f, 0.17f, 0.11f), fur, Quaternion.Euler(40f, 0, 0), "TailLower");

            hatY = 1.04f;
            shellCenter = new Vector3(0, 0.4f, 0);
            shellSize = new Vector3(0.9f, 0.62f, 0.94f);
            hipCenter = new Vector3(0, 0.22f, 0);
            hipSize = new Vector3(0.84f, 0.34f, 0.88f);
        }

        public void Squash(float amount) => spring.Squash(amount);
        public void HeadShake() => shakeT = 0.45f;
        public void WorkPulse() => workPulse = 1f;
        public void Hop() { hopT = 0f; spring.Squash(-0.35f); }
        public void LookAt(Vector3 world) => lookTarget = world;
        public void Cheer() { cheerT = 0.9f; Hop(); }
        public void Sad() { sadT = 2.5f; spring.Squash(0.3f); }

        /// <summary>Take a drink: 'makeProp' builds the drink (e.g. a bottle) under the transform it's given, centred
        /// on it, at the given scale. The rig pulls it out into the right hand, drinks, and puts it away.</summary>
        public void Sip(System.Func<Transform, float, Transform> makeProp)
        {
            if (sipGrip != null) Destroy(sipGrip.gameObject);
            if (mouth == null)
            {
                // the old placeholder critters: a point on the front of the face
                mouth = new GameObject("Mouth").transform;
                mouth.SetParent(face, false);
                mouth.position = body.TransformPoint(new Vector3(0f, Height * 0.62f, 0.42f * k));
            }
            sipGrip = new GameObject("SipGrip").transform;
            sipGrip.SetParent(handR, false);
            sipScale = 1.25f * k;
            makeProp(sipGrip, sipScale);
            sipGrip.localScale = Vector3.zero;
            sipT = 0f;
            sipGlugT = 0.62f;
        }

        void EndSip()
        {
            if (sipGrip != null) Destroy(sipGrip.gameObject);
            sipGrip = null;
            sipT = -1f;
        }

        /// <summary>Advance the sip; returns the right hand's target (pitch space) while it's going.</summary>
        Vector3 SipHand(float dt, Vector3 rest, out float headUp, out float lean)
        {
            headUp = 0f; lean = 0f;
            if (sipGrip == null || handR == null) { EndSip(); return rest; }
            sipT += dt;
            float u = sipT;
            // timeline: pull out 0-0.25, raise 0.25-0.55, drink 0.55-1.45, lower and put away 1.45-1.9
            float raise = Juice.OutCubic(Mathf.Clamp01((u - 0.25f) / 0.3f)) * (1f - Juice.OutCubic(Mathf.Clamp01((u - 1.45f) / 0.3f)));
            float drink = Mathf.Clamp01((u - 0.5f) / 0.15f) * (1f - Mathf.Clamp01((u - 1.4f) / 0.15f));
            float tilt = Mathf.Lerp(0f, -55f, raise) + (-60f) * drink;
            headUp = 22f * drink;
            lean = -9f * drink;

            // grow out of the hand, shrink away at the end
            float grow = u < 0.25f ? Juice.OutBack(Mathf.Clamp01(u / 0.22f)) : u > 1.72f ? 1f - Mathf.Clamp01((u - 1.72f) / 0.18f) : 1f;
            Vector3 hs = handR.lossyScale, ts = transform.lossyScale;
            float inv = Mathf.Abs(hs.x) > 1e-5f ? Mathf.Abs(ts.x / hs.x) : 1f; // undo any scale on the hand
            sipGrip.localScale = Vector3.one * Mathf.Max(0f, grow) * inv;
            sipGrip.rotation = pitch.rotation * Quaternion.Euler(tilt, 0f, 0f);

            // the hand goes where the bottle's mouth meets ours
            Vector3 capWorld = sipGrip.rotation * Vector3.up * (0.085f * sipScale * transform.lossyScale.x);
            Vector3 atMouth = pitch.InverseTransformPoint(mouth.position + transform.forward * 0.03f * k - capWorld);
            Vector3 pocket = rest + new Vector3(0.04f, -0.06f, -0.08f) * k;
            Vector3 target = u < 0.25f ? pocket : Vector3.Lerp(pocket, atMouth, raise);

            if (drink > 0.5f)
            {
                sipGlugT -= dt;
                if (sipGlugT <= 0f)
                {
                    sipGlugT = 0.22f;
                    spring.Squash(0.07f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.bloop : null, 0.35f, Random.Range(0.7f, 0.9f));
                    Fx.Burst(mouth.position, new Color(0.5f, 0.85f, 1f), Color.white, 2, 0.6f, 0.04f, 0.35f, -0.4f);
                }
            }
            if (u >= 1.45f && u - dt < 1.45f) spring.Squash(-0.2f); // ahh
            if (u >= SipDur) EndSip();
            return target;
        }

        public void Tick(float dt, Vector3 facing, float speed01, float height, bool crawling, bool holding, bool working, float vault01)
        {
            if (facing.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            cheerT = Mathf.Max(0f, cheerT - dt);
            sadT = Mathf.Max(0f, sadT - dt);

            float moving = Mathf.Clamp01(speed01 * 1.5f);
            phase += dt * (6f + speed01 * 10f) * (crawling ? 0.8f : 1f) * (moving > 0.05f ? 1f : 0f);
            float sinP = Mathf.Sin(phase);

            float hop = 0f;
            if (hopT >= 0f)
            {
                hopT += dt / 0.32f;
                hop = Mathf.Sin(Mathf.PI * Mathf.Clamp01(hopT)) * 0.4f;
                if (hopT >= 1f) { hopT = -1f; spring.Squash(0.3f); }
            }
            lift.localPosition = new Vector3(0, height + hop * k, 0);

            // posture
            float targetPitch = crawling ? 60f : 8f * moving;
            if (vault01 >= 0f) targetPitch = Mathf.Sin(Mathf.PI * vault01) * 40f;
            if (working) targetPitch = 16f + Mathf.Sin(Time.time * 30f) * 3f;
            if (sadT > 0f) targetPitch = -6f;
            if (Sipping) targetPitch = sipLean;
            curPitch = Mathf.Lerp(curPitch, targetPitch, 1f - Mathf.Exp(-14f * dt));
            // waddle: critters rock side to side
            float targetTilt = crawling ? sinP * 7f * moving : sinP * 7f * moving;
            if (ghostMode) targetTilt = Mathf.Sin(Time.time * 1.7f) * 5f; // a ghost drifts, it doesn't waddle
            curTilt = Mathf.Lerp(curTilt, targetTilt, 1f - Mathf.Exp(-14f * dt));
            pitch.localRotation = Quaternion.Euler(curPitch, 0f, curTilt);

            float bob = crawling || blob || ghostMode ? 0f : Mathf.Abs(sinP) * 0.06f * moving * k;
            if (blob && moving > 0.1f && Mathf.Sign(sinP) != Mathf.Sign(lastSinP)) spring.Squash(0.14f * moving); // goo schlorps along
            lastSinP = sinP;
            float breathe = Mathf.Sin(Time.time * 2.4f) * 0.012f * k;
            body.localPosition = new Vector3(0, -pivotY + bob + breathe, crawling ? -0.05f * k : 0f);

            // face
            shakeT = Mathf.Max(0f, shakeT - dt);
            float shakeYaw = Mathf.Sin(shakeT * 55f) * 25f * (shakeT / 0.45f);
            face.localRotation = Quaternion.Euler((crawling ? -40f : (sadT > 0f ? 10f : 0f)) - sipHeadUp, shakeYaw, 0f) * headRest;

            // eyes: blink, happy squint, sad droop, pupils follow the target
            blinkTimer -= dt;
            if (blinkTimer <= 0f) { blinkT = 0.12f; blinkTimer = Random.Range(1.8f, 4.5f); }
            blinkT = Mathf.Max(0f, blinkT - dt);
            float open = blinkT > 0f ? 0.1f : (sipHeadUp > 8f ? 0.25f : cheerT > 0f ? 0.45f : (sadT > 0f ? 0.75f : 1f));
            Vector3 local = transform.InverseTransformPoint(lookTarget);
            Vector2 look = new Vector2(local.x, local.y - (0.7f + height)) / Mathf.Max(1f, local.magnitude);
            if (sadT > 0f) look = new Vector2(0f, -0.8f);
            if (cheerT > 0f) look = new Vector2(0f, 0.6f);
            Vector2 offset = Vector2.ClampMagnitude(look * 0.09f, 0.07f);
            offset *= lookScale;
            if (modelMode)
            {
                // dot eyes: squash to blink, drift a little toward what we're looking at
                for (int i = 0; i < eyeballs.Length; i++)
                {
                    eyeballs[i].localScale = new Vector3(1f, open, 1f);
                    var target = eyeRest[i] + new Vector3(offset.x, offset.y, 0f) * 0.25f * k;
                    eyeballs[i].localPosition = Vector3.Lerp(eyeballs[i].localPosition, target, 1f - Mathf.Exp(-18f * dt));
                }
            }
            else for (int i = 0; i < eyeballs.Length; i++)
            {
                eyeballs[i].localScale = new Vector3(1f, open, 1f);
                var target = new Vector3(offset.x, offset.y, pupilZ);
                pupils[i].localPosition = Vector3.Lerp(pupils[i].localPosition, target, 1f - Mathf.Exp(-18f * dt));
                // pupils widen when carrying something precious
                pupils[i].localScale = Vector3.one * (holding ? 1.12f : 1f);
            }

            // antenna secondary motion: lags behind acceleration, springs back
            Vector3 pos = transform.position;
            if (!hasLast) { lastPos = pos; hasLast = true; }
            Vector3 vel = (pos - lastPos) / Mathf.Max(dt, 0.0001f);
            Vector3 acc = (vel - lastVel) / Mathf.Max(dt, 0.0001f);
            lastPos = pos; lastVel = vel;
            Vector3 accLocal = transform.InverseTransformDirection(acc);
            Vector3 targetAngle = new Vector3(Mathf.Clamp(-accLocal.z * 0.9f, -40f, 40f), 0f, Mathf.Clamp(accLocal.x * 0.9f, -40f, 40f));
            Vector3 a = -90f * (antennaAngle - targetAngle * 0.25f) - 7f * antennaVel;
            antennaVel += a * dt;
            antennaAngle += antennaVel * dt;
            if (antenna != null)
                antenna.localRotation = Quaternion.Euler(antennaAngle.x + (crawling ? -30f : 0f), 0f, antennaAngle.z + Mathf.Sin(Time.time * 3f) * 3f);

            // ghost tail: each puff lags behind the drift a little more than the one before, and sways
            if (wisps != null)
            {
                Vector3 velLocal = transform.InverseTransformDirection(vel);
                for (int i = 0; i < wisps.Length; i++)
                {
                    float f = i + 1;
                    Vector3 lag = Vector3.ClampMagnitude(new Vector3(-velLocal.x, 0f, -velLocal.z) * 0.025f * f, 0.12f * f);
                    float sway = Mathf.Sin(Time.time * 3f - i * 0.9f) * 0.035f * f;
                    Vector3 want = wispRest[i] + new Vector3(sway + lag.x, Mathf.Sin(Time.time * 2.2f - i) * 0.015f, lag.z);
                    wisps[i].localPosition = Vector3.Lerp(wisps[i].localPosition, want, 1f - Mathf.Exp(-10f * dt));
                }
            }

            // tail wag
            if (tail != null)
                tail.localRotation = Quaternion.Euler(-20f, Mathf.Sin(Time.time * (moving > 0.1f ? 14f : 4f) + 1f) * (moving > 0.1f ? 30f : 12f), 0f);

            // legs (models swing them from the hip) / feet (the old critter slides them)
            if (modelMode)
            {
                float swing = crawling ? 0f : sinP * 30f * moving;
                float fold = crawling ? 55f : 0f;
                if (legL != null) legL.localRotation = Quaternion.Slerp(legL.localRotation, Quaternion.Euler(fold + swing, 0f, 0f) * legRestL, 1f - Mathf.Exp(-20f * dt));
                if (legR != null) legR.localRotation = Quaternion.Slerp(legR.localRotation, Quaternion.Euler(fold - swing, 0f, 0f) * legRestR, 1f - Mathf.Exp(-20f * dt));
            }
            else if (crawling)
            {
                footL.localPosition = new Vector3(-0.16f, 0.12f, -0.42f + sinP * 0.1f * moving);
                footR.localPosition = new Vector3(0.16f, 0.12f, -0.42f - sinP * 0.1f * moving);
            }
            else
            {
                float stride = 0.16f * moving;
                footL.localPosition = new Vector3(-0.17f, 0.06f + Mathf.Max(0f, sinP) * 0.1f * moving, 0.05f + sinP * stride);
                footR.localPosition = new Vector3(0.17f, 0.06f + Mathf.Max(0f, -sinP) * 0.1f * moving, 0.05f - sinP * stride);
            }

            // hands (local to pitch pivot)
            workPulse = Mathf.MoveTowards(workPulse, 0f, dt * 8f);
            Vector3 l, r;
            if (cheerT > 0f && !holding)
            {
                float w = Mathf.Sin(Time.time * 25f) * 0.06f;
                l = new Vector3(-0.42f, 0.75f + w, 0.05f);
                r = new Vector3(0.42f, 0.75f - w, 0.05f);
            }
            else if (holding && !crawling)
            {
                l = new Vector3(-0.32f, 0.42f, 0.4f);
                r = new Vector3(0.32f, 0.42f, 0.4f);
            }
            else if (working)
            {
                float t = Time.time * 22f;
                l = new Vector3(-0.16f + Mathf.Cos(t) * 0.06f, 0.2f + Mathf.Sin(t) * 0.06f, 0.48f);
                r = new Vector3(0.16f - Mathf.Cos(t) * 0.06f, 0.2f - Mathf.Sin(t) * 0.06f, 0.48f + workPulse * 0.08f);
            }
            else if (crawling)
            {
                l = new Vector3(-0.27f, -0.05f + Mathf.Max(0f, sinP) * 0.12f * moving, 0.45f + sinP * 0.12f * moving);
                r = new Vector3(0.27f, -0.05f + Mathf.Max(0f, -sinP) * 0.12f * moving, 0.45f - sinP * 0.12f * moving);
            }
            else if (vault01 >= 0f)
            {
                l = new Vector3(-0.45f, 0.6f, 0.1f);
                r = new Vector3(0.45f, 0.6f, 0.1f);
            }
            else
            {
                l = new Vector3(-0.44f, 0.25f, -sinP * 0.14f * moving);
                r = new Vector3(0.44f, 0.25f, sinP * 0.14f * moving);
            }
            if (modelMode)
            {
                // the same poses, measured from this model's own resting hands and scaled to its size
                l = restHandL + (l - new Vector3(-0.44f, 0.25f, 0f)) * k;
                r = restHandR + (r - new Vector3(0.44f, 0.25f, 0f)) * k;
                if (holding && !crawling)
                {
                    // carry at chest height in front of the belly, however long or short the arms are
                    l.y = r.y = holdLocalY;
                    l.z = r.z = holdLocalZ;
                }
            }
            if (Sipping) r = SipHand(dt, r, out sipHeadUp, out sipLean);
            else sipHeadUp = sipLean = 0f;
            handL.localPosition = Vector3.Lerp(handL.localPosition, l, 1f - Mathf.Exp(-25f * dt));
            handR.localPosition = Vector3.Lerp(handR.localPosition, r, 1f - Mathf.Exp(-25f * dt));

            // arms reach for wherever the hands went
            if (armL != null) armL.localRotation = Quaternion.FromToRotation(armRestDirL, body.InverseTransformPoint(handL.position) - armL.localPosition) * armRestRotL;
            if (armR != null) armR.localRotation = Quaternion.FromToRotation(armRestDirR, body.InverseTransformPoint(handR.position) - armR.localPosition) * armRestRotR;
        }
    }

    /// <summary>Thin-film shimmer: the body's hue drifts through the rainbow, faster while moving.</summary>
    public class Iridescent : MonoBehaviour
    {
        readonly System.Collections.Generic.List<(Material m, float shade)> mats = new System.Collections.Generic.List<(Material, float)>();
        float hue;
        Vector3 last;

        public void Add(Material m, float shade) => mats.Add((m, shade));

        void Update()
        {
            float moved = (transform.position - last).magnitude;
            last = transform.position;
            hue = Mathf.Repeat(hue + Time.deltaTime * 0.08f + moved * 0.12f, 1f);
            for (int i = 0; i < mats.Count; i++)
            {
                var (m, shade) = mats[i];
                if (m == null) continue;
                Color c = Color.HSVToRGB(Mathf.Repeat(hue + i * 0.08f, 1f), 0.42f, 1f) * shade;
                Mats.SetBase(m, c);
                Mats.SetEmission(m, c * 0.12f);
            }
        }

        void OnDestroy() { foreach (var (m, _) in mats) if (m != null) Destroy(m); }
    }
}
