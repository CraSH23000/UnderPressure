using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// What one stream of liquid is, after any filters it went through. A filter splits a stream in two: the purified
    /// half and the concentrated half. What each half becomes is data: a liquid in GameConfig.liquids can name what it
    /// purifies into and what its concentrate becomes (other entries in the same list). A link ending in '+' is an
    /// endless chain: "TOXIC SLIME+" means TOXIC SLIME one step further each pass (TOXIC SLIME+, ++, +++, +4, +5...),
    /// without needing an entry per step. No purify link: generic water one grade purer. No concentrate link: the
    /// liquid stays as it is. 'level' counts filter passes (0-3): it sets the pay bonus, and grade 3 is lethal to drink.
    /// </summary>
    public struct LiquidGrade
    {
        public bool water;     // a water stream (its name has WATER in it)
        public int level;      // filter passes so far, 0-3 (looks and lethality)
        public int passes;     // filter passes so far, uncapped (pay: every filter pays)
        public int liquidId;   // 1-based index into GameConfig.liquids; 0 = generic water (no entry)
        public int extra;      // '+' steps on that entry (TOXIC SLIME++ = 2)
        public bool pure;      // those steps came from purifying (paler and brighter) rather than concentrating (deeper)

        public const int Max = 3;

        /// <summary>The chamber's own liquid, straight out of the spout.</summary>
        public static LiquidGrade Source => new LiquidGrade { water = Progress.LiquidIsWater, level = 0, liquidId = Mats.LiquidId };

        public bool Plain => level == 0;
        public bool Lethal => level >= Max;

        static LiquidType[] List => Game.Config != null ? Game.Config.liquids : null;

        /// <summary>This stream's entry in GameConfig.liquids (null for generic water).</summary>
        public LiquidType Def
        {
            get
            {
                var list = List;
                return list != null && liquidId > 0 && liquidId <= list.Length ? list[liquidId - 1] : null;
            }
        }

        /// <summary>The 1-based id of the entry called 'name' (case and spaces ignored), or 0.</summary>
        public static int Find(string name)
        {
            var list = List;
            if (list == null || string.IsNullOrWhiteSpace(name)) return 0;
            string n = name.Trim();
            for (int i = 0; i < list.Length; i++)
                if (list[i] != null && string.Equals(list[i].name?.Trim(), n, System.StringComparison.OrdinalIgnoreCase)) return i + 1;
            return 0;
        }

        static bool IsWaterName(string name) => name != null && name.ToUpperInvariant().Contains("WATER");

        LiquidGrade Become(int id, int plus, bool purify)
        {
            var def = List[id - 1];
            return new LiquidGrade { water = IsWaterName(def.name), level = Mathf.Min(Max, level + 1), passes = passes + 1, liquidId = id, extra = plus, pure = purify && plus > 0 };
        }

        /// <summary>Where a purifiesTo / concentratesTo link leads from this stream. An exact entry name wins; otherwise
        /// "NAME+" (or "NAME++", "NAME+4") means NAME one '+' further than this stream if it already is NAME, else
        /// that many '+'.</summary>
        bool Follow(string link, bool purify, out LiquidGrade g)
        {
            g = this;
            if (string.IsNullOrWhiteSpace(link)) return false;
            int id = Find(link);
            if (id > 0) { g = Become(id, 0, purify); return true; }
            int n = Pluses(link, out string baseName);
            if (n == 0 || (id = Find(baseName)) == 0) return false;
            g = Become(id, id == liquidId ? extra + 1 : n, purify);
            return true;
        }

        /// <summary>Going against a '+' chain walks back one step: purifying TOXIC SLIME++ gives TOXIC SLIME+, and
        /// concentrating ULTRAPURE WATER+ gives ULTRAPURE WATER. Only once the '+' are gone do the entry's links apply.</summary>
        bool StepBack(bool purify, out LiquidGrade g)
        {
            g = this;
            if (extra <= 0 || pure == purify || Def == null) return false;
            g.extra = extra - 1;
            g.level = Mathf.Min(Max, level + 1);
            g.passes = passes + 1;
            if (g.extra == 0) g.pure = false;
            return true;
        }

        /// <summary>How many '+' a name ends in ("X+++" = 3, "X+7" = 7), and the name without them.</summary>
        public static int Pluses(string name, out string baseName)
        {
            baseName = (name ?? "").Trim();
            int p = baseName.LastIndexOf('+');
            if (p >= 0 && p < baseName.Length - 1 && int.TryParse(baseName.Substring(p + 1), out int k) && k > 0)
            {
                baseName = baseName.Substring(0, p).TrimEnd();
                return k;
            }
            int c = 0;
            while (baseName.EndsWith("+")) { baseName = baseName.Substring(0, baseName.Length - 1); c++; }
            baseName = baseName.TrimEnd();
            return c;
        }

        public static int Pluses(string name) => Pluses(name, out _);

        /// <summary>"+", "++", "+++", then "+4", "+5"...</summary>
        public static string PlusSuffix(int n) => n <= 0 ? "" : n <= 3 ? new string('+', n) : "+" + n;

        /// <summary>The filter: the clockwise output gets the purified stream, the other the concentrated one.</summary>
        public LiquidGrade Purified
        {
            get
            {
                if (StepBack(true, out var g) || Follow(Def?.purifiesTo, true, out g)) return g;
                return new LiquidGrade { water = true, level = Mathf.Min(Max, water ? level + 1 : 1), passes = passes + 1 };
            }
        }

        public LiquidGrade Concentrated
        {
            get
            {
                if (!StepBack(false, out var g) && !Follow(Def?.concentratesTo, false, out g)) g.passes = passes + 1;
                return g;
            }
        }

        static readonly string[] WaterNames = { "WATER", "FILTERED WATER", "PURE WATER", "DEIONIZED WATER" };

        public string Name
        {
            get
            {
                var def = Def;
                if (def != null) return (string.IsNullOrEmpty(def.name) ? "LIQUID" : def.name.Trim()) + PlusSuffix(extra);
                int l = Mathf.Clamp(level, 0, Max);
                if (water) return l == 0 && Progress.LiquidIsWater ? Mats.LiquidName : WaterNames[l];
                return Mats.LiquidName;
            }
        }

        /// <summary>The stream's color in the pipes: its entry's color (deeper for each concentrate '+', paler and
        /// brighter for each purify '+'), or
        /// for generic water, paler and brighter as it purifies.</summary>
        public Color Color
        {
            get
            {
                var def = Def;
                if (def != null) return extra <= 0 ? def.color : pure ? Brighten(def.color, extra) : Deepen(def.color, extra);
                float t = Mathf.Clamp01(level / (float)Max);
                if (water)
                {
                    var baseWater = Progress.LiquidIsWater && level == 0 ? Mats.Liquid : new Color(0.45f, 0.75f, 1f);
                    return Color.Lerp(baseWater, new Color(0.85f, 0.97f, 1f), t);
                }
                return Deepen(Mats.Liquid, level);
            }
        }

        // each step goes part of the remaining way, so +7 still looks a bit different from +6 without going black/white
        static float StepT(int steps) => 1f - Mathf.Pow(0.72f, steps);

        static Color Deepen(Color c, int steps)
        {
            float t = StepT(steps);
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Clamp01(s + 0.35f * t), Mathf.Clamp01(v * (1f - 0.45f * t)));
        }

        static Color Brighten(Color c, int steps) => Color.Lerp(c, new Color(0.92f, 0.99f, 1f), StepT(steps) * 0.8f);

        public Color Glow
        {
            get
            {
                var def = Def;
                if (def != null) return def.glow * (1f + StepT(extra) * (pure ? 0.8f : 1.6f));
                return water ? Color * (0.4f + level * 0.5f) : Mats.LiquidGlow * (1f + level * 0.8f);
            }
        }

        /// <summary>A readable text color for this liquid's name: its own color, or for very dark liquids (VOID) its
        /// glow, so it doesn't vanish on a dark hologram.</summary>
        public Color TextColor
        {
            get
            {
                Color c = Color;
                if (c.grayscale >= 0.3f) return c;
                Color g = Glow;
                float m = Mathf.Max(g.r, Mathf.Max(g.g, g.b));
                return m > 0.01f ? new Color(g.r / m, g.g / m, g.b / m) : Color.Lerp(c, Color.white, 0.5f);
            }
        }

        /// <summary>Filtered streams are worth more. Every filter pass adds filterGradeBonus (filtering is a risk, so it
        /// always pays, whichever way it goes), and every step the result sits away from water adds
        /// filterDistanceBonus. Unfiltered spout liquid pays the plain rate.</summary>
        public float ValueMultiplier
        {
            get
            {
                if (passes <= 0) return 1f;
                var cfg = Game.Config;
                return 1f + passes * (cfg != null ? cfg.filterGradeBonus : 0.5f) + Distance * (cfg != null ? cfg.filterDistanceBonus : 0.25f);
            }
        }

        /// <summary>Steps from water along the purifiesTo / concentratesTo links (either way), plus this stream's '+'.
        /// "Water" is the first chamber's liquid. A group of liquids that never links back to it (??? and friends)
        /// counts as one step further than the furthest connected liquid, then steps out from its own spout liquid.</summary>
        public int Distance
        {
            get
            {
                if (liquidId <= 0) return level; // generic water: its grade is its distance
                var d = Distances();
                return (liquidId <= d.Length ? Mathf.Max(0, d[liquidId - 1]) : 0) + Mathf.Max(0, extra);
            }
        }

        static int[] distCache;
        static int distFrame = -1;
        static LiquidType[] distList;

        static int[] Distances()
        {
            var list = List;
            if (list == null) return new int[0];
            if (distCache != null && distList == list && distFrame == Time.frameCount) return distCache;
            int n = list.Length;
            // links both ways
            var adj = new System.Collections.Generic.List<int>[n];
            for (int i = 0; i < n; i++) adj[i] = new System.Collections.Generic.List<int>();
            for (int i = 0; i < n; i++)
            {
                if (list[i] == null) continue;
                foreach (var link in new[] { list[i].purifiesTo, list[i].concentratesTo })
                {
                    if (string.IsNullOrWhiteSpace(link)) continue;
                    int j = Find(link);
                    if (j == 0) { Pluses(link, out string b); j = Find(b); }
                    if (j > 0 && j - 1 != i) { adj[i].Add(j - 1); adj[j - 1].Add(i); }
                }
            }
            var dist = new int[n];
            for (int i = 0; i < n; i++) dist[i] = -1;
            // start from water (the earliest spout liquid), then any group left unreached from its own earliest spout liquid
            int furthest = 0;
            while (true)
            {
                int seed = -1;
                for (int i = 0; i < n; i++)
                    if (dist[i] < 0 && list[i] != null && list[i].fromChamber > 0 && (seed < 0 || list[i].fromChamber < list[seed].fromChamber)) seed = i;
                if (seed < 0) for (int i = 0; i < n; i++) if (dist[i] < 0) { seed = i; break; }
                if (seed < 0) break;
                bool first = furthest == 0 && System.Array.TrueForAll(dist, x => x < 0);
                dist[seed] = first ? 0 : furthest + 1;
                var q = new System.Collections.Generic.Queue<int>();
                q.Enqueue(seed);
                while (q.Count > 0)
                {
                    int a = q.Dequeue();
                    furthest = Mathf.Max(furthest, dist[a]);
                    foreach (int b in adj[a]) if (dist[b] < 0) { dist[b] = dist[a] + 1; q.Enqueue(b); }
                }
            }
            distCache = dist; distList = list; distFrame = Time.frameCount;
            return dist;
        }

        public bool Same(LiquidGrade o) => water == o.water && level == o.level && liquidId == o.liquidId && extra == o.extra && pure == o.pure && passes == o.passes;
    }
}
