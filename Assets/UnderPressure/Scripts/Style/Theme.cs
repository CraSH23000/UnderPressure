using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// Art direction in one place. "Grime" 0 = sterile white test chamber, 1 = rusted, stained, flickering.
    /// The plan: level 1 is pristine, later levels creep toward 1. Cute characters stay bright the whole way.
    /// </summary>
    public static class Theme
    {
        public static float Grime { get; private set; }

        // environment
        public static Color Floor, FloorSeam, Wall, WallBacking, WallTrim, TileA, TileB, BoardBase;
        public static Color LightColor, Ambient, Fog;
        public static float LightIntensity;
        // accents (Portal-ish orange / blue, safety yellow)
        public static readonly Color AccentOrange = new Color(1f, 0.55f, 0.12f);
        public static readonly Color AccentBlue = new Color(0.25f, 0.65f, 1f);
        public static readonly Color Hazard = new Color(1f, 0.8f, 0.1f);
        public static readonly Color Ink = new Color(0.07f, 0.07f, 0.09f);
        public static readonly Color Stain = new Color(0.22f, 0.17f, 0.1f);
        public static readonly Color Rust = new Color(0.45f, 0.22f, 0.1f);

        public static void Apply(float grime)
        {
            Grime = Mathf.Clamp01(grime);
            float g = Grime;
            Floor = L(new Color(0.85f, 0.86f, 0.87f), new Color(0.46f, 0.43f, 0.36f), g);
            FloorSeam = L(new Color(0.55f, 0.57f, 0.6f), new Color(0.16f, 0.13f, 0.1f), g);
            Wall = L(new Color(0.95f, 0.96f, 0.97f), new Color(0.58f, 0.55f, 0.45f), g);
            WallBacking = L(new Color(0.1f, 0.1f, 0.12f), new Color(0.05f, 0.04f, 0.03f), g);
            WallTrim = L(new Color(0.16f, 0.17f, 0.19f), new Color(0.12f, 0.1f, 0.08f), g);
            TileA = L(new Color(0.66f, 0.69f, 0.72f), new Color(0.4f, 0.39f, 0.33f), g);
            TileB = L(new Color(0.61f, 0.64f, 0.67f), new Color(0.36f, 0.35f, 0.3f), g);
            BoardBase = L(new Color(0.2f, 0.21f, 0.23f), new Color(0.1f, 0.09f, 0.07f), g);
            LightColor = L(new Color(0.93f, 0.97f, 1f), new Color(1f, 0.84f, 0.6f), g);
            LightIntensity = Mathf.Lerp(1.25f, 1.0f, g);
            Ambient = L(new Color(0.78f, 0.82f, 0.88f), new Color(0.42f, 0.38f, 0.3f), g);
            Fog = L(new Color(0.75f, 0.8f, 0.85f), new Color(0.22f, 0.24f, 0.16f), g);

            Mats.PipeBody = L(new Color(0.93f, 0.94f, 0.96f), new Color(0.74f, 0.7f, 0.58f), g);
            Mats.PipeFlange = L(new Color(0.14f, 0.15f, 0.17f), Rust, g * 0.8f);
            Mats.Clamp = L(AccentOrange, new Color(0.62f, 0.42f, 0.2f), g);
            Mats.Staple = L(new Color(0.25f, 0.26f, 0.28f), new Color(0.3f, 0.2f, 0.13f), g);
            Mats.Plate = L(new Color(0.72f, 0.74f, 0.78f), new Color(0.38f, 0.36f, 0.3f), g);
        }

        /// <summary>Pick this chamber's liquid from the config's progression.</summary>
        public static void ApplyLiquid(int chamber, GameConfig cfg)
        {
            if (cfg == null || cfg.liquids == null || cfg.liquids.Length == 0) return;
            // only entries with a fromChamber come out of a spout (fromChamber 0 = made by filters only)
            LiquidType pick = null;
            foreach (var l in cfg.liquids)
                if (l != null && l.fromChamber > 0 && (pick == null || (chamber >= l.fromChamber && l.fromChamber >= pick.fromChamber))) pick = l;
            if (pick == null) pick = cfg.liquids[0];
            Mats.LiquidId = System.Array.IndexOf(cfg.liquids, pick) + 1;
            Mats.Liquid = pick.color;
            Mats.LiquidGlow = pick.glow;
            Mats.LiquidName = string.IsNullOrEmpty(pick.name) ? "LIQUID" : pick.name;
        }

        static Color L(Color a, Color b, float t) => Color.Lerp(a, b, t);
    }
}
