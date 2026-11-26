using UnityEditor;
using UnityEngine;

namespace UnderPressure.EditorTools
{
    /// <summary>
    /// Import settings for song stems (Resources/Songs/&lt;song&gt;/layer_variant.wav). Stems are long, so they're
    /// kept Vorbis-compressed in memory and decoded while they play, with their audio loaded on demand (in the
    /// background) by Music rather than with the clip:
    /// "Decompress On Load" would decode minutes of audio per stem on the main thread at chamber start (a visible
    /// hitch, and ~10x the memory). Not Streaming: Music seeks every stem at once for its VHS stutter, which
    /// streamed clips do badly. Loose files right in Songs/ (risingSynth) are left alone.
    /// </summary>
    public class UnderPressureSongImport : AssetPostprocessor
    {
        const float Quality = 0.7f; // Vorbis quality: plenty for game music, a good deal smaller than 1.0

        void OnPreprocessAudio()
        {
            string p = assetPath.Replace('\\', '/');
            int i = p.IndexOf("/Resources/Songs/");
            if (i < 0 || p.IndexOf('/', i + "/Resources/Songs/".Length) < 0) return; // only files inside a song folder
            var ai = (AudioImporter)assetImporter;
            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.CompressedInMemory;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = Quality;
            s.preloadAudioData = false;
            ai.defaultSampleSettings = s;
            ai.loadInBackground = true; // Music loads each variant on demand ahead of the loop point, off the main thread
        }
    }
}
