using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// During a round: a fixed angled camera framing the whole room. Between rounds: a follow cam that drops in,
    /// tilts toward the back wall and tracks the players. Both have live-tunable pitch + zoom (GameConfig).
    /// Trauma-based shake and FOV punches on top.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        /// <summary>Distance from the camera to the centre of the framed area (round camera).</summary>
        public float Distance { get; private set; }
        Vector3 basePos;
        Quaternion baseRot;
        float baseFov, overviewFov;
        float trauma;
        float fovOffset, fovVel;
        float seed;

        Bounds frame;
        float builtPitch = -1f, builtZoom = -1f, builtAspect = -1f;

        // between-rounds follow mode (also during rounds, with the Follow camera setting)
        bool follow, followBySetting;
        Vector3 followPos, followVel;
        Quaternion followRot;
        Vector3 lookExtra; // a world point we also want in view (the wall monitor)

        public void Init(Camera cam, Bounds roomFrame, float fov = 34f)
        {
            Cam = cam;
            Cam.fieldOfView = fov;
            baseFov = overviewFov = fov;
            seed = Random.value * 100f;
            frame = roomFrame;
            follow = followBySetting = false;
            builtPitch = -1f;
            RebuildOverview(true);
        }

        /// <summary>The room grew: frame the new size (follow mode, if on, carries on as it was).</summary>
        public void SetFrame(Bounds roomFrame)
        {
            frame = roomFrame;
            RebuildOverview(false);
        }

        float Pitch => Game.Config != null ? Game.Config.gameplayCameraPitch : 48f;
        float Zoom => Game.Config != null ? Mathf.Max(0.1f, Game.Config.gameplayCameraZoom) : 1f;

        /// <summary>Fit the room's footprint at the configured pitch, then apply zoom.</summary>
        void RebuildOverview(bool snap)
        {
            float pitch = Pitch, zoom = Zoom, aspect = Mathf.Max(Cam.aspect, 1.3f);
            builtPitch = pitch; builtZoom = zoom; builtAspect = Cam.aspect;
            baseRot = Quaternion.Euler(pitch, 0f, 0f);
            float vHalf = overviewFov * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * aspect);
            float distW = frame.extents.x / Mathf.Tan(hHalf);
            float depthOnScreen = frame.extents.z * Mathf.Sin(pitch * Mathf.Deg2Rad) + 0.8f;
            float distD = depthOnScreen / Mathf.Tan(vHalf);
            float dist = (Mathf.Max(distW, distD) * 1.04f + 1.5f) / zoom;
            Distance = dist;
            basePos = frame.center + Vector3.up * 0.5f - (baseRot * Vector3.forward) * dist;
            if (snap) transform.SetPositionAndRotation(basePos, baseRot);
        }

        public void AddTrauma(float amount)
        {
            float scale = (Game.Config != null ? Game.Config.screenShakeScale : 1f) * GameSettings.ScreenShake;
            trauma = Mathf.Clamp01(trauma + amount * scale);
        }

        /// <summary>Negative = zoom in punch.</summary>
        public void FovPunch(float amount) => fovVel += amount * 30f;

        /// <summary>Drop in, tilt toward the back wall, and follow the players around (between rounds).</summary>
        public void BeginFollow(Vector3 alsoLookAt)
        {
            follow = true;
            followBySetting = false; // the game asked for it: stays on whatever the setting says
            lookExtra = alsoLookAt;
            followPos = basePos;
            followRot = baseRot;
            followVel = Vector3.zero;
        }

        /// <summary>
        /// Frame every player: fit their bounding box at the current pitch (zooming out as they spread),
        /// and tilt from flat (players at the back, wall in view) to steep (players at the front, nobody hidden).
        /// </summary>
        void UpdateFollow(float dt)
        {
            Vector3 min = new Vector3(float.MaxValue, 0f, float.MaxValue), max = new Vector3(float.MinValue, 0f, float.MinValue);
            int n = 0;
            foreach (var p in Game.Players)
            {
                if (p == null) continue;
                var q = p.transform.position;
                min = Vector3.Min(min, q); max = Vector3.Max(max, q);
                n++;
            }
            if (n == 0) { min = max = new Vector3(lookExtra.x, 0f, lookExtra.z - 3f); }
            Vector3 c = (min + max) * 0.5f;
            const float pad = 2.2f;
            float ex = (max.x - min.x) * 0.5f + pad, ez = (max.z - min.z) * 0.5f + pad;

            var cfg = Game.Config;
            float zoom = cfg != null ? Mathf.Max(0.1f, cfg.intermissionCameraZoom) : 1f;
            float back = cfg != null ? cfg.intermissionCameraPitch : 30f;
            float front = cfg != null ? cfg.intermissionCameraPitchFront : 58f;
            // 0 = group at the front of the room, 1 = at the back wall
            float depth01 = Mathf.InverseLerp(frame.min.z + 2f, frame.max.z - 1.5f, c.z);
            float pitch = Mathf.Lerp(front, back, Mathf.SmoothStep(0f, 1f, depth01));

            // when the group is at the back, ease the aim up toward the wall monitor
            Vector3 look = c + Vector3.up * 0.8f;
            look = Vector3.Lerp(look, new Vector3(look.x, lookExtra.y * 0.55f, Mathf.Max(look.z, lookExtra.z - 1.5f)), 0.35f * depth01);

            // fit width and (pitch-projected) depth, plus a little height for the critters themselves
            float fov = overviewFov + 4f;
            float vHalf = fov * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * Mathf.Max(Cam.aspect, 1.2f));
            float s = Mathf.Sin(pitch * Mathf.Deg2Rad), k = Mathf.Cos(pitch * Mathf.Deg2Rad);
            float fitW = ex / Mathf.Tan(hHalf);
            float fitD = (ez * s + 1.2f * k) / Mathf.Tan(vHalf);
            float dist = Mathf.Clamp(Mathf.Max(fitW, fitD, 9f) / zoom, 6f, Distance * 1.2f);
            var rot = Quaternion.Euler(pitch, 0f, 0f);
            Vector3 want = look - rot * Vector3.forward * dist;
            followPos = Vector3.SmoothDamp(followPos, want, ref followVel, 0.45f, Mathf.Infinity, dt);
            followRot = Quaternion.Slerp(followRot, rot, 1f - Mathf.Exp(-3f * dt));
            baseFov = Mathf.Lerp(baseFov, overviewFov + 4f, 1f - Mathf.Exp(-2f * dt));
        }

        void LateUpdate()
        {
            if (Cam == null) return;
            float dt = Time.unscaledDeltaTime;
            // the Follow camera setting: follow during rounds too (and hand back the fixed view if it's switched off)
            if (GameSettings.FollowCamera && !follow)
            {
                BeginFollow(Game.Monitor != null ? Game.Monitor.Center : frame.center + Vector3.up * 3f);
                followBySetting = true;
            }
            else if (!GameSettings.FollowCamera && followBySetting) { follow = followBySetting = false; baseFov = overviewFov; }
            if (follow) UpdateFollow(dt);
            else if (!Mathf.Approximately(Pitch, builtPitch) || !Mathf.Approximately(Zoom, builtZoom) || !Mathf.Approximately(Cam.aspect, builtAspect))
                RebuildOverview(false); // live-tune from the config asset

            Vector3 anchor = follow ? followPos : basePos;
            Quaternion anchorRot = follow ? followRot : baseRot;
            trauma = Mathf.Max(0f, trauma - dt * 1.6f);
            float shake = trauma * trauma;
            float t = Time.unscaledTime * 28f;
            Vector3 off = new Vector3(
                (Mathf.PerlinNoise(seed, t) - 0.5f) * 2f,
                (Mathf.PerlinNoise(seed + 10f, t) - 0.5f) * 2f,
                0f) * shake * 0.55f;
            float roll = (Mathf.PerlinNoise(seed + 20f, t) - 0.5f) * 2f * shake * 3f;

            float acc = -220f * fovOffset - 18f * fovVel;
            fovVel += acc * dt;
            fovOffset += fovVel * dt;

            transform.position = anchor + anchorRot * off;
            transform.rotation = anchorRot * Quaternion.Euler(0f, 0f, roll);
            Cam.fieldOfView = baseFov + fovOffset;
        }
    }
}
