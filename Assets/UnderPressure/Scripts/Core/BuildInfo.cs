using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// The game's version, from Project Settings → Player → Version (Application.version).
    /// Shown on the HUD and pause menu so playtesters can say which build they're on, and written as the first
    /// line of Player.log, which Unity's crash handler copies into every crash report folder.
    /// </summary>
    public static class BuildInfo
    {
        public static string Version => Application.version;

        /// <summary>"v0.0.230", or "v0.0.230 (editor)" in Play mode in the editor.</summary>
        public static string Label => "v" + Version + (Application.isEditor ? " (editor)" : "");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LogVersion()
        {
            Debug.Log($"[UnderPressure] Under Pressure {Label} · Unity {Application.unityVersion} · " +
                      $"{SystemInfo.operatingSystem} · {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}) · " +
                      $"{Screen.currentResolution.width}x{Screen.currentResolution.height}");
        }
    }
}
