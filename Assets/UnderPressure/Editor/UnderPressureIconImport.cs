using UnityEditor;

namespace UnderPressure.EditorTools
{
    /// <summary>Import settings for the UI icons in Resources/Icons: keep their real size (no power-of-two
    /// stretching), clean alpha edges, no compression smudges.</summary>
    public class UnderPressureIconImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Icons/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
