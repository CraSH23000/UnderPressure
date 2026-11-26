using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// Per-song settings for recorded stems: when each variant is allowed to play. Lives in the song's own folder as
    /// Resources/Songs/&lt;song&gt;/Song.asset (Music loads it by that path; no Song asset = every variant always
    /// allowed). Use the "Scan Folder" button in its inspector to add the folder's layer_variant files.
    /// </summary>
    [CreateAssetMenu(menuName = "Under Pressure/Song", fileName = "Song")]
    public class SongDef : ScriptableObject
    {
        public const string AssetName = "Song";

        [System.Serializable]
        public class Variant
        {
            [Tooltip("The clip's file name without extension, e.g. synth_005.")]
            public string clip;
            [Range(0f, 1f)] [Tooltip("Only picked once the music grime is at least this.")]
            public float minGrime = 0f;
            [Range(0f, 1f)] [Tooltip("Retired once the music grime goes above this.")]
            public float maxGrime = 1f;
            [Min(0f)] [Tooltip("How likely it is to be picked among the allowed variants (0 = never).")]
            public float weight = 1f;

            public bool Allowed(float grime) => weight > 0f && grime >= minGrime - 1e-4f && grime <= maxGrime + 1e-4f;
        }

        [System.Serializable]
        public class Layer
        {
            public string name;
            public List<Variant> variants = new List<Variant>();
        }

        public List<Layer> layers = new List<Layer>();

        /// <summary>The settings for one clip, or null when it isn't listed (then it's always allowed).</summary>
        public Variant Find(string clipName)
        {
            foreach (var l in layers)
                if (l != null && l.variants != null)
                    foreach (var v in l.variants)
                        if (v != null && string.Equals(v.clip, clipName, System.StringComparison.OrdinalIgnoreCase)) return v;
            return null;
        }

        public static SongDef Load(string song) => Resources.Load<SongDef>("Songs/" + song + "/" + AssetName);
    }
}
