using System;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// Placeholder sound effects synthesised at startup (no audio assets needed).
    /// Swap any of these fields for real AudioClips later — call sites won't change.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        public static Sfx I;

        public AudioClip pickup, thunk, clank, ratchet, pop, bloop, loop, coin, spill, fanfare, tick, whoosh, hop, nope, rotate, splat;
        public AudioClip creak, clunk, land, waste, yoink, alarm, hum, zap, thunder, materialize, moneyTick, moneyTickDown;
        public AudioClip[] grunts;

        AudioSource[] sources;
        AudioSource tickSource; // the money counter ticks up to 30+ times a second: keep it off the shared pool
        int next;
        const int SR = 44100;
        static System.Random rng = new System.Random(7);

        void Awake()
        {
            I = this;
            sources = new AudioSource[16];
            for (int i = 0; i < sources.Length; i++)
                sources[i] = NewSource(transform, "Voice " + i, null);
            tickSource = NewSource(transform, "Tick", null);
            Generate();
        }

        void OnDestroy() { if (I == this) I = null; }

        public static void Play(AudioClip clip, float volume = 1f, float pitch = 1f, float pitchJitter = 0.05f)
        {
            if (I == null || clip == null) return;
            var s = I.sources[I.next];
            I.next = (I.next + 1) % I.sources.Length;
            s.clip = clip;
            s.volume = volume * (Game.Config != null ? Game.Config.sfxVolume : 0.8f);
            s.pitch = pitch * (1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter));
            s.Play();
        }

        // ------------------------------------------------------------------ synthesis

        void Generate()
        {
            pickup = Make("pickup", 0.13f, t => Sweep(t, 0.13f, 380, 950) * Env(t, 0.004f, 0.13f) * 0.5f
                                               + Square(t * 760) * Env(t, 0.002f, 0.05f) * 0.08f);
            thunk = Make("thunk", 0.22f, t => Sweep(t, 0.22f, 160, 60) * Mathf.Exp(-t * 22f) * 0.9f
                                              + Noise() * Mathf.Exp(-t * 70f) * 0.35f);
            clank = Make("clank", 0.45f, t =>
                (Sin(t * 1250) * Mathf.Exp(-t * 11f) * 0.35f +
                 Sin(t * 1873) * Mathf.Exp(-t * 15f) * 0.25f +
                 Sin(t * 2650) * Mathf.Exp(-t * 22f) * 0.2f +
                 Sin(t * 3910) * Mathf.Exp(-t * 35f) * 0.12f +
                 Noise() * Mathf.Exp(-t * 180f) * 0.5f) * 0.9f);
            ratchet = Make("ratchet", 0.05f, t => (Noise() * 0.6f + Sin(t * 2300) * 0.4f) * Mathf.Exp(-t * 110f));
            pop = Make("pop", 0.16f, t => Sweep(t, 0.16f, 260, 1300) * Env(t, 0.002f, 0.16f) * 0.6f
                                          + Noise() * Mathf.Exp(-t * 120f) * 0.3f);
            bloop = Make("bloop", 0.24f, t =>
            {
                float f = Mathf.Lerp(620, 280, t / 0.24f) * (1f + 0.04f * Mathf.Sin(t * 60f));
                return Mathf.Sin(2f * Mathf.PI * f * t) * Env(t, 0.006f, 0.24f) * 0.55f;
            });
            loop = Make("loop", 0.9f, t =>
            {
                float v = 0f;
                float[] notes = { 1046.5f, 1318.5f, 1568f, 2093f };
                for (int i = 0; i < notes.Length; i++)
                {
                    float st = i * 0.07f;
                    if (t < st) continue;
                    float u = t - st;
                    v += (Sin(u * notes[i]) * 0.6f + Sin(u * notes[i] * 2f) * 0.15f) * Mathf.Exp(-u * 5f) * 0.3f;
                }
                return v;
            });
            coin = Make("coin", 0.45f, t =>
            {
                float f = t < 0.07f ? 987.8f : 1318.5f;
                float u = t < 0.07f ? t : t - 0.07f;
                return (Sin(t * f) * 0.7f + Square(t * f) * 0.12f) * Mathf.Exp(-u * 7f) * 0.35f;
            });
            float lp = 0f;
            spill = Make("spill", 1.0f, t =>
            {
                lp += (Noise() - lp) * 0.08f;
                return lp * 2.2f * Env(t, 0.02f, 1.0f) + Sweep(t, 1f, 320, 70) * Mathf.Exp(-t * 4f) * 0.4f;
            });
            fanfare = Make("fanfare", 1.6f, t =>
            {
                float v = 0f;
                float[] arp = { 783.99f, 1046.5f, 1318.5f, 1567.98f };
                for (int i = 0; i < arp.Length; i++)
                {
                    float st = i * 0.1f;
                    if (t < st) continue;
                    float u = t - st;
                    v += (Sin(u * arp[i]) + Square(u * arp[i]) * 0.15f) * Mathf.Exp(-u * 9f) * 0.25f;
                }
                if (t > 0.42f)
                {
                    float u = t - 0.42f;
                    float env = Mathf.Min(1f, u * 30f) * Mathf.Exp(-u * 2.2f);
                    v += (Sin(u * 1046.5f) + Sin(u * 1318.5f) + Sin(u * 1567.98f) + Sin(u * 2093f) * 0.5f) * env * 0.12f;
                }
                return v;
            });
            tick = Make("tick", 0.06f, t => Sin(t * 1400) * Mathf.Exp(-t * 70f) * 0.4f);
            float lp2 = 0f;
            whoosh = Make("whoosh", 0.7f, t =>
            {
                float k = Mathf.Lerp(0.02f, 0.35f, t / 0.7f);
                lp2 += (Noise() - lp2) * k;
                return lp2 * 1.6f * Env(t, 0.25f, 0.7f) + Sweep(t, 0.7f, 90, 240) * Env(t, 0.1f, 0.7f) * 0.3f;
            });
            hop = Make("hop", 0.16f, t => Sweep(t, 0.16f, 300, 680) * Env(t, 0.003f, 0.16f) * 0.4f);
            nope = Make("nope", 0.22f, t => Square(t * 140) * (t < 0.08f || (t > 0.11f && t < 0.19f) ? 1f : 0f) * 0.18f);
            rotate = Make("rotate", 0.08f, t => (Noise() * 0.3f + Sin(t * 1800) * 0.5f) * Mathf.Exp(-t * 60f) * 0.6f);
            GenerateV4();
            float lp3 = 0f;
            splat = Make("splat", 0.35f, t =>
            {
                lp3 += (Noise() - lp3) * 0.25f;
                return lp3 * 1.5f * Mathf.Exp(-t * 12f) + Sweep(t, 0.35f, 220, 90) * Mathf.Exp(-t * 14f) * 0.5f;
            });
        }

        void GenerateV4()
        {
            // Creaking pipe under strain: stick-slip pulses through two metal resonances, wobbling and sagging in pitch.
            creak = FromSamples("creak", Creak(0.55f, 60f, 38f, new[] { 260f, 610f, 1150f }));
            // Dull "that came loose" clunk + a little downward groan.
            clunk = Make("clunk", 0.35f, t => Sweep(t, 0.35f, 120, 55) * Mathf.Exp(-t * 14f) * 0.8f
                                              + Sin(t * 410) * Mathf.Exp(-t * 30f) * 0.25f
                                              + Noise() * Mathf.Exp(-t * 90f) * 0.3f);
            land = Make("land", 0.12f, t => Sweep(t, 0.12f, 140, 70) * Mathf.Exp(-t * 35f) * 0.6f + Noise() * Mathf.Exp(-t * 120f) * 0.15f);
            // Wasted piece: sad descending "bwomp-womp".
            waste = Make("waste", 0.75f, t =>
            {
                float f = t < 0.32f ? Mathf.Lerp(330f, 250f, t / 0.32f) : Mathf.Lerp(235f, 140f, (t - 0.32f) / 0.43f);
                float u = t < 0.32f ? t : t - 0.32f;
                float env = Mathf.Min(1f, u * 40f) * Mathf.Exp(-u * 4f);
                return (Mathf.Sin(2f * Mathf.PI * f * t) * 0.6f + Square(f * t) * 0.15f) * env * 0.5f;
            });
            // two-tone klaxon for the leak warning
            alarm = Make("alarm", 0.9f, t =>
            {
                float f = ((int)(t / 0.15f) & 1) == 0 ? 880f : 660f;
                return (Square(t * f) * 0.25f + Mathf.Sin(2f * Mathf.PI * f * t) * 0.3f) * Env(t, 0.01f, 0.9f) * 0.6f;
            });
            // tesla coil charge-up hum: buzzy 60Hz + harmonics with a tremolo, loopable (exactly 1 s)
            hum = MakeLoop("hum", 1f, t =>
                (Saw(t * 60f) * 0.35f + Sin(t * 120f) * 0.25f + Sin(t * 180f) * 0.15f + Noise() * 0.05f) * (0.75f + 0.25f * Mathf.Sin(t * 2f * Mathf.PI * 8f)));
            // arc crackle
            zap = Make("zap", 0.14f, t => (Noise() * (Mathf.Sin(t * 900f) > 0.2f ? 1f : 0.2f)) * Mathf.Exp(-t * 28f) * 0.7f + Sin(t * 2400) * Mathf.Exp(-t * 60f) * 0.2f);
            // the big one: a crack, then a rumble
            float lpT = 0f;
            thunder = Make("thunder", 2.2f, t =>
            {
                lpT += (Noise() - lpT) * (t < 0.05f ? 0.9f : 0.04f);
                float crack = Noise() * Mathf.Exp(-t * 40f);
                float rumble = lpT * 3.2f * Mathf.Exp(-t * 1.6f) * (0.8f + 0.2f * Mathf.Sin(t * 13f));
                return crack * 0.9f + rumble + Sweep(t, 2.2f, 70, 35) * Mathf.Exp(-t * 2.5f) * 0.4f;
            });
            materialize = Make("materialize", 0.6f, t => Sweep(t, 0.6f, 1600, 200) * Env(t, 0.01f, 0.6f) * 0.25f
                                                          + Noise() * Env(t, 0.002f, 0.25f) * 0.3f);
            yoink = Make("yoink", 0.22f, t => Sweep(t, 0.22f, 1200, 300) * Env(t, 0.003f, 0.22f) * 0.35f + Noise() * Env(t, 0.002f, 0.08f) * 0.2f);

            // NES-style score tally blip: a short 25% pulse wave with a fast decay
            moneyTick = Make("moneyTick", 0.045f, t =>
            {
                float ph = t * 1568f; // G6
                float pulse = (ph - Mathf.Floor(ph)) < 0.25f ? 1f : -1f;
                return pulse * Mathf.Exp(-t * 70f) * 0.22f;
            });
            // spending: a 50% square an octave and a bit lower (C6 sliding down to G5), so it reads as "draining"
            moneyTickDown = Make("moneyTickDown", 0.05f, t =>
            {
                float ph = 1046.5f * t - (1046.5f - 784f) * t * t / (2f * 0.05f);
                float sq = (ph - Mathf.Floor(ph)) < 0.5f ? 1f : -1f;
                return sq * Mathf.Exp(-t * 60f) * 0.18f;
            });

            // Little monster grunts: "hup!", "hnf!", "hah!" — formant-filtered glottal buzz.
            grunts = new[]
            {
                FromSamples("grunt_hup", Voice(0.17f, 250f, 190f, new[] { 650f, 1100f, 2400f }, 0.45f)),
                FromSamples("grunt_hnf", Voice(0.15f, 230f, 170f, new[] { 420f, 900f, 2200f }, 0.6f)),
                FromSamples("grunt_hah", Voice(0.19f, 270f, 200f, new[] { 800f, 1250f, 2600f }, 0.35f)),
            };
        }

        /// <summary>
        /// The money counter's tick, on its own source so rapid ticks never cut off other sounds.
        /// 'spending' uses the lower, falling blip so losing money never sounds like winning it.
        /// </summary>
        public static void PlayTick(float volume, float pitch, bool spending = false)
        {
            var clip = I == null ? null : spending ? I.moneyTickDown : I.moneyTick;
            if (clip == null) return;
            I.tickSource.pitch = pitch;
            I.tickSource.PlayOneShot(clip, volume * (Game.Config != null ? Game.Config.sfxVolume : 0.8f));
        }

        public static AudioClip RandomGrunt() =>
            I == null || I.grunts == null || I.grunts.Length == 0 ? null : I.grunts[UnityEngine.Random.Range(0, I.grunts.Length)];

        /// <summary>Glottal pulse train (pitch glide) through formant resonators, with a breathy onset.</summary>
        static float[] Voice(float dur, float f0Start, float f0End, float[] formants, float breath)
        {
            int n = Mathf.CeilToInt(SR * dur);
            var src = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float f0 = Mathf.Lerp(f0Start, f0End, t / dur) * (1f + 0.03f * Mathf.Sin(t * 70f));
                phase += f0 / SR;
                phase -= Mathf.Floor(phase);
                float saw = 1f - 2f * phase;             // glottal-ish buzz
                float env = Mathf.Min(1f, t / 0.012f) * Mathf.Clamp01((dur - t) / (dur * 0.55f));
                float b = Noise() * breath * Mathf.Exp(-t * 25f);
                src[i] = (saw * 0.6f + b) * env;
            }
            return Resonate(src, formants, new[] { 90f, 120f, 180f }, new[] { 1f, 0.6f, 0.25f }, 0.5f);
        }

        /// <summary>Stick-slip friction pulses (irregular rate) through metallic resonances.</summary>
        static float[] Creak(float dur, float rateStart, float rateEnd, float[] resonances)
        {
            int n = Mathf.CeilToInt(SR * dur);
            var src = new float[n];
            float next = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                if (t >= next)
                {
                    float rate = Mathf.Lerp(rateStart, rateEnd, t / dur) * (0.75f + 0.5f * (float)rng.NextDouble());
                    next = t + 1f / rate;
                    src[i] = 1f;
                }
                float env = Mathf.Min(1f, t / 0.04f) * Mathf.Clamp01((dur - t) / 0.12f) * (0.7f + 0.3f * Mathf.Sin(t * 23f));
                src[i] = (src[i] + Noise() * 0.04f) * env;
            }
            return Resonate(src, resonances, new[] { 25f, 40f, 70f }, new[] { 1f, 0.7f, 0.35f }, 0.9f);
        }

        /// <summary>Sum of two-pole resonators (bandpass-ish), then normalised to 'gain'.</summary>
        static float[] Resonate(float[] x, float[] freqs, float[] bandwidths, float[] amps, float gain)
        {
            int n = x.Length;
            var y = new float[n];
            for (int k = 0; k < freqs.Length; k++)
            {
                float r = Mathf.Exp(-Mathf.PI * bandwidths[k] / SR);
                float a1 = -2f * r * Mathf.Cos(2f * Mathf.PI * freqs[k] / SR);
                float a2 = r * r;
                float y1 = 0f, y2 = 0f;
                for (int i = 0; i < n; i++)
                {
                    float v = (1f - r) * x[i] - a1 * y1 - a2 * y2;
                    y2 = y1; y1 = v;
                    y[i] += v * amps[k];
                }
            }
            float peak = 0.0001f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(y[i]));
            for (int i = 0; i < n; i++) y[i] = y[i] / peak * gain;
            return y;
        }

        static AudioClip FromSamples(string name, float[] data)
        {
            int n = data.Length;
            for (int i = 0; i < n; i++)
            {
                float tail = Mathf.Clamp01((n - i) / (SR * 0.005f));
                data[i] = Mathf.Clamp(data[i] * tail, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, SR, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Like Make but without the fade-out tail, so it loops cleanly.</summary>
        static AudioClip MakeLoop(string name, float dur, Func<float, float> f)
        {
            int n = Mathf.CeilToInt(SR * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)SR), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, SR, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Saw(float phaseCycles) => 2f * (phaseCycles - Mathf.Floor(phaseCycles)) - 1f;

        /// <summary>A dedicated looping source (e.g. the teleporter hum). Caller controls volume/pitch and stops it.</summary>
        public static AudioSource Loop(AudioClip clip)
        {
            if (I == null || clip == null) return null;
            var s = NewSource(I.transform, clip.name, clip);
            s.loop = true;
            s.volume = 0f;
            s.Play();
            return s;
        }

        /// <summary>
        /// A 2D AudioSource on its own child object (destroy its gameObject when done). Built while the child is
        /// inactive: an AudioSource added to an active object wakes up with the default playOnAwake and no clip,
        /// which Unity warns about ("Only custom filters can be played").
        /// </summary>
        public static AudioSource NewSource(Transform parent, string name, AudioClip clip)
        {
            var go = new GameObject(string.IsNullOrEmpty(name) ? "Source" : name);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            go.SetActive(true);
            return s;
        }

        static AudioClip Make(string name, float dur, Func<float, float> f)
        {
            int n = Mathf.CeilToInt(SR * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = f(i / (float)SR);
                // soft fade-out on the last 5ms to avoid clicks
                float tail = Mathf.Clamp01((n - i) / (SR * 0.005f));
                data[i] = Mathf.Clamp(v * tail, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, SR, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Sin(float cyclesTimesT) => Mathf.Sin(2f * Mathf.PI * cyclesTimesT);
        static float Square(float phaseCycles) => (phaseCycles - Mathf.Floor(phaseCycles)) < 0.5f ? 1f : -1f;
        static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
        static float Env(float t, float attack, float dur) =>
            t < attack ? t / attack : Mathf.Clamp01(1f - (t - attack) / Mathf.Max(0.0001f, dur - attack)) * Mathf.Clamp01(1f - (t - attack) / Mathf.Max(0.0001f, dur - attack));

        /// <summary>Linear frequency sweep with correct phase integration.</summary>
        static float Sweep(float t, float dur, float f0, float f1) =>
            Mathf.Sin(2f * Mathf.PI * (f0 * t + (f1 - f0) * t * t / (2f * dur)));
    }
}
