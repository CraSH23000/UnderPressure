using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnderPressure
{
    /// <summary>
    /// Player-facing settings (the pause menu's Settings page), saved in PlayerPrefs.
    /// The usual indie "escape hatches" for graphics problems, plus audio and screen shake.
    /// </summary>
    public static class GameSettings
    {
        public static readonly string[] DisplayModes = { "Borderless", "Fullscreen", "Windowed" };
        public static readonly string[] AntiAliasNames = { "Off", "FXAA", "SMAA" };
        public static readonly float[] RenderScales = { 0.5f, 0.67f, 0.75f, 0.85f, 1f, 1.25f, 1.5f };

        public static int DisplayMode;     // index into DisplayModes
        public static int ResolutionIndex; // index into Resolutions()
        public static bool VSync = true;
        public static int AntiAlias = 2;
        public static int RenderScaleIndex = 4;
        public static float MasterVolume = 1f;
        public static float MusicVolume = 1f;
        public static float ScreenShake = 1f;

        static bool loaded;
        static List<Vector2Int> resolutions;

        public static List<Vector2Int> Resolutions()
        {
            if (resolutions != null) return resolutions;
            resolutions = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (!resolutions.Contains(v)) resolutions.Add(v);
            }
            if (resolutions.Count == 0) resolutions.Add(new Vector2Int(Screen.width, Screen.height));
            return resolutions;
        }

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            var res = Resolutions();
            int native = res.Count - 1;
            DisplayMode = PlayerPrefs.GetInt("up_display", 0);
            ResolutionIndex = Mathf.Clamp(PlayerPrefs.GetInt("up_res", native), 0, res.Count - 1);
            VSync = PlayerPrefs.GetInt("up_vsync", 1) == 1;
            AntiAlias = PlayerPrefs.GetInt("up_aa", 2);
            RenderScaleIndex = PlayerPrefs.GetInt("up_rscale", 4);
            MasterVolume = PlayerPrefs.GetFloat("up_master", 1f);
            MusicVolume = PlayerPrefs.GetFloat("up_music", 1f);
            ScreenShake = PlayerPrefs.GetFloat("up_shake", 1f);
        }

        public static void Save()
        {
            PlayerPrefs.SetInt("up_display", DisplayMode);
            PlayerPrefs.SetInt("up_res", ResolutionIndex);
            PlayerPrefs.SetInt("up_vsync", VSync ? 1 : 0);
            PlayerPrefs.SetInt("up_aa", AntiAlias);
            PlayerPrefs.SetInt("up_rscale", RenderScaleIndex);
            PlayerPrefs.SetFloat("up_master", MasterVolume);
            PlayerPrefs.SetFloat("up_music", MusicVolume);
            PlayerPrefs.SetFloat("up_shake", ScreenShake);
            PlayerPrefs.Save();
        }

        /// <summary>Apply everything except display mode/resolution (which can hitch, so only on change).</summary>
        public static void ApplyLight()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            AudioListener.volume = MasterVolume;
            var cam = Game.Cam != null ? Game.Cam.Cam : Camera.main;
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.antialiasing = AntiAlias == 0 ? AntialiasingMode.None :
                    AntiAlias == 1 ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
#if !UNITY_EDITOR
            // In the editor this would permanently edit your URP asset, so it only applies in builds.
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.renderScale = RenderScales[Mathf.Clamp(RenderScaleIndex, 0, RenderScales.Length - 1)];
#endif
        }

        public static void ApplyDisplay()
        {
            var res = Resolutions();
            var r = res[Mathf.Clamp(ResolutionIndex, 0, res.Count - 1)];
            var mode = DisplayMode == 0 ? FullScreenMode.FullScreenWindow : DisplayMode == 1 ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.Windowed;
            Screen.SetResolution(r.x, r.y, mode);
        }
    }
}
