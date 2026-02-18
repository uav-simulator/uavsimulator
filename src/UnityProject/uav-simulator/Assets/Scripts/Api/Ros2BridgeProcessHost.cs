using System;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace UavSimulator.Api
{
    public sealed class Ros2BridgeProcessHost : MonoBehaviour
    {
        [SerializeField] private bool autoStart = false;
        [SerializeField] private string pythonExecutable = "python3";
        [SerializeField] private string scriptPathOverride = string.Empty;
        [SerializeField] private string rosNamespace = "/uavsim/ks0223";
        [SerializeField] private float loopRateHz = 15f;
        [SerializeField] private bool resetOnStart = true;

        private Process process;

        private void Start()
        {
            if (!autoStart)
            {
                return;
            }

            TryStartBridgeProcess();
        }

        public void StartBridgeNow()
        {
            TryStartBridgeProcess();
        }

        private void OnDestroy()
        {
            StopBridgeProcess();
        }

        private void TryStartBridgeProcess()
        {
            if (process != null && !process.HasExited)
            {
                return;
            }

            var scriptPath = ResolveScriptPath();
            if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            {
                UnityEngine.Debug.LogWarning($"[Ros2BridgeProcessHost] ROS2 bridge script not found: '{scriptPath}'");
                return;
            }

            var baseUrl = ResolveBaseUrl();
            var safeRate = Mathf.Max(1f, loopRateHz);
            var rateText = safeRate.ToString("0.###", CultureInfo.InvariantCulture);
            var args = $"\"{scriptPath}\" --base-url \"{baseUrl}\" --namespace \"{rosNamespace}\" --rate-hz {rateText}";
            if (resetOnStart)
            {
                args += " --reset-on-start";
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = string.IsNullOrWhiteSpace(pythonExecutable) ? "python3" : pythonExecutable,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Directory.GetCurrentDirectory(),
                };

                process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true,
                };
                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        UnityEngine.Debug.Log($"[Ros2Bridge] {e.Data}");
                    }
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        UnityEngine.Debug.LogWarning($"[Ros2Bridge] {e.Data}");
                    }
                };
                process.Exited += (_, _) =>
                {
                    UnityEngine.Debug.Log("[Ros2BridgeProcessHost] ROS2 bridge process exited.");
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                UnityEngine.Debug.Log($"[Ros2BridgeProcessHost] Started ROS2 bridge process: {startInfo.FileName} {args}");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[Ros2BridgeProcessHost] Failed to start ROS2 bridge process: {ex.Message}");
                StopBridgeProcess();
            }
        }

        private void StopBridgeProcess()
        {
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(1000);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[Ros2BridgeProcessHost] Failed to stop ROS2 bridge process: {ex.Message}");
            }
            finally
            {
                process.Dispose();
                process = null;
            }
        }

        private string ResolveBaseUrl()
        {
            var apiHost = FindFirstObjectByType<HttpJsonApiHost>();
            var port = apiHost != null ? apiHost.Port : 8000;
            return $"http://127.0.0.1:{port}";
        }

        private string ResolveScriptPath()
        {
            if (!string.IsNullOrWhiteSpace(scriptPathOverride))
            {
                return scriptPathOverride;
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.GetFullPath(Path.Combine(projectRoot, "..", "..", "..", "python", "bridges", "ros2_bridge.py"));
        }
    }
}
