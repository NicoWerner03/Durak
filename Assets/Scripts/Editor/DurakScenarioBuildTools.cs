using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace DurakGame.Editor
{
    public static class DurakScenarioBuildTools
    {
        public static void BuildWindowsScenarioRunner()
        {
            var outputPath = ReadCommandLineValue("-durakBuildOutput");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = "Builds/ScenarioRunner/DurakGameCodex.exe";
            }

            var fullOutputPath = Path.GetFullPath(outputPath);
            var outputDir = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var scenes = CollectEnabledScenes();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes found in Build Settings.");
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = fullOutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("Scenario build failed: " + report.summary.result);
            }

            UnityEngine.Debug.Log("Scenario build completed: " + fullOutputPath);
        }

        private static string[] CollectEnabledScenes()
        {
            var result = new List<string>();
            var scenes = EditorBuildSettings.scenes;
            for (var i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].enabled)
                {
                    result.Add(scenes[i].path);
                }
            }

            return result.ToArray();
        }

        private static string ReadCommandLineValue(string key)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return string.Empty;
        }
    }
}
