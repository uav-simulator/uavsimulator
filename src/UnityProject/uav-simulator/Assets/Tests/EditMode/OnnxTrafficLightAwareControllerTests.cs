using NUnit.Framework;
using UavSimulator.CityDemo;
using UavSimulator.Vehicles;
using UnityEngine;
using UnityEngine.TestTools;

namespace UavSimulator.Tests.EditMode
{
    /// <summary>
    /// Verifies the <see cref="IMovementGate"/> abstraction (Plan B.3):
    /// both the ground-truth raycast controller and the ONNX-inference
    /// controller implement the same gate contract, and the ONNX gate
    /// fails closed (does not brake) when no model file is present.
    /// </summary>
    public sealed class OnnxTrafficLightAwareControllerTests
    {
        [Test]
        public void TrafficLightAwareController_Implements_IMovementGate()
        {
            var go = new GameObject("ctrl-gt");
            try
            {
                var ctrl = go.AddComponent<TrafficLightAwareController>();
                Assert.IsTrue(ctrl is IMovementGate,
                    "TrafficLightAwareController must implement IMovementGate.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void OnnxTrafficLightAwareController_Implements_IMovementGate()
        {
            // Awake logs a warning when no ONNX model is present; that's expected.
            LogAssert.ignoreFailingMessages = true;
            var go = new GameObject("ctrl-onnx");
            try
            {
                var ctrl = go.AddComponent<OnnxTrafficLightAwareController>();
                Assert.IsTrue(ctrl is IMovementGate,
                    "OnnxTrafficLightAwareController must implement IMovementGate.");
            }
            finally
            {
                Object.DestroyImmediate(go);
                LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void OnnxTrafficLightAwareController_DefaultsToGreenWhenNoModel()
        {
            // With no ONNX model on disk the gate must default to a "Green"
            // verdict — no braking, intensity 0. This keeps the component
            // safe to drop into scenes before the classifier is trained.
            LogAssert.ignoreFailingMessages = true;
            var go = new GameObject("ctrl-onnx-default");
            try
            {
                var ctrl = go.AddComponent<OnnxTrafficLightAwareController>();
                bool brake = ctrl.ShouldBrake(out var intensity);
                Assert.IsFalse(brake, "Gate must not brake when no model is loaded.");
                Assert.AreEqual(0f, intensity, "Brake intensity must be 0 in the default state.");
                Assert.AreEqual("Green", ctrl.LastVerdict, "Default verdict must be 'Green'.");
            }
            finally
            {
                Object.DestroyImmediate(go);
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
