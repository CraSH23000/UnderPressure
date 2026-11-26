using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// The tension riser: pipes building up pressure. A swarm of hollow pulse voices starts as a low, drifting rumble
    /// and, as the spill gets closer, rises while it converges on one tense, unresolved minor chord (with a rub in
    /// it). A resonant filter opens and its resonance climbs until it whistles like a kettle, and each voice bubbles
    /// harder as it boils. It never resolves or holds: at the spill it cuts into a splash (a burst of spray sweeping
    /// down, a thump, and bubbles), and the splash is the resolution. Laying pipe lowers the tension, so it sinks
    /// back toward the rumble. Its own piece, not tied to the song.
    /// Generated live on the audio thread; the main thread only sets the tension and level, and triggers the splash.
    /// </summary>
    public class PanicRiser : MonoBehaviour
    {
        // the chord, in semitones from the root: a minor 9th with an added 11th (the 14/15 rub keeps it from resolving)
        static readonly int[] Chord = { -12, -5, 0, 3, 7, 10, 12, 14, 15, 19, 22, 24 };
        const int Voices = 12;
        const int Bubbles = 8;

        // set on the main thread, read on the audio thread
        volatile float tTension, tGain, tPitch = 1f, splashLevel;
        volatile int tRoot = 57, splashRequests;
        int sr = 48000;

        // audio thread state
        readonly float[] startHz = new float[Voices], wanderRate = new float[Voices], wanderPhase = new float[Voices];
        readonly float[] bubbleRate = new float[Voices], bubblePhase = new float[Voices];
        readonly double[] phase = new double[Voices];
        readonly float[] curHz = new float[Voices], toHz = new float[Voices], curAmp = new float[Voices], toAmp = new float[Voices];
        float tension, gain, pitchMul = 1f;
        float low, band;            // the resonant filter (state-variable)
        double clock;
        uint rng = 0x9E3779B9u;

        // the splash
        int splashesDone;
        float splashT = -1f, splashAmp, sLow, sBand, thumpPh;
        readonly float[] bubbleAt = new float[Bubbles], bubbleHz = new float[Bubbles], bubbleAmp = new float[Bubbles];
        readonly double[] bubblePh = new double[Bubbles];

        public static PanicRiser Create(Transform parent)
        {
            var go = new GameObject("PanicRiser");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<PanicRiser>();
            r.sr = AudioSettings.outputSampleRate;
            r.Seed();
            var src = go.AddComponent<AudioSource>(); // no clip: OnAudioFilterRead fills it
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 1f;
            src.Play();
            return r;
        }

        void Seed()
        {
            var rnd = new System.Random(7);
            for (int i = 0; i < Voices; i++)
            {
                startHz[i] = 60f * Mathf.Pow(2f, (float)rnd.NextDouble()); // a low rumble: 60-120 Hz
                wanderRate[i] = 0.08f + 0.3f * (float)rnd.NextDouble();
                wanderPhase[i] = (float)rnd.NextDouble() * 6.283f;
                bubbleRate[i] = 3f + 6f * (float)rnd.NextDouble();
                bubblePhase[i] = (float)rnd.NextDouble() * 6.283f;
                phase[i] = rnd.NextDouble();
                curHz[i] = startHz[i];
                curAmp[i] = 1f;
            }
        }

        float Rand()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 16777216f;
        }

        /// <summary>Drive it. tension 0-1 (1 = spilling now); volume is the overall level; pitchMul follows the
        /// music's pitch; rootMidi is the chord's root (57 = A3).</summary>
        public void Set(float tension01, float volume, float pitchMul, int rootMidi)
        {
            tTension = Mathf.Clamp01(tension01);
            tGain = Mathf.Max(0f, volume);
            tPitch = Mathf.Max(0.1f, pitchMul);
            tRoot = rootMidi;
        }

        /// <summary>The spill: the riser cuts and a splash plays, as big as the tension was (0-1) at this level.</summary>
        public void Splash(float strength, float volume)
        {
            splashLevel = Mathf.Clamp01(strength) * Mathf.Max(0f, volume);
            splashRequests++;
        }

        void StartSplash()
        {
            splashT = 0f;
            splashAmp = splashLevel;
            sLow = sBand = 0f;
            for (int b = 0; b < Bubbles; b++)
            {
                bubbleAt[b] = 0.04f + Rand() * 0.9f;    // bubbles pop up over the first second
                bubbleHz[b] = 280f + Rand() * 520f;
                bubbleAmp[b] = 0.25f + Rand() * 0.35f;
                bubblePh[b] = 0.0;
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            int n = data.Length / channels;
            float s = sr;
            float blockSec = n / s;
            float k = 1f - Mathf.Exp(-blockSec / 0.05f); // ~50 ms smoothing, block to block

            if (splashRequests != splashesDone) { splashesDone = splashRequests; StartSplash(); }

            float tensionFrom = tension, gainFrom = gain;
            tension += (tTension - tension) * k;
            pitchMul += (tPitch - pitchMul) * k;
            float t = tension;
            gain += (tGain * Mathf.Pow(t, 1.5f) - gain) * k;

            // rising while converging: from the rumble to the chord, getting there right at the spill
            float glide = Mathf.SmoothStep(0f, 1f, Mathf.Pow(t, 0.9f));
            float wanderAmt = 3f * (1f - glide);
            clock += blockSec;
            float rootHz = 440f * Mathf.Pow(2f, (tRoot - 69) / 12f);
            float bubbleDepth = 0.15f + 0.55f * t; // simmer to boil
            for (int v = 0; v < Voices; v++)
            {
                float wander = wanderAmt * Mathf.Sin((float)(clock * wanderRate[v] * 6.283) + wanderPhase[v]);
                float from = Mathf.Log(startHz[v]) + wander / 12f * 0.6931f;
                float to = Mathf.Log(rootHz) + Chord[v] / 12f * 0.6931f;
                toHz[v] = Mathf.Exp(Mathf.Lerp(from, to, glide)) * pitchMul;
                float rate = bubbleRate[v] * (1f + 1.5f * t);
                toAmp[v] = 1f - bubbleDepth * (0.5f + 0.5f * Mathf.Sin((float)(clock * rate * 6.283) + bubblePhase[v]));
            }

            // resonant low-pass: dark and soft to bright and whistling
            float fcFrom = 180f * Mathf.Pow(5000f / 180f, tensionFrom), fcTo = 180f * Mathf.Pow(5000f / 180f, t);
            float qFrom = 1f / (0.8f + 3.5f * tensionFrom * tensionFrom), qTo = 1f / (0.8f + 3.5f * t * t);

            for (int i = 0; i < n; i++)
            {
                float u = (i + 1) / (float)n;
                float sum = 0f;
                for (int v = 0; v < Voices; v++)
                {
                    float hz = curHz[v] + (toHz[v] - curHz[v]) * u;
                    phase[v] += hz / s;
                    if (phase[v] >= 1.0) phase[v] -= 1.0;
                    float pulse = phase[v] < 0.3 ? 0.7f : -0.3f; // a hollow 30% pulse (DC-free)
                    sum += pulse * (curAmp[v] + (toAmp[v] - curAmp[v]) * u);
                }
                float x = sum / Voices * 2f;

                float fc = Mathf.Min(fcFrom + (fcTo - fcFrom) * u, s * 0.14f);
                float f = 2f * Mathf.Sin(Mathf.PI * fc / s);
                float q = qFrom + (qTo - qFrom) * u;
                low += f * band;
                float high = x - low - q * band;
                band += f * high;
                float g = gainFrom + (gain - gainFrom) * u;
                float y = low * g;

                if (splashT >= 0f) y += SplashSample(s);
                for (int c = 0; c < channels; c++) data[i * channels + c] += y;
            }
            for (int v = 0; v < Voices; v++) { curHz[v] = toHz[v]; curAmp[v] = toAmp[v]; }
        }

        /// <summary>One sample of the splash: spray (noise through a band-pass sweeping down), a thump, bubbles.</summary>
        float SplashSample(float s)
        {
            float st = splashT;
            splashT += 1f / s;
            if (st > 1.6f) { splashT = -1f; return 0f; }

            // spray: white noise through a band-pass that sweeps from a hiss down to a slosh
            float wn = Rand() * 2f - 1f;
            float fc = Mathf.Min(350f + 6000f * Mathf.Exp(-st / 0.25f), s * 0.14f);
            float f = 2f * Mathf.Sin(Mathf.PI * fc / s);
            sLow += f * sBand;
            float hi = wn - sLow - 0.8f * sBand;
            sBand += f * hi;
            float spray = sBand * Mathf.Min(1f, st / 0.004f) * Mathf.Exp(-st / 0.35f);

            // the thump of it hitting the floor
            thumpPh += Mathf.Lerp(70f, 40f, Mathf.Clamp01(st / 0.15f)) / s;
            if (thumpPh >= 1f) thumpPh -= 1f;
            float thump = Mathf.Sin(thumpPh * 6.283f) * Mathf.Exp(-st / 0.12f) * 0.8f;

            // bubbles: short sine blips that rise in pitch, popping up over the first second
            float bub = 0f;
            for (int b = 0; b < Bubbles; b++)
            {
                float dt = st - bubbleAt[b];
                if (dt < 0f || dt > 0.09f) continue;
                float hz = bubbleHz[b] * (1f + dt / 0.09f * 1.4f);
                bubblePh[b] += hz / s;
                bub += (float)System.Math.Sin(bubblePh[b] * 6.283) * Mathf.Exp(-dt / 0.03f) * bubbleAmp[b];
            }
            return (spray * 1.4f + thump + bub) * splashAmp * 0.6f;
        }
    }
}
