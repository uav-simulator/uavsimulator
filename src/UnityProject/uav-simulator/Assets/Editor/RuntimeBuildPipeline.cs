using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace UavSimulator.EditorTools
{
    public static class RuntimeBuildPipeline
    {
        private const string DefaultScenePath = "Assets/Scenes/TrackScence.unity";

        public static void BuildMacOsRuntime()
        {
            var outputPath = Environment.GetEnvironmentVariable("RUSIM_BUILD_OUTPUT");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = Path.Combine("build", "runtime", "macos", "uav-simulator.app");
            }

            var scenePath = Environment.GetEnvironmentVariable("RUSIM_BUILD_SCENE");
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                scenePath = DefaultScenePath;
            }

            var absoluteOutput = Path.GetFullPath(outputPath);
            var outputDirectory = Path.GetDirectoryName(absoluteOutput);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = absoluteOutput,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Runtime build failed: {summary.result}, errors={summary.totalErrors}, warnings={summary.totalWarnings}.");
            }

            UnityEngine.Debug.Log(
                $"[RuntimeBuildPipeline] Build completed: {absoluteOutput}, size={summary.totalSize} bytes, time={summary.totalTime}.");
        }
    }
}
