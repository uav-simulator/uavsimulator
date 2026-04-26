using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace UavSimulator.EditorTools
{
    public static class RuntimeBuildPipeline
    {
        private const string DefaultScenePath = "Assets/Scenes/TrackScence.unity";

        public static void BuildMacOsRuntime() =>
            BuildRuntime(BuildTarget.StandaloneOSX,
                Path.Combine("build", "runtime", "macos", "uav-simulator.app"));

        public static void BuildWindowsRuntime() =>
            BuildRuntime(BuildTarget.StandaloneWindows64,
                Path.Combine("build", "runtime", "windows", "uav-simulator.exe"));

        public static void BuildLinuxRuntime() =>
            BuildRuntime(BuildTarget.StandaloneLinux64,
                Path.Combine("build", "runtime", "linux", "uav-simulator"));

        private static void BuildRuntime(BuildTarget target, string defaultOutput)
        {
            RuntimeShaderAssetSeeder.EnsureRuntimeShaderAssets();
            PluginCatalogSeeder.SyncBuiltinPluginCatalog();

            var outputPath = Environment.GetEnvironmentVariable("RUSIM_BUILD_OUTPUT");
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = defaultOutput;
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
                target = target,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Runtime build failed for {target}: {summary.result}, " +
                    $"errors={summary.totalErrors}, warnings={summary.totalWarnings}.");
            }

            UnityEngine.Debug.Log(
                $"[RuntimeBuildPipeline] {target} build completed: {absoluteOutput}, " +
                $"size={summary.totalSize} bytes, time={summary.totalTime}.");
        }
    }
}
