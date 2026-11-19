using UnityEngine;

namespace UnderPressure
{
    /// <summary>Code-built particle effects. Cartoon style: opaque blobs that shrink away.</summary>
    public static class Fx
    {
        public static ParticleSystem Make(Vector3 pos, string name)
        {
            var go = new GameObject("FX_" + name);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.6f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mats.Particle(false);
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        /// <summary>Generic burst. gravity > 0 falls, gravity < 0 floats up.</summary>
        public static void Burst(Vector3 pos, Color a, Color b, int count, float speed, float size, float life, float gravity,
            bool glow = false, ParticleSystemShapeType shape = ParticleSystemShapeType.Sphere, float radius = 0.1f, Quaternion? rot = null)
        {
            var ps = Make(pos, "Burst");
            if (rot.HasValue) ps.transform.rotation = rot.Value;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.45f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.gravityModifier = gravity;
            main.maxParticles = Mathf.Max(count, 1);
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = shape;
            sh.radius = radius;
            if (shape == ParticleSystemShapeType.Cone) sh.angle = 35f;
            if (glow) ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = Mats.Particle(true);
            ps.Play();
            Object.Destroy(ps.gameObject, life + 0.5f);
        }

        /// <summary>Upward fountain (hemisphere pointing up).</summary>
        public static void Fountain(Vector3 pos, Color a, Color b, int count, float speed, float size, float life, float gravity, bool glow = false) =>
            Burst(pos, a, b, count, speed, size, life, gravity, glow, ParticleSystemShapeType.Hemisphere, 0.15f, Quaternion.Euler(-90f, 0f, 0f));

        public static void Puff(Vector3 pos, Color c, float size) =>
            Burst(pos, c, Color.Lerp(c, Color.gray, 0.3f), 3, 0.6f, size, 0.6f, -0.15f);

        /// <summary>Flat dust ring along the ground — for slams and landings.</summary>
        public static void Dust(Vector3 pos, float radius, int count = 14)
        {
            var ps = Make(pos + Vector3.up * 0.05f, "Dust");
            ps.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // circle shape lies in XY -> rotate to XZ
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.92f, 0.85f), new Color(0.8f, 0.78f, 0.72f));
            main.gravityModifier = -0.05f;
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = radius;
            sh.radiusThickness = 0f; // emit from the edge
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.limit = 0.4f;
            lim.dampen = 0.12f;
            ps.Play();
            Object.Destroy(ps.gameObject, 1.2f);
        }

        /// <summary>Hot little sparks for metal-on-metal latching.</summary>
        public static void Sparks(Vector3 pos, int count = 16, float speed = 6f) =>
            Burst(pos, new Color(1f, 0.95f, 0.5f), new Color(1f, 0.6f, 0.15f), count, speed, 0.12f, 0.35f, 1.6f, true);

        public static void Confetti(Vector3 pos, int count, float speed)
        {
            Burst(pos, new Color(1f, 0.35f, 0.5f), new Color(1f, 0.9f, 0.2f), count / 2, speed, 0.22f, 1.3f, 0.9f, false,
                ParticleSystemShapeType.Hemisphere, 0.3f, Quaternion.Euler(-90f, 0f, 0f));
            Burst(pos, new Color(0.3f, 0.7f, 1f), new Color(0.5f, 1f, 0.5f), count / 2, speed, 0.22f, 1.3f, 0.9f, false,
                ParticleSystemShapeType.Hemisphere, 0.3f, Quaternion.Euler(-90f, 0f, 0f));
        }

        /// <summary>A continuous emitter (e.g. droplets trailing the liquid front). Caller moves it and destroys it.</summary>
        public static ParticleSystem Trail(Color a, Color b, float rate, float size)
        {
            var ps = Make(Vector3.zero, "Trail");
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.gravityModifier = 0.6f;
            main.maxParticles = 200;
            var em = ps.emission;
            em.rateOverTime = rate;
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.12f;
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = Mats.Particle(true);
            ps.Play();
            return ps;
        }
    }
}
