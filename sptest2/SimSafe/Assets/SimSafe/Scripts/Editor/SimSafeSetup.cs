using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SimSafe.EditorTools
{
    /// <summary>
    /// One-click project setup and build entry points.
    ///   SimSafe > 1. Setup Project     creates materials, the Main scene, build settings and input/WebGL settings
    ///   SimSafe > Build > ...          Windows 64-bit and WebGL builds (also usable from the command line with -executeMethod)
    /// </summary>
    public static class SimSafeSetup
    {
        const string Root = "Assets/SimSafe";
        const string MatDir = Root + "/Resources/SimSafe";
        const string SceneDir = Root + "/Scenes";
        const string ScenePath = SceneDir + "/Main.unity";

        [MenuItem("SimSafe/1. Setup Project")]
        public static void Setup()
        {
            EnsureFolder(Root, "Scenes");
            EnsureFolder(Root + "/Resources", "SimSafe");

            // Base materials are saved as assets so builds include the right shaders and variants.
            CreateMaterial("BaseLit", "Universal Render Pipeline/Lit", "Standard");
            CreateMaterial("BaseUnlit", "Universal Render Pipeline/Unlit", "Unlit/Color");
            CreateMaterial("Sky", "Skybox/Procedural", "Skybox/Procedural");

            // An empty scene is enough: SimSafe builds its world at runtime (see Bootstrap).
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.productName = "SimSafe";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.runInBackground = false;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;     // works on any static host without special headers
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;

            bool restart = EnableBothInputHandlers();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string msg = "SimSafe setup complete.\n\nPress Play to run the prototype.";
            if (restart) msg += "\n\nThe Active Input Handling setting was changed to 'Both'. Unity will ask to restart the editor: accept, then press Play.";
            EditorUtility.DisplayDialog("SimSafe", msg, "OK");
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        static void CreateMaterial(string name, string shaderName, string fallback)
        {
            string path = MatDir + "/" + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var shader = Shader.Find(shaderName) ?? Shader.Find(fallback);
            if (shader == null) { Debug.LogWarning("[SimSafe] No shader found for " + name); return; }
            AssetDatabase.CreateAsset(new Material(shader), path);
        }

        /// <summary>SimSafe uses the classic Input class; this lets it coexist with the Input System package.</summary>
        static bool EnableBothInputHandlers()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return false;
            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null || prop.intValue == 2 || prop.intValue == 0) return false;   // 0 = old only (fine), 2 = both
            prop.intValue = 2;
            so.ApplyModifiedProperties();
            return true;
        }

        [MenuItem("SimSafe/Build/Windows 64-bit")]
        public static void BuildWindows() { Build(BuildTarget.StandaloneWindows64, "Builds/Windows/SimSafe.exe"); }

        [MenuItem("SimSafe/Build/WebGL")]
        public static void BuildWebGL() { Build(BuildTarget.WebGL, "Builds/WebGL"); }

        static void Build(BuildTarget target, string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[SimSafe] No scenes in Build Settings. Run SimSafe > 1. Setup Project first.");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            var options = new BuildPlayerOptions { scenes = scenes, locationPathName = path, target = target, options = BuildOptions.None };
            var report = BuildPipeline.BuildPlayer(options);
            bool ok = report.summary.result == BuildResult.Succeeded;
            Debug.Log("[SimSafe] " + target + " build " + report.summary.result + " -> " + path + " (" + report.summary.totalSize / (1024 * 1024) + " MB)");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
