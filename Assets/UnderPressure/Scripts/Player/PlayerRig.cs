using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// The critter: an original round little monster — one squishy body, enormous eyes that follow what you're
    /// about to grab, stubby horns, a single fang, and a glowing antenna (in the player's colour) that lags
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

        /// <summary>Build (or rebuild) the critter from a look: species, body colour and outfit.</summary>
        public void Build(CritterLook look)
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            hasLast = false;

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
            if (look.species == 2) BuildSharc(fur, furDark, belly, color, out hatY, out shellCenter, out shellSize, out hipCenter, out hipSize);
            else if (look.species == 1) BuildPeeper(fur, furDark, belly, bulb, color, out hatY, out shellCenter, out shellSize, out hipCenter, out hipSize);
            else BuildBlorb(fur, furDark, belly, bulb, color, out hatY, out shellCenter, out shellSize, out hipCenter, out hipSize);

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

        /// <summary>The second species: a tall bean with one enormous eye, fin ears and twin antennae.</summary>
        void BuildPeeper(Material fur, Material furDark, Material belly, Material bulb, Color color, out float hatY, out Vector3 shellCenter, out Vector3 shellSize, out Vector3 hipCenter, out Vector3 hipSize)
        {
            var white = Mats.Lit(Color.white, 0.95f);
            var ink = Mats.Lit(new Color(0.05f, 0.04f, 0.07f), 0.95f);
            var iris = Mats.Lit(Color.Lerp(color, new Color(0.1f, 0.1f, 0.15f), 0.55f), 0.9f);
            var blush = Mats.Lit(Color.Lerp(color, new Color(1f, 0.4f, 0.5f), 0.6f), 0.3f);

            Prims.Make(PrimitiveType.Sphere, body, new Vector3(0, 0.62f, 0), new Vector3(0.78f, 1.12f, 0.72f), fur, null, "Body");
            Prims.Make(PrimitiveType.Sphere, body, new Vector3(0, 0.46f, 0.15f), new Vector3(0.5f, 0.66f, 0.5f), belly, null, "Belly");
            for (int s = -1; s <= 1; s += 2) // fin ears
                Prims.Make(PrimitiveType.Sphere, body, new Vector3(s * 0.39f, 0.86f, -0.02f), new Vector3(0.08f, 0.3f, 0.2f), furDark,
                    Quaternion.Euler(0, 0, s * 22f), "FinEar");

            face = Prims.Empty("Face", body, new Vector3(0, 0.8f, 0)).transform;
            eyeballs = new Transform[1]; pupils = new Transform[1];
            pupilZ = 0.11f; lookScale = 1.5f;
            var ball = Prims.Empty("Eye", face, new Vector3(0, 0.06f, 0.26f)).transform;
            Prims.Make(PrimitiveType.Sphere, ball, Vector3.zero, new Vector3(0.5f, 0.5f, 0.3f), white, null, "Sclera");
            var pupil = Prims.Empty("Pupil", ball, new Vector3(0, 0, pupilZ)).transform;
            Prims.Make(PrimitiveType.Sphere, pupil, Vector3.zero, new Vector3(0.3f, 0.3f, 0.12f), iris, null, "Iris");
            Prims.Make(PrimitiveType.Sphere, pupil, new Vector3(0, 0, 0.035f), new Vector3(0.16f, 0.16f, 0.08f), ink, null, "Pupil");
            Prims.Make(PrimitiveType.Sphere, pupil, new Vector3(0.07f, 0.08f, 0.07f), Vector3.one * 0.08f, white, null, "Sparkle");
            Prims.Make(PrimitiveType.Sphere, pupil, new Vector3(-0.05f, -0.07f, 0.07f), Vector3.one * 0.04f, white, null, "Sparkle2");
            eyeballs[0] = ball; pupils[0] = pupil;
            for (int s = -1; s <= 1; s += 2)
                Prims.Make(PrimitiveType.Sphere, face, new Vector3(s * 0.26f, -0.16f, 0.27f), new Vector3(0.1f, 0.05f, 0.05f), blush, null, "Blush");
            // wide grin with two little teeth
            Prims.Make(PrimitiveType.Sphere, face, new Vector3(0, -0.24f, 0.31f), new Vector3(0.24f, 0.06f, 0.05f), ink, null, "Mouth");
            for (int s = -1; s <= 1; s += 2)
                Prims.Make(PrimitiveType.Cube, face, new Vector3(s * 0.04f, -0.26f, 0.33f), new Vector3(0.035f, 0.04f, 0.02f), white, null, "Tooth");

            antenna = Prims.Empty("Antenna", body, new Vector3(0, 1.14f, -0.04f)).transform;
            for (int s = -1; s <= 1; s += 2)
            {
                var stalk = Prims.Empty("Stalk", antenna, Vector3.zero).transform;
                stalk.localRotation = Quaternion.Euler(0, 0, -s * 20f);
                Prims.Make(PrimitiveType.Cylinder, stalk, new Vector3(0, 0.15f, 0), new Vector3(0.03f, 0.15f, 0.03f), furDark, null, "Stalk");
                Prims.Make(PrimitiveType.Sphere, stalk, new Vector3(0, 0.33f, 0), Vector3.one * 0.11f, bulb, null, "Bulb");
            }
            tail = Prims.Empty("Tail", body, new Vector3(0, 0.25f, -0.33f)).transform;
            Prims.Make(PrimitiveType.Sphere, tail, Vector3.zero, Vector3.one * 0.12f, fur, null, "TailNub");

            hatY = 1.16f;
            shellCenter = new Vector3(0, 0.42f, 0);
            shellSize = new Vector3(0.82f, 0.72f, 0.8f);
            hipCenter = new Vector3(0, 0.22f, 0);
            hipSize = new Vector3(0.7f, 0.34f, 0.66f);
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
            lift.localPosition = new Vector3(0, height + hop, 0);

            // posture
            float targetPitch = crawling ? 60f : 8f * moving;
            if (vault01 >= 0f) targetPitch = Mathf.Sin(Mathf.PI * vault01) * 40f;
            if (working) targetPitch = 16f + Mathf.Sin(Time.time * 30f) * 3f;
            if (sadT > 0f) targetPitch = -6f;
            curPitch = Mathf.Lerp(curPitch, targetPitch, 1f - Mathf.Exp(-14f * dt));
            // waddle: critters rock side to side
            float targetTilt = crawling ? sinP * 7f * moving : sinP * 7f * moving;
            curTilt = Mathf.Lerp(curTilt, targetTilt, 1f - Mathf.Exp(-14f * dt));
            pitch.localRotation = Quaternion.Euler(curPitch, 0f, curTilt);

            float bob = crawling ? 0f : Mathf.Abs(sinP) * 0.06f * moving;
            float breathe = Mathf.Sin(Time.time * 2.4f) * 0.012f;
            body.localPosition = new Vector3(0, -0.25f + bob + breathe, crawling ? -0.05f : 0f);

            // face
            shakeT = Mathf.Max(0f, shakeT - dt);
            float shakeYaw = Mathf.Sin(shakeT * 55f) * 25f * (shakeT / 0.45f);
            face.localRotation = Quaternion.Euler(crawling ? -40f : (sadT > 0f ? 10f : 0f), shakeYaw, 0f);

            // eyes: blink, happy squint, sad droop, pupils follow the target
            blinkTimer -= dt;
            if (blinkTimer <= 0f) { blinkT = 0.12f; blinkTimer = Random.Range(1.8f, 4.5f); }
            blinkT = Mathf.Max(0f, blinkT - dt);
            float open = blinkT > 0f ? 0.1f : (cheerT > 0f ? 0.45f : (sadT > 0f ? 0.75f : 1f));
            Vector3 local = transform.InverseTransformPoint(lookTarget);
            Vector2 look = new Vector2(local.x, local.y - (0.7f + height)) / Mathf.Max(1f, local.magnitude);
            if (sadT > 0f) look = new Vector2(0f, -0.8f);
            if (cheerT > 0f) look = new Vector2(0f, 0.6f);
            Vector2 offset = Vector2.ClampMagnitude(look * 0.09f, 0.07f);
            offset *= lookScale;
            for (int i = 0; i < eyeballs.Length; i++)
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
            antenna.localRotation = Quaternion.Euler(antennaAngle.x + (crawling ? -30f : 0f), 0f, antennaAngle.z + Mathf.Sin(Time.time * 3f) * 3f);

            // tail wag
            tail.localRotation = Quaternion.Euler(-20f, Mathf.Sin(Time.time * (moving > 0.1f ? 14f : 4f) + 1f) * (moving > 0.1f ? 30f : 12f), 0f);

            // feet
            if (crawling)
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
            handL.localPosition = Vector3.Lerp(handL.localPosition, l, 1f - Mathf.Exp(-25f * dt));
            handR.localPosition = Vector3.Lerp(handR.localPosition, r, 1f - Mathf.Exp(-25f * dt));
        }
    }
}
