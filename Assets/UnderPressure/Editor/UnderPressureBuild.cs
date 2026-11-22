using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnderPressure.EditorTools
{
    /// <summary>
    /// The game makes every material in code with Shader.Find, so a player build would otherwise strip the URP
    /// shaders it needs (nothing in the scene references them) and the level fails to build: an empty sky.
    /// Before each build this keeps one material per shader setup we use in a Resources folder, which pulls the
    /// shaders and exactly those keyword variants into the build.
    /// </summary>
    [InitializeOnLoad]
    public class UnderPressureBuild : IPreprocessBuildWithReport
    {
        // make the materials as soon as the project opens, so Play in the editor uses the same ones as a build
        static UnderPressureBuild()
        {
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(Dir + "/LitTransparent.mat")) Prepare();
            };
        }

        public const string Dir = "Assets/UnderPressure/Resources/UnderPressureShaderKeep";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            CheckVersion(report.summary.outputPath);
            Prepare();
        }

        // Build folders are named after the version (e.g. "Version 0.0.230"). Warn when that doesn't match
        // Player → Version, because that's what the HUD, pause menu and crash logs report.
        static void CheckVersion(string outputPath)
        {
            string version = PlayerSettings.bundleVersion;
            var m = System.Text.RegularExpressions.Regex.Match(outputPath ?? "", @"\d+\.\d+\.\d+");
            if (m.Success && m.Value != version)
                Debug.LogWarning($"[UnderPressure] Building to '{outputPath}', but Player → Version is {version}. " +
                                 $"The game will report v{version}. Update Project Settings → Player → Version if that's wrong.");
            else
                Debug.Log($"[UnderPressure] Building v{version}.");
        }

        [MenuItem("Under Pressure/Prepare Shaders For Build")]
        public static void PrepareMenu()
        {
            Prepare();
            Debug.Log("[UnderPressure] Shader keep-alive materials are in " + Dir + " (this also runs automatically before every build).");
        }

        public static void Prepare()
        {
            Directory.CreateDirectory(Dir);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (lit == null) { Debug.LogError("[UnderPressure] URP Lit shader not found. Is the project using URP?"); return; }

            Save("Lit", lit, m => { });
            Save("LitEmission", lit, m =>
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.white);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            });
            Save("LitTransparent", lit, m =>
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.white);
                m.renderQueue = (int)RenderQueue.Transparent;
            });
            if (unlit != null) Save("Unlit", unlit, m => { });
            if (particles != null)
                Save("ParticlesUnlit", particles, m =>
                {
                    m.SetFloat("_AlphaClip", 1f);
                    m.SetFloat("_Cutoff", 0.5f);
                    m.EnableKeyword("_ALPHATEST_ON");
                });
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void Save(string name, Shader shader, System.Action<Material> setup)
        {
            string path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = m == null;
            if (fresh) m = new Material(shader);
            else m.shader = shader;
            setup(m);
            if (fresh) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
        }
    }
}
