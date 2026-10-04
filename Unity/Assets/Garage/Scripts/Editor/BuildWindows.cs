using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Garage.Unity.EditorTools
{
    /// <summary>
    /// Garage/Build/Windows x64: builds the player into <c>Builds/Windows/Taller.exe</c> (repo root, ignored by git).
    /// Missing scenes are generated first. Also callable from the command line
    /// (<c>-executeMethod Garage.Unity.EditorTools.BuildWindows.CommandLine [-buildPath dir] [-development]</c>),
    /// which exits with code 1 on failure; see <c>tools/build-windows.ps1</c> / <c>.sh</c>.
    /// </summary>
    public static class BuildWindows
    {
        public const string ExeName = "Taller.exe";

        public static string DefaultDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Builds", "Windows"));

        private static readonly string[] Scenes = { OtherSceneBuilders.MainMenuPath, WorkshopSceneBuilder.ScenePath, OtherSceneBuilders.DynoPath };

        [MenuItem("Garage/Build/Windows x64", priority = 50)]
        public static void Menu()
        {
            BuildReport r = Build(DefaultDir, false);
            bool ok = r != null && r.summary.result == BuildResult.Succeeded;
            EditorUtility.DisplayDialog("Build Windows x64", ok ? "Build terminada en\n" + DefaultDir : "La build ha fallado. Mira la consola.", "OK");
        }

        [MenuItem("Garage/Build/Windows x64 (Development)", priority = 51)]
        public static void MenuDev() => Build(DefaultDir, true);

        /// <summary>Entry point for batch mode.</summary>
        public static void CommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string dir = DefaultDir;
            bool dev = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-buildPath" && i + 1 < args.Length)
                {
                    dir = Path.GetFullPath(args[i + 1]);
                }
                else if (args[i] == "-development")
                {
                    dev = true;
                }
            }

            BuildReport r = Build(dir, dev);
            EditorApplication.Exit(r != null && r.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>Builds the three scenes for Windows 64-bit.</summary>
        public static BuildReport Build(string dir, bool development)
        {
            if (!File.Exists(Path.Combine(Application.dataPath, "..", Scenes[0])) || !File.Exists(Path.Combine(Application.dataPath, "..", Scenes[1])) || !File.Exists(Path.Combine(Application.dataPath, "..", Scenes[2])))
            {
                Debug.Log("[Build] Faltan escenas: ejecutando Run All Setup Steps.");
                OtherSceneBuilders.All();
            }

            if (!Directory.Exists(Path.Combine(Application.streamingAssetsPath, "data", "base")))
            {
                Debug.LogError("[Build] Falta StreamingAssets/data: ejecuta tools/sync-sim-to-unity antes de compilar.");
                return null;
            }

            Directory.CreateDirectory(dir);
            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = Path.Combine(dir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report != null)
            {
                Debug.Log($"[Build] {report.summary.result}: {report.summary.totalSize / (1024 * 1024)} MB en {report.summary.totalTime} → {options.locationPathName}");
            }

            return report;
        }
    }
}
