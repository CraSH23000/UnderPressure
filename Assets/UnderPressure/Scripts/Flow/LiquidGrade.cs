using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// What one stream of liquid is, after any filters it went through. A filter splits a stream in two: the purified
    /// half (water, one grade purer) and the concentrated half (the impurities, one grade stronger). Grades stop at 3,
    /// and grade 3 of either is lethal to drink.
    /// </summary>
    public struct LiquidGrade
    {
        public bool water;   // a water stream (from a water chamber, or filtered out of something worse)
        public int level;    // water: purity 0-3; anything else: concentration 0-3

        public const int Max = 3;

        /// <summary>The chamber's own liquid, straight out of the spout.</summary>
        public static LiquidGrade Source => new LiquidGrade { water = Progress.LiquidIsWater, level = 0 };

        public bool Plain => level == 0;
        public bool Lethal => level >= Max;

        /// <summary>The filter: the clockwise output gets the purified stream, the other the concentrated one.</summary>
        public LiquidGrade Purified => new LiquidGrade { water = true, level = Mathf.Min(Max, water ? level + 1 : 1) };
        public LiquidGrade Concentrated => water ? this : new LiquidGrade { water = false, level = Mathf.Min(Max, level + 1) };

        static readonly string[] WaterNames = { "WATER", "FILTERED WATER", "PURE WATER", "DEIONIZED WATER" };
        static readonly string[] ConcPrefix = { "", "CONCENTRATED ", "DOUBLE-CONCENTRATED ", "HYPER-CONCENTRATED " };

        public string Name
        {
            get
            {
                int l = Mathf.Clamp(level, 0, Max);
                if (water) return l == 0 && Progress.LiquidIsWater ? Mats.LiquidName : WaterNames[l];
                return ConcPrefix[l] + Mats.LiquidName;
            }
        }

        /// <summary>The stream's color in the pipes: water goes paler and brighter as it purifies; the concentrate
        /// gets deeper and glows harder.</summary>
        public Color Color
        {
            get
            {
                float t = Mathf.Clamp01(level / (float)Max);
                if (water)
                {
                    var baseWater = Progress.LiquidIsWater && level == 0 ? Mats.Liquid : new Color(0.45f, 0.75f, 1f);
                    return Color.Lerp(baseWater, new Color(0.85f, 0.97f, 1f), t);
                }
                Color.RGBToHSV(Mats.Liquid, out float h, out float s, out float v);
                return Color.HSVToRGB(h, Mathf.Clamp01(s + 0.25f * t), Mathf.Clamp01(v * (1f - 0.25f * t)));
            }
        }

        public Color Glow => water ? Color * (0.4f + level * 0.5f) : Mats.LiquidGlow * (1f + level * 0.8f);

        /// <summary>Filtered streams are worth more: each grade adds filterGradeBonus to a pipe's pay.</summary>
        public float ValueMultiplier => 1f + level * (Game.Config != null ? Game.Config.filterGradeBonus : 0.5f);

        public bool Same(LiquidGrade o) => water == o.water && level == o.level;
    }
}
