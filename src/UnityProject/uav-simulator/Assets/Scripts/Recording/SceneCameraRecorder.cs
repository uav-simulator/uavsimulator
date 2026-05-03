using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UavSimulator.Recording
{
    /// <summary>
    /// Records MP4 video from an arbitrary Unity Camera (e.g. third-person showcase camera,
    /// not just the observation feed). Closes the third-person recorder TODO from
    /// <c>memory/project_third_person_recorder.md</c>.
    ///
    /// Implementation strategy:
    ///   Path A (preferred, NOT YET WIRED): Unity Recorder package (com.unity.recorder).
    ///     The package is currently NOT installed in Packages/manifest.json (checked 2026-05-03),
    ///     so this code path is intentionally absent. A later developer can add a
    ///     <c>#if UNITY_RECORDER_INSTALLED</c> block here that uses RecorderController +
    ///     MovieRecorderSettings without changing the public API.
    ///   Path B (active): per-frame <see cref="RenderTexture"/> -> <see cref="Texture2D.ReadPixels"/> ->
    ///     PNG dump under <c>&lt;outputPath&gt;_frames/</c>, then on Stop invoke <c>ffmpeg</c> to
    ///     stitch frames into MP4. Requires <c>ffmpeg</c> in PATH; otherwise StopRecording leaves
    ///     PNG frames on disk and logs an error.
    ///
    /// State machine: Idle -> Recording -> Idle. Cannot double-start; cannot stop when idle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneCameraRecorder : MonoBehaviour
    {
        [Tooltip("Camera to capture. If null when StartRecording is called, an error is thrown.")]
        public Camera sourceCamera;

        [Tooltip("Target frame rate of the output video (frames per second).")]
        public int fps = 30;

        [Tooltip("Render resolution (width, height). Frames are captured at this size.")]
        public Vector2Int resolution = new Vector2Int(1920, 1080);

        [Tooltip("Reserved for Path A (Unity Recorder package). Currently has no effect; "
                 + "Path B (manual + ffmpeg) is always used.")]
        public bool useUnityRecorderIfAvailable = true;

        public bool IsRecording { get; private set; }
        public float ElapsedSeconds { get; private set; }

        private string _outputPath;
        private string _frameDir;
        private float _maxDurationSec;
        private int _frameIndex;
        private float _frameInterval;
        private float _nextCaptureTime;
        private RenderTexture _renderTexture;
        private Texture2D _readbackTexture;
        private Camera _previousTarget;
        private RenderTexture _previousActive;

        public void StartRecording(string outputPath, float maxDurationSec = -1f)
        {
            if (IsRecording)
            {
                throw new InvalidOperationException(
                    "SceneCameraRecorder is already recording; call StopRecording first.");
            }
            if (sourceCamera == null)
            {
                throw new InvalidOperationException(
                    "SceneCameraRecorder.sourceCamera is null; assign a Camera before StartRecording.");
            }
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("outputPath must not be empty.", nameof(outputPath));
            }
            if (fps <= 0)
            {
                throw new InvalidOperationException("fps must be positive.");
            }
            if (resolution.x <= 0 || resolution.y <= 0)
            {
                throw new InvalidOperationException("resolution must be positive on both axes.");
            }

            _outputPath = outputPath;
            _frameDir = outputPath + "_frames";
            _maxDurationSec = maxDurationSec;
            _frameIndex = 0;
            _frameInterval = 1f / fps;
            _nextCaptureTime = 0f;
            ElapsedSeconds = 0f;

            Directory.CreateDirectory(Path.GetDirectoryName(_outputPath) ?? ".");
            Directory.CreateDirectory(_frameDir);

            _renderTexture = new RenderTexture(resolution.x, resolution.y, 24)
            {
                name = "SceneCameraRecorder.RT",
            };
            _readbackTexture = new Texture2D(resolution.x, resolution.y, TextureFormat.RGB24, false);

            _previousTarget = null;
            _previousActive = null;

            IsRecording = true;
        }

        public void StopRecording()
        {
            if (!IsRecording)
            {
                return;
            }
            IsRecording = false;

            // Restore camera target (in case capture path mutated it).
            if (sourceCamera != null && _renderTexture != null && sourceCamera.targetTexture == _renderTexture)
            {
                sourceCamera.targetTexture = null;
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
            if (_readbackTexture != null)
            {
                Destroy(_readbackTexture);
                _readbackTexture = null;
            }

            EncodeFramesToMp4();
        }

        private void LateUpdate()
        {
            Tick(Time.unscaledDeltaTime, capture: true);
        }

        /// <summary>
        /// Public tick entry point. Production code drives this from <see cref="LateUpdate"/>;
        /// EditMode tests call it directly to validate state transitions without spinning up a
        /// PlayMode session or a live Camera.
        /// </summary>
        /// <param name="deltaTime">Seconds since previous tick.</param>
        /// <param name="capture">If false, skips the actual frame capture (no Camera needed).</param>
        public void Tick(float deltaTime, bool capture)
        {
            if (!IsRecording)
            {
                return;
            }

            ElapsedSeconds += deltaTime;

            if (_maxDurationSec > 0f && ElapsedSeconds >= _maxDurationSec)
            {
                StopRecording();
                return;
            }

            if (ElapsedSeconds + 1e-6f < _nextCaptureTime)
            {
                return;
            }
            _nextCaptureTime += _frameInterval;

            if (capture)
            {
                CaptureFrame();
            }
            else
            {
                _frameIndex++;
            }
        }

        private void CaptureFrame()
        {
            if (sourceCamera == null || _renderTexture == null || _readbackTexture == null)
            {
                return;
            }

            var prevTarget = sourceCamera.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                sourceCamera.targetTexture = _renderTexture;
                sourceCamera.Render();

                RenderTexture.active = _renderTexture;
                _readbackTexture.ReadPixels(new Rect(0, 0, resolution.x, resolution.y), 0, 0);
                _readbackTexture.Apply();
            }
            finally
            {
                sourceCamera.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
            }

            var bytes = _readbackTexture.EncodeToPNG();
            var path = Path.Combine(_frameDir, $"frame_{_frameIndex:D6}.png");
            File.WriteAllBytes(path, bytes);
            _frameIndex++;
        }

        private void EncodeFramesToMp4()
        {
            if (_frameIndex == 0)
            {
                Debug.LogWarning("[SceneCameraRecorder] No frames captured; skipping ffmpeg encode.");
                TryCleanupFrameDir();
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = string.Join(" ", new[]
                {
                    "-y",
                    "-framerate", fps.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-i", QuotePath(Path.Combine(_frameDir, "frame_%06d.png")),
                    "-c:v", "libx264",
                    "-pix_fmt", "yuv420p",
                    QuotePath(_outputPath),
                }),
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    throw new InvalidOperationException("Process.Start returned null.");
                }
                proc.WaitForExit();
                if (proc.ExitCode != 0)
                {
                    var stderr = proc.StandardError.ReadToEnd();
                    Debug.LogError($"[SceneCameraRecorder] ffmpeg exited with code {proc.ExitCode}: {stderr}");
                    return; // keep frames on disk for debugging
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[SceneCameraRecorder] Failed to invoke ffmpeg ({ex.GetType().Name}: {ex.Message}). "
                    + $"Frames left on disk at {_frameDir}. Ensure ffmpeg is in PATH.");
                return;
            }

            TryCleanupFrameDir();
        }

        private void TryCleanupFrameDir()
        {
            try
            {
                if (!string.IsNullOrEmpty(_frameDir) && Directory.Exists(_frameDir))
                {
                    Directory.Delete(_frameDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SceneCameraRecorder] Failed to clean up frame dir {_frameDir}: {ex.Message}");
            }
        }

        private static string QuotePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "\"\"";
            return "\"" + path.Replace("\"", "\\\"") + "\"";
        }

        private void OnDestroy()
        {
            if (IsRecording)
            {
                StopRecording();
            }
        }
    }
}
