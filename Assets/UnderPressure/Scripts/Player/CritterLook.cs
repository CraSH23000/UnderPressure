using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// What a critter looks like: species, body color, hat, torso and feet. Changed in the closets,
    /// remembered per player slot between sessions. (The floor ring always stays the player's identity color.)
    /// </summary>
    /// <summary>The parts of a critter you can change in the closets.</summary>
    /// <summary>Rows a closet can show. Player is the player color (floor ring, HUD), not part of the critter's look.</summary>
    public enum Part { Species, Size, Color, Hat, Torso, Arms, Legs, Feet, Player }

    [System.Serializable]
    public class CritterLook
    {
        public int species, size, color, hat, torso, arms, legs, feet;

        public static readonly string[] Species = { "YETI", "SKELETON", "CHIHUAHUA", "SHARC", "SLIME" };
        public static readonly string[] Sizes = { "SMALL", "MEDIUM", "LARGE" };

        /// <summary>The one color list: bodies can be any of them; player colors (floor ring, HUD, locker) only the ones
        /// marked 'player' (bright enough to spot on the floor and the HUD). Add new colors at the end: saves store the index.</summary>
        public static readonly (string name, Color color, bool player)[] Colors =
        {
            ("SKY", new Color(0.25f, 0.75f, 1f), true),
            ("BUBBLEGUM", new Color(1f, 0.42f, 0.7f), true),
            ("TANGERINE", new Color(1f, 0.62f, 0.18f), true),
            ("LILAC", new Color(0.68f, 0.45f, 1f), true),
            ("MINT", new Color(0.45f, 0.95f, 0.7f), true),
            ("LEMON", new Color(1f, 0.9f, 0.3f), true),
            ("CRIMSON", new Color(0.55f, 0.06f, 0.1f), false),
            ("TEAL", new Color(0.1f, 0.7f, 0.68f), true),
            ("MOSS", new Color(0.5f, 0.65f, 0.28f), false),
            ("COCOA", new Color(0.58f, 0.38f, 0.24f), false),
            ("SNOW", new Color(0.94f, 0.95f, 0.98f), true),
            ("CHARCOAL", new Color(0.26f, 0.26f, 0.3f), false),
            ("LIME", new Color(0.65f, 1f, 0.25f), true),
            ("CHERRY", new Color(1f, 0.12f, 0.2f), true),
            ("HONEY", new Color(0.95f, 0.66f, 0.18f), true),
            ("GOLD", new Color(1f, 0.78f, 0.3f), true),
            ("GUNMETAL", new Color(0.3f, 0.34f, 0.39f), false),
            ("CONCRETE", new Color(0.58f, 0.58f, 0.56f), false),
            ("ASH", new Color(0.78f, 0.77f, 0.75f), false),
            ("TOXIC", new Color(0.55f, 1f, 0.12f), true),
            ("HAZMAT", new Color(1f, 0.85f, 0f), true),
            ("BLEACH", new Color(0.93f, 0.97f, 1f), false),
            ("CHROME", new Color(0.86f, 0.88f, 0.9f), false),
            ("IRIDESCENT", new Color(0.8f, 0.65f, 1f), false),
            ("BLACKLIGHT", new Color(0.92f, 0.9f, 1f), false),
        };

        /// <summary>How a body color is painted on the critter: matte fur/skin by default; metals shine,
        /// lab colors glow, and iridescent shifts hue as it moves.</summary>
        public struct BodyFinishInfo { public float smooth, metal; public Color glow; public bool iridescent; }

        public static BodyFinishInfo Finish(int colorIndex)
        {
            var f = new BodyFinishInfo { smooth = 0.15f, metal = 0f, glow = Color.black };
            switch (Colors[Mathf.Clamp(colorIndex, 0, Colors.Length - 1)].name)
            {
                case "GOLD": f.smooth = 0.7f; f.metal = 1f; break;
                case "GUNMETAL": f.smooth = 0.6f; f.metal = 0.9f; break;
                case "CHROME": f.smooth = 0.95f; f.metal = 1f; break;
                case "IRIDESCENT": f.smooth = 0.85f; f.metal = 0.5f; f.iridescent = true; break;
                case "TOXIC": f.smooth = 0.4f; f.glow = new Color(0.35f, 1f, 0.1f) * 0.7f; break;
                case "BLACKLIGHT": f.smooth = 0.3f; f.glow = new Color(0.55f, 0.45f, 1f) * 0.9f; break;
                case "HAZMAT": f.smooth = 0.45f; break; // glossy safety plastic
            }
            return f;
        }

        public BodyFinishInfo BodyFinish => Finish(color);

        public static readonly string[] Hats = { "NONE", "BASEBALL CAP", "COWBOY HAT", "SOMBRERO", "SHOWER CAP" };
        public static readonly string[] Torsos = { "NONE", "LAB COAT", "FLANNEL SHIRT", "BIKER JACKET" };
        public static readonly string[] Arms = { "NONE", "ARM FLOATIES" };
        public static readonly string[] Legs = { "NONE", "SWIM TRUNKS" };
        public static readonly string[] Feet = { "NONE", "CLOWN SHOES", "SNEAKERS", "MILITARY BOOTS" };

        public Color BodyColor => Colors[Mathf.Clamp(color, 0, Colors.Length - 1)].color;

        public CritterLook Clone() => (CritterLook)MemberwiseClone();

        public static CritterLook Default(int slot) => new CritterLook { color = JoinManager.DefaultColorIndex(slot) };

        static string Key(int slot) => "up_look" + slot;

        public static CritterLook Load(int slot)
        {
            var s = PlayerPrefs.GetString(Key(slot), "");
            var parts = s.Split(',');
            var l = new CritterLook();
            if (parts.Length == 8)
            {
                int.TryParse(parts[0], out l.species); int.TryParse(parts[1], out l.size); int.TryParse(parts[2], out l.color);
                int.TryParse(parts[3], out l.hat); int.TryParse(parts[4], out l.torso); int.TryParse(parts[5], out l.arms);
                int.TryParse(parts[6], out l.legs); int.TryParse(parts[7], out l.feet);
            }
            else if (parts.Length == 7) // before sizes (and the old placeholder species): keep color and outfit
            {
                int.TryParse(parts[1], out l.color); int.TryParse(parts[2], out l.hat);
                int.TryParse(parts[3], out l.torso); int.TryParse(parts[4], out l.arms); int.TryParse(parts[5], out l.legs);
                int.TryParse(parts[6], out l.feet);
            }
            else return Default(slot);
            l.Clamp();
            l.StripLocked(slot);
            return l;
        }

        public void Save(int slot)
        {
            PlayerPrefs.SetString(Key(slot), $"{species},{size},{color},{hat},{torso},{arms},{legs},{feet}");
            PlayerPrefs.Save();
        }

        public void Clamp()
        {
            species = Mathf.Clamp(species, 0, Species.Length - 1);
            size = Mathf.Clamp(size, 0, Sizes.Length - 1);
            color = Mathf.Clamp(color, 0, Colors.Length - 1);
            hat = Mathf.Clamp(hat, 0, Hats.Length - 1);
            torso = Mathf.Clamp(torso, 0, Torsos.Length - 1);
            arms = Mathf.Clamp(arms, 0, Arms.Length - 1);
            legs = Mathf.Clamp(legs, 0, Legs.Length - 1);
            feet = Mathf.Clamp(feet, 0, Feet.Length - 1);
        }

        public int Get(Part p) =>
            p == Part.Species ? species : p == Part.Size ? size : p == Part.Color ? color : p == Part.Hat ? hat : p == Part.Torso ? torso :
            p == Part.Arms ? arms : p == Part.Legs ? legs : feet;

        public void Set(Part p, int v)
        {
            switch (p)
            {
                case Part.Species: species = v; break;
                case Part.Size: size = v; break;
                case Part.Color: color = v; break;
                case Part.Hat: hat = v; break;
                case Part.Torso: torso = v; break;
                case Part.Arms: arms = v; break;
                case Part.Legs: legs = v; break;
                default: feet = v; break;
            }
        }

        public static string[] Names(Part p)
        {
            switch (p)
            {
                case Part.Species: return Species;
                case Part.Size: return Sizes;
                case Part.Hat: return Hats;
                case Part.Torso: return Torsos;
                case Part.Arms: return Arms;
                case Part.Legs: return Legs;
                case Part.Feet: return Feet;
                default:
                    var n = new string[Colors.Length];
                    for (int i = 0; i < n.Length; i++) n[i] = Colors[i].name;
                    return n;
            }
        }

        /// <summary>Anything this player hasn't unlocked (yet, or any more) goes back to the default.</summary>
        public void StripLocked(int slot)
        {
            foreach (Part p in System.Enum.GetValues(typeof(Part)))
                if (!Progress.IsUnlocked(slot, p, Get(p))) Set(p, 0);
        }
    }
}
