using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnderPressure.EditorTools
{
    public static class UnderPressureMenu
    {
        const string SceneDir = "Assets/UnderPressure/Scenes";
        const string ScenePath = SceneDir + "/UnderPressure.unity";

        [MenuItem("Under Pressure/Create Playable Scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Directory.CreateDirectory(SceneDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("UnderPressure (Bootstrap)");
            go.AddComponent<GameBootstrap>();
            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            cam.AddComponent<Camera>();
            cam.AddComponent<AudioListener>();
            EditorSceneManager.SaveScene(scene, ScenePath);

            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath))
            {
                list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = list.ToArray();
            }
            Debug.Log("[UnderPressure] Scene created at " + ScenePath + ". Press Play!");
        }

        [MenuItem("Under Pressure/Create Config Asset")]
        public static void CreateConfig()
        {
            Directory.CreateDirectory("Assets/UnderPressure");
            var cfg = ScriptableObject.CreateInstance<GameConfig>();
            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/UnderPressure/GameConfig.asset");
            AssetDatabase.CreateAsset(cfg, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = cfg;
            var boot = Object.FindAnyObjectByType<GameBootstrap>();
            if (boot != null && boot.config == null)
            {
                Undo.RecordObject(boot, "Assign Under Pressure config");
                boot.config = cfg;
                EditorUtility.SetDirty(boot);
            }
            Debug.Log("[UnderPressure] Config created at " + path + " — tweak values in the Inspector.");
        }

        [MenuItem("Under Pressure/Reset Unlocks and Stats")]
        public static void ResetUnlocks()
        {
            if (!EditorUtility.DisplayDialog("Reset unlocks", "Wipe all saved stats and secret cosmetic unlocks for every player slot? (Looks are kept, minus anything that becomes locked.)", "Reset", "Cancel")) return;
            Progress.ResetAll();
            Debug.Log("[UnderPressure] Stats and unlocks reset.");
        }

        [MenuItem("Under Pressure/Log Unlock Progress")]
        public static void LogProgress()
        {
            for (int i = 0; i < JoinManager.MaxPlayers; i++) Debug.Log($"[UnderPressure] P{i + 1}\n" + Progress.Describe(i));
        }
    }
}
