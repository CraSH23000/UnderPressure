using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// Original placeholder soundtrack: a jaunty 80s-arcade loop in A harmonic minor (pulse lead, square arps,
    /// triangle bass, synth drums), rendered in code at startup. Two synced stems:
    ///   base = bass + arps + drums (always on), lead = melody + harmony (fades in when the liquid flows).
    /// When the liquid spills, the music "tape-stops". Grime turns it into a worn VHS: wow/flutter, hiss and
    /// crackle, skips (a moment repeats), dropouts and tracking warbles.
    /// Swap the two clips for real music later; the state-driven mixing below still applies.
    /// </summary>
    public class Music : MonoBehaviour
    {
        const int SR = 44100;
        const float BPM = 136f;
        // render settings for the normal music (the panic loop has its own)
        float renderSpeed = 1f, renderSemitones = 0f;
        float pendingSince = -1f;
        float Sixteenth => 60f / (BPM * renderSpeed) / 4f;
        float MidiT(int m) => 440f * Mathf.Pow(2f, (m + renderSemitones - 69) / 12f);

        public static Music Instance;
        bool cut;
        /// <summary>Hard cut to silence (the teleporter strike). Resume() brings it back with a fade-in.</summary>
        public void Cut()
        {
            cut = true;
            baseVol = leadVol = panicVol = 0f;
            foreach (var m in music) m.volume = 0f;
            panicSrc.volume = 0f;
            staticSrc.volume = 0f;
        }
        public void Resume() => cut = false;
        void OnDestroy() { if (Instance == this) Instance = null; }

        AudioSource baseSrc, leadSrc, harmSrc, staticSrc, panicSrc;
        AudioSource[] music;
        float baseVol, leadVol, panicVol, pitch = 1f, overTime = -1f, tempo = 1f;
        float tempoTarget = 1f, tempoChangeTime;
        bool paused;

        // VHS damage state
        float vhsClock, nextSkip = 6f, nextDropout = 4f, nextWarble = 3f;
        int stutterLeft; float stutterTimer; int stutterPos;
        float dropoutT = -1f, dropoutLen, warbleT = -1f, staticBurst;
        readonly System.Random vrng = new System.Random(5);

        void Awake()
        {
            Instance = this;
            ReadRenderSettings(out renderSpeed, out renderSemitones);
            BuildClips(out var baseClip, out var leadClip, out var harmClip);
            baseSrc = MakeSource(baseClip);
            leadSrc = MakeSource(leadClip);
            harmSrc = MakeSource(harmClip);
            music = new[] { baseSrc, leadSrc, harmSrc };
            staticSrc = MakeSource(BuildStatic());
            panicSrc = MakeSource(BuildPanic());
            double start = AudioSettings.dspTime + 0.2;
            foreach (var m in music) m.PlayScheduled(start);
            staticSrc.Play();
            panicSrc.Play();
        }

        AudioSource MakeSource(AudioClip clip)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.volume = 0f;
            return s;
        }

        float R() => (float)vrng.NextDouble();

        static void ReadRenderSettings(out float speed, out float semitones)
        {
            var cfg = Game.Config;
            speed = cfg != null ? Mathf.Clamp(cfg.musicSpeed, 0.25f, 3f) : 0.84f;
            semitones = cfg != null ? cfg.musicPitchSemitones : -3f;
        }

        /// <summary>
        /// Pitch and speed are independent because the music is re-generated (notes transposed, tempo changed)
        /// rather than played faster/slower. When either setting changes, rebuild once it settles,
        /// keeping our place in the song.
        /// </summary>
        void CheckRerender()
        {
            ReadRenderSettings(out float speed, out float semis);
            bool changed = Mathf.Abs(speed - renderSpeed) > 0.001f || Mathf.Abs(semis - renderSemitones) > 0.001f;
            if (!changed) { pendingSince = -1f; return; }
            if (pendingSince < 0f) { pendingSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - pendingSince < 0.4f) return; // wait for the slider to stop moving
            pendingSince = -1f;

            float progress = baseSrc.clip != null ? baseSrc.timeSamples / (float)baseSrc.clip.samples : 0f;
            renderSpeed = speed;
            renderSemitones = semis;
            BuildClips(out var b, out var l, out var h);
            var clips = new[] { b, l, h };
            for (int i = 0; i < music.Length; i++)
            {
                var old = music[i].clip;
                music[i].Stop();
                music[i].clip = clips[i];
                music[i].timeSamples = Mathf.Clamp((int)(progress * clips[i].samples), 0, clips[i].samples - 1);
                if (old != null) Destroy(old);
            }
            foreach (var m in music) m.Play();
        }

        void Update()
        {
            var gm = Game.Manager;
            var cfg = Game.Config;
            float master = (cfg != null ? cfg.musicVolume : 0.55f) * GameSettings.MusicVolume;
            float leadMix = cfg != null ? cfg.musicLeadVolume : 0.7f;
            float harmMix = cfg != null ? cfg.musicHarmonyVolume : 0.5f;
            float dt = Time.unscaledDeltaTime;

            bool shouldPause = gm != null && gm.Paused;
            if (shouldPause != paused)
            {
                paused = shouldPause;
                foreach (var m in music) { if (paused) m.Pause(); else m.UnPause(); }
                if (paused) { staticSrc.Pause(); panicSrc.Pause(); } else { staticSrc.UnPause(); panicSrc.UnPause(); }
            }
            if (paused || cut) return;

            float targetBase = 0.6f, targetLead = 0f, targetPitch = 1f, targetPanic = 0f;
            bool tapeStop = false;
            bool panic = gm != null && gm.Panic && gm.Current == GameManager.State.Flowing;
            if (gm != null)
            {
                bool anyone = Game.Players.Count > 0;
                switch (gm.Current)
                {
                    case GameManager.State.Countdown:
                        targetBase = anyone ? 1f : 0.55f;
                        // the melody starts creeping in during the last seconds of the countdown
                        targetLead = anyone ? Mathf.Clamp01(1f - gm.CountdownRemaining / 8f) * 0.5f : 0f;
                        overTime = -1f;
                        break;
                    case GameManager.State.Lobby:
                        // the base: just the groove, no melody, no tape stop
                        targetBase = anyone ? 1f : 0.55f;
                        targetLead = 0f;
                        overTime = -1f;
                        break;
                    case GameManager.State.Flowing:
                        targetBase = panic ? 0f : 1f;
                        targetLead = panic ? 0f : 1f;
                        targetPanic = panic ? 1f : 0f;
                        overTime = -1f;
                        break;
                    default:
                        if (overTime < 0f) overTime = 0f;
                        overTime += dt;
                        if (overTime < 1.0f)
                        {
                            // tape stop on the spill
                            tapeStop = true;
                            pitch = Mathf.Lerp(1f, 0.35f, overTime);
                            targetBase = 1f - overTime;
                            targetLead = 1f - overTime;
                        }
                        else
                        {
                            // off the clock: back to the pre-flow groove (bass, arps, drums), no melody or harmony
                            targetBase = Game.Players.Count > 0 ? 1f : 0.55f;
                            targetLead = 0f;
                            if (overTime < 1.1f) pitch = 1f;
                        }
                        break;
                }
            }

            // panic swaps in fast; everything else drifts
            bool swapping = panic || panicVol > 0.01f;
            if (panic && panicVol < 0.01f) panicSrc.timeSamples = 0; // start the alarm loop from the top
            baseVol = Mathf.MoveTowards(baseVol, targetBase, dt * (swapping ? 3f : 0.8f));
            leadVol = Mathf.MoveTowards(leadVol, targetLead, dt * (swapping ? 3f : 0.6f));
            panicVol = Mathf.MoveTowards(panicVol, tapeStop ? Mathf.Min(panicVol, 1f - overTime) : targetPanic, dt * (panic ? 5f : 1.5f));
            if (!tapeStop) pitch = Mathf.MoveTowards(pitch, targetPitch, dt * 0.5f);

            // the flow valve speeds the music up (or down), partway so 3x isn't chipmunks
            float follow = cfg != null ? cfg.musicTempoFollowsFlow : 0.3f;
            float flowMul = Game.Flow != null ? Game.Flow.SpeedMultiplier : 1f;
            float targetTempo = gm != null && !gm.IsOver ? Mathf.Max(0.5f, 1f + (flowMul - 1f) * follow) : 1f;
            // ramp: starts at a gentle rate, then accelerates exponentially so big jumps land quickly
            if (Mathf.Abs(targetTempo - tempoTarget) > 0.001f) { tempoTarget = targetTempo; tempoChangeTime = 0f; }
            tempoChangeTime += dt;
            float rate = 0.6f * Mathf.Exp(2.5f * tempoChangeTime);
            tempo = Mathf.MoveTowards(tempo, tempoTarget, dt * rate);

            // ---- worn VHS (scales with grime) ----
            float vhs = Theme.Grime * (cfg != null ? cfg.musicVhsAmount : 1f);
            float vhsPitch = 1f, vhsGain = 1f;
            if (vhs > 0.01f && !tapeStop) UpdateVhs(dt, vhs, out vhsPitch, out vhsGain);
            else staticBurst = 0f;

            float p = pitch * vhsPitch * tempo;
            baseSrc.volume = baseVol * master * vhsGain;
            leadSrc.volume = leadVol * leadMix * master * vhsGain;
            harmSrc.volume = leadVol * harmMix * master * vhsGain;
            // pitch and speed of the normal music are baked into its clips (see ReadRenderSettings)
            foreach (var m in music) m.pitch = p;
            CheckRerender();
            panicSrc.pitch = p;
            float panicMix = cfg != null ? cfg.musicPanicVolume : 1.4f;
            panicSrc.volume = Mathf.Min(1f, panicVol * master * vhsGain * panicMix);
            staticSrc.volume = master * (vhs * 0.05f + staticBurst * 0.3f) * (gm != null && gm.IsOver && overTime > 1f ? 0.5f : 1f);
        }

        void UpdateVhs(float dt, float v, out float pitchMul, out float gain)
        {
            vhsClock += dt;
            float t = vhsClock;
            // wow (slow drift) + flutter (fast shimmer)
            pitchMul = 1f + v * (0.014f * Mathf.Sin(t * 2f * Mathf.PI * 0.45f) + 0.004f * Mathf.Sin(t * 2f * Mathf.PI * 6.3f + 1f));
            gain = 1f;
            staticBurst = Mathf.MoveTowards(staticBurst, 0f, dt * 3f);

            // tracking warble: a sudden sag in pitch
            nextWarble -= dt;
            if (nextWarble <= 0f) { warbleT = 0f; nextWarble = Mathf.Lerp(18f, 3.5f, v) * (0.5f + R()); }
            if (warbleT >= 0f)
            {
                warbleT += dt / 0.4f;
                pitchMul *= 1f - 0.08f * v * Mathf.Sin(Mathf.PI * Mathf.Clamp01(warbleT));
                if (warbleT >= 1f) warbleT = -1f;
            }

            // dropout: the signal nearly vanishes for a beat
            nextDropout -= dt;
            if (nextDropout <= 0f && v > 0.2f)
            {
                dropoutT = 0f; dropoutLen = 0.06f + R() * 0.18f;
                nextDropout = Mathf.Lerp(20f, 3f, v) * (0.5f + R());
                staticBurst = 1f;
            }
            if (dropoutT >= 0f)
            {
                dropoutT += dt;
                gain = 0.12f;
                if (dropoutT >= dropoutLen) dropoutT = -1f;
            }

            // skip: a moment of tape repeats a few times
            nextSkip -= dt;
            if (nextSkip <= 0f && v > 0.25f && stutterLeft == 0)
            {
                stutterLeft = 1 + vrng.Next(3);
                stutterPos = panicVol > 0.5f ? panicSrc.timeSamples : baseSrc.timeSamples;
                stutterTimer = 0.07f + R() * 0.07f;
                nextSkip = Mathf.Lerp(26f, 5f, v) * (0.5f + R());
            }
            if (stutterLeft > 0)
            {
                stutterTimer -= dt;
                if (stutterTimer <= 0f)
                {
                    if (panicVol > 0.5f) panicSrc.timeSamples = Mathf.Clamp(stutterPos, 0, panicSrc.clip.samples - 1);
                    else foreach (var m in music) m.timeSamples = Mathf.Clamp(stutterPos, 0, m.clip.samples - 1);
                    stutterLeft--;
                    stutterTimer = 0.07f + R() * 0.07f;
                    staticBurst = Mathf.Max(staticBurst, 0.7f);
                }
            }
        }

        /// <summary>Tape hiss with pops and crackle, looped under the music when grime is up.</summary>
        static AudioClip BuildStatic()
        {
            int n = SR * 3;
            var d = new float[n];
            var rng = new System.Random(3);
            float lp = 0f, pop = 0f;
            for (int i = 0; i < n; i++)
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.35f;
                if (rng.NextDouble() < 12.0 / SR) pop = (float)(rng.NextDouble() * 2.0 - 1.0);
                pop *= 0.985f;
                float crackle = rng.NextDouble() < 0.002 ? noise : 0f;
                d[i] = lp * 0.5f + pop * 0.8f + crackle * 0.6f;
            }
            var c = AudioClip.Create("vhs_static", n, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        // =================================================================== composition

        enum Wave { Pulse25, Square, Triangle, Sine }

        struct Chord { public int bass; public int[] tones; }

        // A minor / F / G / E(major, from harmonic minor) / Dm — a little sinister under the bounce.
        static readonly Chord Am = new Chord { bass = 45, tones = new[] { 57, 60, 64 } };
        static readonly Chord F = new Chord { bass = 41, tones = new[] { 53, 57, 60 } };
        static readonly Chord G = new Chord { bass = 43, tones = new[] { 55, 59, 62 } };
        static readonly Chord E = new Chord { bass = 40, tones = new[] { 52, 56, 59 } };
        static readonly Chord Dm = new Chord { bass = 38, tones = new[] { 50, 53, 57 } };

        // (midi, start16th, length16ths) over 8 bars — an original melody.
        static readonly int[,] Melody =
        {
            { 69, 0, 2 }, { 72, 2, 2 }, { 76, 4, 2 }, { 81, 6, 4 }, { 80, 10, 2 }, { 81, 12, 4 },
            { 77, 16, 4 }, { 76, 20, 2 }, { 72, 22, 2 }, { 69, 24, 4 }, { 72, 28, 4 },
            { 71, 32, 2 }, { 74, 34, 2 }, { 79, 36, 4 }, { 77, 40, 2 }, { 74, 42, 2 }, { 71, 44, 4 },
            { 68, 48, 2 }, { 71, 50, 2 }, { 76, 52, 4 }, { 74, 56, 2 }, { 71, 58, 2 }, { 68, 60, 4 },
            { 69, 64, 2 }, { 72, 66, 2 }, { 76, 68, 2 }, { 81, 70, 2 }, { 84, 72, 4 }, { 83, 76, 2 }, { 81, 78, 2 },
            { 77, 80, 2 }, { 81, 82, 2 }, { 84, 84, 4 }, { 81, 88, 2 }, { 77, 90, 2 }, { 76, 92, 4 },
            { 74, 96, 4 }, { 77, 100, 2 }, { 81, 102, 2 }, { 80, 104, 4 }, { 77, 108, 2 }, { 74, 110, 2 },
            { 76, 112, 4 }, { 80, 116, 4 }, { 83, 120, 4 }, { 80, 124, 2 }, { 76, 126, 2 },
        };

        static readonly int[] HarmonicMinorA = { 9, 11, 0, 2, 4, 5, 8 };

        static int ThirdBelow(int midi)
        {
            int pc = ((midi % 12) + 12) % 12;
            int idx = Array.IndexOf(HarmonicMinorA, pc);
            if (idx < 0) return midi - 3;
            int target = HarmonicMinorA[(idx + 5) % 7]; // two scale steps down
            int down = (pc - target + 12) % 12;
            return midi - down;
        }

        void BuildClips(out AudioClip baseClip, out AudioClip leadClip, out AudioClip harmClip)
        {
            var bars = new List<Chord> { Am, F, G, E, Am, F, Dm, E, Am, F, G, E, Am, F, Dm, E };
            int barSixteenths = 16;
            int totalSixteenths = bars.Count * barSixteenths;
            int n = Mathf.CeilToInt(totalSixteenths * Sixteenth * SR);
            var bass = new float[n];
            var lead = new float[n];
            var harm = new float[n];
            var rng = new System.Random(11);

            for (int b = 0; b < bars.Count; b++)
            {
                var c = bars[b];
                int bar0 = b * barSixteenths;
                bool secondHalf = b >= 8;

                // bass: bouncing octaves in 8ths, with a walk-up on the last 8th
                int[] bassPattern = { 0, 12, 0, 12, 0, 12, 7, 12 };
                for (int i = 0; i < 8; i++)
                {
                    int note = c.bass + bassPattern[i];
                    if (i == 7 && secondHalf) note = c.bass + 10;
                    AddNote(bass, (bar0 + i * 2) * Sixteenth, Sixteenth * 1.7f, MidiT(note), Wave.Triangle, 0.34f, 0.004f, 0.08f, 0.7f, 0.04f, 0f);
                    AddNote(bass, (bar0 + i * 2) * Sixteenth, Sixteenth * 1.7f, MidiT(note - 12), Wave.Sine, 0.14f, 0.004f, 0.1f, 0.6f, 0.04f, 0f);
                }

                // arpeggio: 16ths up and down the chord
                int[] arp = { 0, 1, 2, 3, 2, 1 };
                for (int i = 0; i < 16; i++)
                {
                    int k = arp[i % arp.Length];
                    int note = k == 3 ? c.tones[0] + 12 : c.tones[k];
                    AddNote(bass, (bar0 + i) * Sixteenth, Sixteenth * 0.6f, MidiT(note + 12), Wave.Square, 0.055f, 0.002f, 0.04f, 0.3f, 0.02f, 0f);
                }

                // drums
                for (int i = 0; i < 16; i++)
                {
                    float t = (bar0 + i) * Sixteenth;
                    bool fill = b == bars.Count - 1 && i >= 12;
                    if (i == 0 || i == 8 || (i == 10 && (b & 1) == 1)) Kick(bass, t);
                    if (fill || i == 4 || i == 12) Snare(bass, t, rng, fill ? 0.18f : 0.26f);
                    if ((i & 1) == 0) Hat(bass, t, rng, i == 14 ? 0.09f : 0.05f, i == 14 ? 0.12f : 0.03f);
                }
            }

            // lead (+ harmony a third below on the second pass)
            for (int pass = 0; pass < 2; pass++)
            {
                int offset = pass * 128;
                for (int i = 0; i < Melody.GetLength(0); i++)
                {
                    int note = Melody[i, 0];
                    float start = (offset + Melody[i, 1]) * Sixteenth;
                    float len = Melody[i, 2] * Sixteenth * 0.92f;
                    AddNote(lead, start, len, MidiT(note), Wave.Pulse25, 0.2f, 0.005f, 0.09f, 0.6f, 0.06f, 0.012f);
                    if (pass == 1)
                        AddNote(harm, start, len, MidiT(ThirdBelow(note)), Wave.Square, 0.2f, 0.005f, 0.09f, 0.5f, 0.06f, 0.008f);
                }
            }

            Finish(bass, 0.12f);
            Finish(lead, 0.18f);
            Finish(harm, 0.25f);
            baseClip = AudioClip.Create("music_base", n, 1, SR, false);
            baseClip.SetData(bass, 0);
            leadClip = AudioClip.Create("music_lead", n, 1, SR, false);
            leadClip.SetData(lead, 0);
            harmClip = AudioClip.Create("music_harmony", n, 1, SR, false);
            harmClip.SetData(harm, 0);
        }

        /// <summary>Original panic loop: 168 BPM, semitone "alarm" tremolo, pounding 16th bass, chromatic fall.</summary>
        static AudioClip BuildPanic()
        {
            const float bpm = 168f;
            float sx = 60f / bpm / 4f;
            var bb = new Chord { bass = 46, tones = new[] { 58, 62, 65 } }; // B-flat: the semitone above home, pure dread
            var bars = new[] { Am, Am, bb, bb, Am, Am, F, E };
            int n = Mathf.CeilToInt(bars.Length * 16 * sx * SR);
            var buf = new float[n];
            var rng = new System.Random(23);

            for (int b = 0; b < bars.Length; b++)
            {
                var c = bars[b];
                int bar0 = b * 16;
                int[] bassPat = { 0, 0, 12, 0, 0, 12, 0, 12, 0, 0, 12, 0, 0, 12, 7, 12 };
                for (int i = 0; i < 16; i++)
                {
                    float t = (bar0 + i) * sx;
                    AddNote(buf, t, sx * 0.8f, Midi(c.bass + bassPat[i]), Wave.Triangle, 0.3f, 0.002f, 0.05f, 0.6f, 0.02f, 0f);
                    AddNote(buf, t, sx * 0.8f, Midi(c.bass - 12), Wave.Sine, 0.12f, 0.002f, 0.05f, 0.6f, 0.02f, 0f);
                    // drums
                    if ((i & 3) == 0) Kick(buf, t);
                    if (i == 4 || i == 12 || ((b & 1) == 1 && i == 14)) Snare(buf, t, rng, 0.24f);
                    Hat(buf, t, rng, (i & 1) == 0 ? 0.05f : 0.03f, 0.025f);
                    // offbeat chord stabs
                    if ((i & 3) == 2)
                        foreach (var tone in c.tones) AddNote(buf, t, sx * 0.7f, Midi(tone + 12), Wave.Square, 0.03f, 0.002f, 0.03f, 0.4f, 0.02f, 0f);
                }

                // lead
                int[] alarm = b < 2 || (b >= 4 && b < 6) ? new[] { 81, 82 } : b < 4 ? new[] { 86, 87 } : null;
                int octave = b >= 4 && b < 6 ? 12 : 0;
                if (alarm != null)
                {
                    for (int i = 0; i < 16; i++)
                        AddNote(buf, (bar0 + i) * sx, sx * 0.7f, Midi(alarm[i & 1] + octave), Wave.Pulse25, 0.14f, 0.002f, 0.03f, 0.6f, 0.02f, 0f);
                }
                else if (b == 6)
                {
                    for (int i = 0; i < 16; i++) // chromatic fall
                        AddNote(buf, (bar0 + i) * sx, sx * 0.75f, Midi(89 - i), Wave.Pulse25, 0.16f, 0.002f, 0.03f, 0.6f, 0.02f, 0f);
                }
                else
                {
                    AddNote(buf, bar0 * sx, sx * 7.5f, Midi(76), Wave.Pulse25, 0.16f, 0.004f, 0.1f, 0.6f, 0.04f, 0.015f);
                    AddNote(buf, (bar0 + 8) * sx, sx * 3.5f, Midi(80), Wave.Pulse25, 0.16f, 0.004f, 0.1f, 0.6f, 0.04f, 0.015f);
                    AddNote(buf, (bar0 + 12) * sx, sx * 3.5f, Midi(83), Wave.Pulse25, 0.16f, 0.004f, 0.1f, 0.6f, 0.04f, 0.015f);
                }
            }
            Finish(buf, 0.12f, 2.2f); // driven harder: denser and louder
            var clip = AudioClip.Create("music_panic", n, 1, SR, false);
            clip.SetData(buf, 0);
            return clip;
        }

        static float Midi(int m) => 440f * Mathf.Pow(2f, (m - 69) / 12f);

        /// <summary>Adds one enveloped note; anything that runs past the end wraps to the start so the loop is seamless.</summary>
        static void AddNote(float[] buf, float start, float dur, float freq, Wave wave, float vol,
            float attack, float decay, float sustain, float release, float vibrato)
        {
            int n = buf.Length;
            int s0 = Mathf.RoundToInt(start * SR);
            int len = Mathf.RoundToInt((dur + release) * SR);
            float phase = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float env;
                if (t < attack) env = t / attack;
                else if (t < attack + decay) env = Mathf.Lerp(1f, sustain, (t - attack) / decay);
                else if (t < dur) env = sustain;
                else env = sustain * Mathf.Clamp01(1f - (t - dur) / release);
                float vib = vibrato > 0f && t > 0.12f ? 1f + vibrato * Mathf.Sin(t * 2f * Mathf.PI * 5.5f) : 1f;
                phase += freq * vib / SR;
                phase -= Mathf.Floor(phase);
                float v;
                switch (wave)
                {
                    case Wave.Pulse25: v = phase < 0.25f ? 1f : -0.33f; break;
                    case Wave.Square: v = phase < 0.5f ? 1f : -1f; break;
                    case Wave.Triangle: v = 4f * Mathf.Abs(phase - 0.5f) - 1f; break;
                    default: v = Mathf.Sin(phase * 2f * Mathf.PI); break;
                }
                buf[(s0 + i) % n] += v * env * vol;
            }
        }

        static void Kick(float[] buf, float start)
        {
            int n = buf.Length, s0 = Mathf.RoundToInt(start * SR), len = Mathf.RoundToInt(0.22f * SR);
            float phase = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float f = 45f + 110f * Mathf.Exp(-t * 30f);
                phase += f / SR;
                buf[(s0 + i) % n] += Mathf.Sin(phase * 2f * Mathf.PI) * Mathf.Exp(-t * 14f) * 0.55f;
            }
        }

        static void Snare(float[] buf, float start, System.Random rng, float vol)
        {
            int n = buf.Length, s0 = Mathf.RoundToInt(start * SR), len = Mathf.RoundToInt(0.18f * SR);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                buf[(s0 + i) % n] += (noise * Mathf.Exp(-t * 22f) + Mathf.Sin(t * 2f * Mathf.PI * 185f) * Mathf.Exp(-t * 30f) * 0.6f) * vol;
            }
        }

        static void Hat(float[] buf, float start, System.Random rng, float vol, float length)
        {
            int n = buf.Length, s0 = Mathf.RoundToInt(start * SR), len = Mathf.RoundToInt(length * SR);
            float prev = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float hp = noise - prev; // crude high-pass
                prev = noise;
                buf[(s0 + i) % n] += hp * Mathf.Exp(-t * (3f / length)) * vol;
            }
        }

        /// <summary>Gentle low-pass to take the edge off the raw waves, then normalise with a soft clip.</summary>
        static void Finish(float[] buf, float smoothing, float drive = 1.2f)
        {
            float lp = 0f, peak = 0.0001f;
            for (int i = 0; i < buf.Length; i++)
            {
                lp += (buf[i] - lp) * (1f - smoothing);
                buf[i] = lp;
                peak = Mathf.Max(peak, Mathf.Abs(lp));
            }
            float g = 0.85f / peak;
            float norm = drive > 1.5f ? 0.95f / (float)Math.Tanh(drive) : 1f; // only the driven panic mix gets re-levelled
            for (int i = 0; i < buf.Length; i++) buf[i] = (float)Math.Tanh(buf[i] * g * drive) * Mathf.Min(norm, 1.2f) * 0.85f;
        }
    }
}
