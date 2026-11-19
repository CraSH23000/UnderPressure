using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// What a critter looks like: species, body colour, hat, torso and feet. Changed in the closets,
    /// remembered per player slot between sessions. (The floor ring always stays the player's identity colour.)
    /// </summary>
    /// <summary>The parts of a critter you can change in the closets.</summary>
    public enum Part { Species, Color, Hat, Torso, Arms, Legs, Feet }

    [System.Serializable]
    public class CritterLook
    {
        public int species, color, hat, torso, arms, legs, feet;

        public static readonly string[] Species = { "BLORB", "PEEPER", "SHARC" };

        public static readonly (string name, Color color)[] Colors =
        {
            ("SKY", new Color(0.25f, 0.75f, 1f)),
            ("BUBBLEGUM", new Color(1f, 0.42f, 0.7f)),
            ("TANGERINE", new Color(1f, 0.62f, 0.18f)),
            ("LILAC", new Color(0.68f, 0.45f, 1f)),
            ("MINT", new Color(0.45f, 0.95f, 0.7f)),
            ("LEMON", new Color(1f, 0.9f, 0.3f)),
            ("CRIMSON", new Color(0.9f, 0.2f, 0.25f)),
            ("TEAL", new Color(0.1f, 0.65f, 0.65f)),
            ("MOSS", new Color(0.5f, 0.65f, 0.28f)),
            ("COCOA", new Color(0.58f, 0.38f, 0.24f)),
            ("SNOW", new Color(0.94f, 0.95f, 0.98f)),
            ("CHARCOAL", new Color(0.26f, 0.26f, 0.3f)),
        };

        public static readonly string[] Hats = { "NONE", "BASEBALL CAP", "COWBOY HAT", "SOMBRERO", "SHOWER CAP" };
        public static readonly string[] Torsos = { "NONE", "LAB COAT", "FLANNEL SHIRT", "BIKER JACKET" };
        public static readonly string[] Arms = { "NONE", "ARM FLOATIES" };
        public static readonly string[] Legs = { "NONE", "SWIM TRUNKS" };
        public static readonly string[] Feet = { "NONE", "CLOWN SHOES", "SNEAKERS", "MILITARY BOOTS" };

        public Color BodyColor => Colors[Mathf.Clamp(color, 0, Colors.Length - 1)].color;

        public CritterLook Clone() => (CritterLook)MemberwiseClone();

        public static CritterLook Default(int slot) => new CritterLook { color = slot % 4 };

        static string Key(int slot) => "up_look" + slot;

        public static CritterLook Load(int slot)
        {
            var s = PlayerPrefs.GetString(Key(slot), "");
            var parts = s.Split(',');
            var l = new CritterLook();
            if (parts.Length == 5) // older save (before arms/legs)
            {
                int.TryParse(parts[0], out l.species); int.TryParse(parts[1], out l.color); int.TryParse(parts[2], out l.hat);
                int.TryParse(parts[3], out l.torso); int.TryParse(parts[4], out l.feet);
            }
            else if (parts.Length == 7)
            {
                int.TryParse(parts[0], out l.species); int.TryParse(parts[1], out l.color); int.TryParse(parts[2], out l.hat);
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
            PlayerPrefs.SetString(Key(slot), $"{species},{color},{hat},{torso},{arms},{legs},{feet}");
            PlayerPrefs.Save();
        }

        public void Clamp()
        {
            species = Mathf.Clamp(species, 0, Species.Length - 1);
            color = Mathf.Clamp(color, 0, Colors.Length - 1);
            hat = Mathf.Clamp(hat, 0, Hats.Length - 1);
            torso = Mathf.Clamp(torso, 0, Torsos.Length - 1);
            arms = Mathf.Clamp(arms, 0, Arms.Length - 1);
            legs = Mathf.Clamp(legs, 0, Legs.Length - 1);
            feet = Mathf.Clamp(feet, 0, Feet.Length - 1);
        }

        public int Get(Part p) =>
            p == Part.Species ? species : p == Part.Color ? color : p == Part.Hat ? hat : p == Part.Torso ? torso :
            p == Part.Arms ? arms : p == Part.Legs ? legs : feet;

        public void Set(Part p, int v)
        {
            switch (p)
            {
                case Part.Species: species = v; break;
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
