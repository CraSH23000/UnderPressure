using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnderPressure
{
    /// <summary>Runtime URP material factory + the game's color palette.</summary>
    public static class Mats
    {
        // ---- Pipe palette: written by Theme.Apply() so it can get grimier level by level ----
        public static Color PipeBody = new Color(0.93f, 0.94f, 0.96f);
        public static Color PipeFlange = new Color(0.14f, 0.15f, 0.17f);
        public static Color Clamp = new Color(1.0f, 0.55f, 0.12f);
        public static Color Staple = new Color(0.25f, 0.26f, 0.28f);
        public static Color Plate = new Color(0.7f, 0.72f, 0.76f);
        // ---- Liquid: set per chamber by Theme.ApplyLiquid (clean water early, worse later) ----
        public static Color Liquid = new Color(0.25f, 1.0f, 0.45f);
        public static Color LiquidGlow = new Color(0.3f, 2.2f, 0.7f); // HDR -> bloom
        public static string LiquidName = "TOXIC SLIME";
        /// <summary>The chamber's liquid: 1-based index into GameConfig.liquids (0 = none picked).</summary>
        public static int LiquidId;

        /// <summary>
        /// How much of the liquid's glow shows on its surface (pipe fill, puddle). Dark liquids like VOID keep their
        /// color (black) with just a faint sheen; the glow still shows on the liquid front, splashes and particles.
        /// </summary>
        public static float FillGlowScale
        {
            get
            {
                float lum = Liquid.r * 0.3f + Liquid.g * 0.59f + Liquid.b * 0.11f;
                return Mathf.Clamp(lum * 1.6f, 0.08f, 1f);
            }
        }
        public static readonly Color Danger = new Color(1f, 0.25f, 0.2f);
        public static readonly Color Money = new Color(1f, 0.86f, 0.25f);

        public const int FillLevels = 6;

        static Shader lit, particle;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Material[] fillMats;
        static Texture2D circle, ring;

        public static void Init()
        {
            cache.Clear();
            patterned.Clear();
            tintedFills.Clear();
            patternShaderLooked = false;
            fillMats = null;
            circle = null;
            ring = null;
            lit = Shader.Find("Universal Render Pipeline/Lit");
            // in a player build the shaders come in through the keep-alive materials (see Editor/UnderPressureBuild)
            if (lit == null) lit = KeptShader("LitEmission");
            if (lit == null) lit = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (lit == null) lit = Shader.Find("Standard");
            particle = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (particle == null) particle = KeptShader("ParticlesUnlit");
            if (particle == null) particle = Shader.Find("Particles/Standard Unlit");
            if (lit == null) Debug.LogError("[UnderPressure] Could not find a URP Lit shader. Is the project using URP?");
        }

        static Shader KeptShader(string name)
        {
            var m = Resources.Load<Material>("UnderPressureShaderKeep/" + name);
            return m != null ? m.shader : null;
        }

        public static Material Lit(Color c, float smoothness = 0.45f, float metallic = 0f, Color? emission = null)
        {
            string key = $"lit{c}{smoothness}{metallic}{emission}";
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(lit) { name = "UP_" + key };
            SetBase(m, c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.SetColor("_EmissionColor", emission.Value);
            }
            cache[key] = m;
            return m;
        }

        public static void SetEmission(Material m, Color c)
        {
            if (m != null && m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c);
        }

        /// <summary>A unique (uncached) transparent material — e.g. the placement ghost, whose color changes.</summary>
        public static Material Transparent(Color c)
        {
            // Copy the saved transparent material when there is one: its keywords are exactly the variant that
            // gets included in a player build (one built from scratch here can miss it and render solid).
            var kept = Resources.Load<Material>("UnderPressureShaderKeep/LitTransparent");
            if (kept != null)
            {
                var k = new Material(kept) { name = "UP_Transparent" };
                SetBase(k, c);
                k.SetColor("_EmissionColor", c * 0.4f);
                return k;
            }
            var m = new Material(lit) { name = "UP_Transparent" };
            SetBase(m, c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 0.4f);
            return m;
        }

        public static void SetBase(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        /// <summary>Pipe segment materials from empty (0) to full of liquid (FillLevels-1). Swapping shared
        /// materials instead of property blocks keeps the SRP batcher happy.</summary>
        static readonly Dictionary<string, Material[]> tintedFills = new Dictionary<string, Material[]>();

        /// <summary>Fill materials for a stream of a given color (filtered or concentrated liquid).</summary>
        public static Material FillMatFor(Color liquid, Color glow, int level, int liquidId = 0)
        {
            bool pat = IsPatterned(liquidId);
            string key = pat ? $"{liquid}{glow}#{liquidId}" : $"{liquid}{glow}";
            if (!tintedFills.TryGetValue(key, out var mats) || mats == null || mats[0] == null)
            {
                mats = new Material[FillLevels];
                for (int i = 0; i < FillLevels; i++)
                {
                    float t = i / (float)(FillLevels - 1);
                    var c = Color.Lerp(PipeBody, liquid, Mathf.SmoothStep(0, 1, t));
                    mats[i] = pat && i > 0
                        ? PatternMat(liquidId, liquid, glow, 0.4f * FillGlowScale, 0.85f, 0f, Mathf.SmoothStep(0, 1, t), t)
                        : Lit(c, 0.85f, 0f, Color.Lerp(Color.black, glow * 0.4f * FillGlowScale, t));
                }
                tintedFills[key] = mats;
            }
            return mats[Mathf.Clamp(level, 0, FillLevels - 1)];
        }

        public static Material FillMat(int level)
        {
            if (fillMats == null)
            {
                fillMats = new Material[FillLevels];
                for (int i = 0; i < FillLevels; i++)
                {
                    float t = i / (float)(FillLevels - 1);
                    var c = Color.Lerp(PipeBody, Liquid, Mathf.SmoothStep(0, 1, t));
                    fillMats[i] = i > 0 && IsPatterned(LiquidId)
                        ? PatternMat(LiquidId, Liquid, LiquidGlow, 0.4f * FillGlowScale, 0.85f, 0f, Mathf.SmoothStep(0, 1, t), t)
                        : Lit(c, 0.85f, 0f, Color.Lerp(Color.black, LiquidGlow * 0.4f * FillGlowScale, t));
                }
            }
            return fillMats[Mathf.Clamp(level, 0, FillLevels - 1)];
        }

        // ---------------- Patterned liquids (LiquidType.pattern != Solid) ----------------

        class Patterned { public Material mat; public int id; public float glowScale; }
        static readonly List<Patterned> patterned = new List<Patterned>();
        static Shader patternShader;
        static bool patternShaderLooked;
        static readonly int OriginId = Shader.PropertyToID("_UPLiquidOrigin");

        /// <summary>Debug (F8 in the editor / dev builds): show every patterned liquid as this pattern instead of
        /// its own, to compare looks live. Null = each liquid's configured pattern.</summary>
        public static LiquidPattern? PatternOverride;

        static Shader PatternShader
        {
            get
            {
                if (!patternShaderLooked) { patternShaderLooked = true; patternShader = Shader.Find("UnderPressure/LiquidPattern"); }
                return patternShader != null && patternShader.isSupported ? patternShader : null;
            }
        }

        static LiquidType LiquidDef(int id)
        {
            var list = Game.Config != null ? Game.Config.liquids : null;
            return list != null && id > 0 && id <= list.Length ? list[id - 1] : null;
        }

        /// <summary>Does this GameConfig.liquids entry (1-based) draw with a moving pattern?</summary>
        public static bool IsPatterned(int liquidId)
        {
            var d = LiquidDef(liquidId);
            return d != null && d.pattern != LiquidPattern.Solid && PatternShader != null;
        }

        /// <summary>
        /// A liquid's surface: plain Lit (cached) for solid liquids, or the LiquidPattern shader for patterned ones.
        /// 'glowScale' multiplies both glows (the entry's glow2 gets the same treatment as 'glow').
        /// 'unique' gives a fresh material the caller may recolor (the spout slime).
        /// </summary>
        public static Material LiquidMat(int liquidId, Color c, Color glow, float glowScale, float smoothness, float metallic = 0f, bool unique = false)
        {
            if (IsPatterned(liquidId))
            {
                var pm = PatternMat(liquidId, c, glow, glowScale, smoothness, metallic, 1f, 1f, unique);
                if (pm != null) return pm;
            }
            var m = Lit(c, smoothness, metallic, glow * glowScale);
            return unique ? new Material(m) : m;
        }

        static Material PatternMat(int liquidId, Color c, Color glow, float glowScale, float smoothness, float metallic, float fill, float fillGlow, bool unique = false)
        {
            string key = $"pat{liquidId}{c}{glow}{glowScale}{smoothness}{metallic}{fill}{fillGlow}";
            if (!unique && cache.TryGetValue(key, out var m) && m != null) return m;
            var sh = PatternShader;
            if (sh == null) return null;
            m = new Material(sh) { name = "UP_LiquidPattern" };
            SetBase(m, c);
            m.SetColor("_EmissionColor", glow * glowScale);
            m.SetColor("_PipeColor", PipeBody);
            m.SetFloat("_Fill", fill);
            m.SetFloat("_FillGlow", fillGlow);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            var p = new Patterned { mat = m, id = liquidId, glowScale = glowScale };
            ApplyPattern(p);
            patterned.Add(p);
            if (!unique) cache[key] = m;
            return m;
        }

        static void ApplyPattern(Patterned p)
        {
            var d = LiquidDef(p.id);
            if (d == null) return;
            var pat = PatternOverride ?? d.pattern;
            p.mat.SetFloat("_Pattern", pat == LiquidPattern.Marble ? 1f : 0f);
            p.mat.SetColor("_Color2", d.color2);
            p.mat.SetColor("_Emission2", d.glow2 * p.glowScale);
            p.mat.SetFloat("_Scale", d.patternScale);
            p.mat.SetFloat("_Speed", d.patternSpeed);
        }

        /// <summary>Once a frame (from the spout): where the bands flow out from, plus live re-reading of the
        /// pattern settings so they can be tuned in the GameConfig inspector during play.</summary>
        public static void TickPatterns(Vector3 origin)
        {
            Shader.SetGlobalVector(OriginId, origin);
            var kb = Debug.isDebugBuild ? UnityEngine.InputSystem.Keyboard.current : null; // editor + dev builds only
            if (kb != null && kb.f8Key.wasPressedThisFrame)
            {
                PatternOverride = PatternOverride == null ? LiquidPattern.Bands : PatternOverride == LiquidPattern.Bands ? LiquidPattern.Marble : (LiquidPattern?)null;
                Debug.Log(PatternOverride.HasValue
                    ? $"[UnderPressure] Liquid pattern: forced to {PatternOverride} (F8)"
                    : "[UnderPressure] Liquid pattern: back to each liquid's own setting in GameConfig (F8)");
            }
            for (int i = patterned.Count - 1; i >= 0; i--)
            {
                if (patterned[i].mat == null) { patterned.RemoveAt(i); continue; }
                ApplyPattern(patterned[i]);
            }
        }

        /// <summary>Unlit (HDR) color, e.g. for lightning and electric arcs. Not cached: callers may animate it.</summary>
        public static Material Unlit(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = KeptShader("Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var m = new Material(sh) { name = "UP_Unlit" };
            SetBase(m, c);
            return m;
        }

        public static Material Particle(bool glow)
        {
            string key = glow ? "particleGlow" : "particle";
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(particle) { name = "UP_" + key };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", CircleTexture);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", CircleTexture);
            SetBase(m, glow ? new Color(2.2f, 2.2f, 2.2f, 1f) : Color.white);
            // Opaque + alpha clip = crisp cartoon blobs that shrink away, no sorting issues.
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            cache[key] = m;
            return m;
        }

        public static Texture2D CircleTexture
        {
            get
            {
                if (circle != null) return circle;
                circle = MakeCircle(64, 0f);
                return circle;
            }
        }

        public static Texture2D RingTexture
        {
            get
            {
                if (ring != null) return ring;
                ring = MakeCircle(128, 0.62f);
                return ring;
            }
        }

        static Texture2D MakeCircle(int size, float innerRadius01)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float outer = Mathf.Clamp01((1f - d) * size * 0.5f);
                float inner = innerRadius01 > 0 ? Mathf.Clamp01((d - innerRadius01) * size * 0.5f) : 1f;
                byte a = (byte)(255 * Mathf.Min(outer, inner));
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        public static Sprite MakeSprite(Texture2D tex) =>
            UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }
}
