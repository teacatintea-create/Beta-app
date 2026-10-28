using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kosynka.EditorTools
{
    // Сборка Windows .exe: из меню Unity или в CI (game-ci передаёт -customBuildPath)
    public static class BuildScript
    {
        const string ScenePath = "Assets/Kosynka/Main.unity";

        [MenuItem("Kosynka/Собрать .exe для Windows")]
        public static void BuildFromMenu()
        {
            string path = EditorUtility.SaveFilePanel("Куда сохранить игру", "", "Kosynka", "exe");
            if (!string.IsNullOrEmpty(path)) Build(path);
        }

        // Точка входа для CI
        public static void BuildWindows()
        {
            string path = Arg("-customBuildPath") ?? "Build/Windows/Kosynka.exe";
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, "Kosynka.exe");
            bool ok = Build(path);
            if (!ok) EditorApplication.Exit(1);
        }

        static bool Build(string exePath)
        {
            EnsureScene();
            PlayerSettings.companyName = "Kosynka";
            PlayerSettings.productName = "Масляная косынка";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 820;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? ".");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log("Сборка: " + report.summary.result + ", размер " + report.summary.totalSize + " байт, путь " + exePath);
            return report.summary.result == BuildResult.Succeeded;
        }

        // Игра создаёт всё сама при запуске, сцене достаточно быть пустой
        static void EnsureScene()
        {
            if (File.Exists(ScenePath)) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
