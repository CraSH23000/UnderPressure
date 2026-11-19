using System;
using System.Collections;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>Hosts coroutines and owns Time.timeScale (hit-stop + slow-mo).</summary>
    public class JuiceHost : MonoBehaviour
    {
        public static JuiceHost Instance;

        float hitStopUntil;
        public bool Paused;
        float slowMoScale = 1f, slowMoUntil;

        void Awake() { Instance = this; Time.timeScale = 1f; }
        void OnDestroy() { if (Instance == this) { Instance = null; Time.timeScale = 1f; } }

        public void HitStop(float seconds)
        {
            if (Game.Config != null && !Game.Config.hitStop) return;
            hitStopUntil = Mathf.Max(hitStopUntil, Time.unscaledTime + seconds);
        }

        public void SlowMo(float scale, float seconds)
        {
            slowMoScale = scale;
            slowMoUntil = Time.unscaledTime + seconds;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            float target = now < slowMoUntil ? slowMoScale : 1f;
            Time.timeScale = Paused || now < hitStopUntil ? 0f : target;
        }
    }

    public static class Juice
    {
        public static Coroutine Run(IEnumerator routine) =>
            JuiceHost.Instance != null ? JuiceHost.Instance.StartCoroutine(routine) : null;

        public static void Delay(float seconds, Action action)
        {
            if (seconds <= 0f) { action(); return; }
            Run(DelayCo(seconds, action));
        }

        static IEnumerator DelayCo(float s, Action a)
        {
            yield return new WaitForSeconds(s);
            a();
        }

        public static void HitStop(float seconds) => JuiceHost.Instance?.HitStop(seconds);
        public static void SlowMo(float scale, float seconds) => JuiceHost.Instance?.SlowMo(scale, seconds);
        public static void Shake(float trauma) => Game.Cam?.AddTrauma(trauma);

        /// <summary>Tween a transform's localScale with an easing curve. Safe if the transform dies mid-tween.</summary>
        public static void ScaleTo(Transform t, Vector3 from, Vector3 to, float duration, Func<float, float> ease, Action done = null)
        {
            if (t == null) return;
            Run(ScaleCo(t, from, to, duration, ease, done));
        }

        static IEnumerator ScaleCo(Transform t, Vector3 from, Vector3 to, float d, Func<float, float> ease, Action done)
        {
            float e = 0f;
            while (e < d)
            {
                if (t == null) yield break;
                e += Time.deltaTime;
                t.localScale = Vector3.LerpUnclamped(from, to, ease(Mathf.Clamp01(e / d)));
                yield return null;
            }
            if (t == null) yield break;
            t.localScale = to;
            done?.Invoke();
        }

        // ---- Easing ----
        public static float OutBack(float t)
        {
            const float c1 = 1.9f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
        public static float InBack(float t)
        {
            const float c1 = 1.7f, c3 = c1 + 1f;
            return c3 * t * t * t - c1 * t * t;
        }
        public static float OutCubic(float t) { float u = 1f - t; return 1f - u * u * u; }
        public static float InCubic(float t) => t * t * t;
        public static float OutElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * (2f * Mathf.PI / 3f)) + 1f;
        }
    }

    /// <summary>
    /// Spring-driven squash & stretch on a transform's localScale. Kick it and it wobbles back.
    /// Uses scaled time, so hit-stop freezes it mid-squash (that's the point).
    /// </summary>
    public class Springy : MonoBehaviour
    {
        public float stiffness = 380f;
        public float damping = 15f;
        public Vector3 baseScale = Vector3.one;
        [NonSerialized] public float pulse; // extra uniform scale (e.g. highlight breathing)

        Vector3 currentBase = Vector3.one;
        Vector3 disp, vel;
        bool initialised;

        public void SetBase(float s, bool instant = false)
        {
            baseScale = Vector3.one * s;
            if (instant || !initialised) { currentBase = baseScale; initialised = true; }
        }

        /// <summary>Positive = squash (wide & short), negative = stretch (thin & tall).</summary>
        public void Squash(float amount) => vel += new Vector3(amount, -amount, amount) * 12f;
        public void Kick(Vector3 velocity) => vel += velocity;
        public void Punch(float amount) => vel += Vector3.one * amount * 12f;

        void Update()
        {
            if (!initialised) { currentBase = baseScale; initialised = true; }
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                currentBase = Vector3.Lerp(currentBase, baseScale, 1f - Mathf.Exp(-16f * dt));
                int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
                float h = dt / steps;
                for (int i = 0; i < steps; i++)
                {
                    Vector3 acc = -stiffness * disp - damping * vel;
                    vel += acc * h;
                    disp += vel * h;
                }
            }
            Vector3 s = Vector3.Scale(currentBase, Vector3.one + disp) * (1f + pulse);
            transform.localScale = Vector3.Max(s, Vector3.one * 0.02f);
        }
    }
}
