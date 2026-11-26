using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnderPressure.EditorTools
{
    /// <summary>Song asset inspector: a "Scan Folder" button that lists the layer_variant audio files next to it.</summary>
    [CustomEditor(typeof(SongDef))]
    public class SongDefEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var song = (SongDef)target;
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(song))?.Replace('\\', '/');
            bool inSongs = folder != null && folder.Contains("/Resources/Songs/");
            if (!inSongs)
                EditorGUILayout.HelpBox("Put this asset in a song's folder (Resources/Songs/<song>/) and keep it named \"Song\", or Music won't find it.", MessageType.Warning);
            else if (song.name != SongDef.AssetName)
                EditorGUILayout.HelpBox($"Rename this asset to \"{SongDef.AssetName}\", or Music won't find it.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(!inSongs))
                if (GUILayout.Button("Scan Folder (add new variants, flag missing ones)")) Scan(song, folder);

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        static void Scan(SongDef song, string folder)
        {
            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetDirectoryName(p).Replace('\\', '/') == folder) // this folder only
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => n.IndexOf('_') > 0)
                .OrderBy(n => n, System.StringComparer.Ordinal)
                .ToList();

            Undo.RecordObject(song, "Scan Song Folder");
            int added = 0;
            foreach (var clip in clips)
            {
                if (song.Find(clip) != null) continue;
                string layerName = clip.Substring(0, clip.IndexOf('_')).ToLowerInvariant();
                var layer = song.layers.FirstOrDefault(l => l != null && l.name == layerName);
                if (layer == null) { layer = new SongDef.Layer { name = layerName }; song.layers.Add(layer); }
                layer.variants.Add(new SongDef.Variant { clip = clip });
                layer.variants.Sort((a, b) => string.CompareOrdinal(a.clip, b.clip));
                added++;
            }
            song.layers.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            var present = new HashSet<string>(clips, System.StringComparer.OrdinalIgnoreCase);
            var missing = song.layers.SelectMany(l => l.variants).Where(v => !present.Contains(v.clip)).Select(v => v.clip).ToList();
            EditorUtility.SetDirty(song);
            Debug.Log($"Song '{Path.GetFileName(folder)}': added {added} variant(s)." +
                      (missing.Count > 0 ? $" Listed but no file (left in place, remove them if they're gone for good): {string.Join(", ", missing)}" : ""));
        }
    }
}
