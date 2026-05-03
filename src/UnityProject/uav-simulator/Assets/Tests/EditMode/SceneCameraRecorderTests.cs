using System;
using System.IO;
using NUnit.Framework;
using UavSimulator.Recording;
using UnityEngine;

namespace UavSimulator.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="SceneCameraRecorder"/>. We avoid PlayMode by driving the
    /// state machine through the public <see cref="SceneCameraRecorder.Tick"/> hook with
    /// <c>capture: false</c> — this exercises every branch (timing, auto-stop, frame counting)
    /// without needing a real <see cref="Camera"/> render or ffmpeg.
    /// </summary>
    public sealed class SceneCameraRecorderTests
    {
        private GameObject _go;
        private SceneCameraRecorder _recorder;
        private Camera _cam;
        private string _outputPath;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("recorder-host");
            _recorder = _go.AddComponent<SceneCameraRecorder>();

            // A real Camera component, attached to the same GO. We never call Render() in tests
            // (Tick capture:false), but the recorder validates the field is non-null.
            _cam = _go.AddComponent<Camera>();
            _recorder.sourceCamera = _cam;

            _outputPath = Path.Combine(Path.GetTempPath(),
                "scene-camera-recorder-tests-" + Guid.NewGuid().ToString("N") + ".mp4");
        }

        [TearDown]
        public void TearDown()
        {
            if (_recorder != null && _recorder.IsRecording)
            {
                _recorder.StopRecording();
            }
            if (_go != null)
            {
                UnityEngine.Object.DestroyImmediate(_go);
            }
            // Cleanup any artifacts the recorder may have left on disk.
            TryDelete(_outputPath);
            var frameDir = _outputPath + "_frames";
            if (Directory.Exists(frameDir))
            {
                try { Directory.Delete(frameDir, recursive: true); }
                catch { /* best effort */ }
            }
        }

        [Test]
        public void StartRecording_SetsIsRecordingTrue()
        {
            Assert.That(_recorder.IsRecording, Is.False, "precondition: not recording");

            _recorder.StartRecording(_outputPath);

            Assert.That(_recorder.IsRecording, Is.True);
            Assert.That(_recorder.ElapsedSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void StopRecording_AfterStart_SetsIsRecordingFalse()
        {
            _recorder.StartRecording(_outputPath);
            Assert.That(_recorder.IsRecording, Is.True);

            // No frames captured (Tick never ran) → StopRecording skips ffmpeg with a warning,
            // which is fine for the state-machine assertion.
            _recorder.StopRecording();

            Assert.That(_recorder.IsRecording, Is.False);
        }

        [Test]
        public void StartRecording_WithMaxDuration_AutoStopsAfterDuration()
        {
            _recorder.fps = 10;
            _recorder.StartRecording(_outputPath, maxDurationSec: 0.3f);
            Assert.That(_recorder.IsRecording, Is.True);

            // Drive 4 ticks of 0.1s = 0.4s simulated time. Auto-stop should trigger by tick 3.
            for (int i = 0; i < 4 && _recorder.IsRecording; i++)
            {
                _recorder.Tick(0.1f, capture: false);
            }

            Assert.That(_recorder.IsRecording, Is.False, "should auto-stop after maxDurationSec");
            Assert.That(_recorder.ElapsedSeconds, Is.GreaterThanOrEqualTo(0.3f));
        }

        [Test]
        public void StartRecording_WithoutSourceCamera_Throws()
        {
            _recorder.sourceCamera = null;

            Assert.That(
                () => _recorder.StartRecording(_outputPath),
                Throws.InvalidOperationException);
            Assert.That(_recorder.IsRecording, Is.False);
        }

        [Test]
        public void StartRecording_Twice_Throws()
        {
            _recorder.StartRecording(_outputPath);

            Assert.That(
                () => _recorder.StartRecording(_outputPath),
                Throws.InvalidOperationException,
                "double-start should be rejected");
        }

        [Test]
        public void StopRecording_WhenIdle_IsNoop()
        {
            Assert.That(_recorder.IsRecording, Is.False);

            // Should not throw.
            _recorder.StopRecording();

            Assert.That(_recorder.IsRecording, Is.False);
        }

        [Test]
        public void Tick_AdvancesElapsedSeconds()
        {
            _recorder.StartRecording(_outputPath);

            _recorder.Tick(0.05f, capture: false);
            _recorder.Tick(0.05f, capture: false);

            Assert.That(_recorder.ElapsedSeconds, Is.EqualTo(0.1f).Within(1e-4f));
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* best effort cleanup */ }
        }
    }
}
