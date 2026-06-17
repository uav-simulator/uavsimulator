using NUnit.Framework;
using System.Reflection;
using UavSimulator.Contracts;
using UavSimulator.Vehicles;
using UnityEngine;

namespace UavSimulator.Tests.EditMode
{
    public sealed class Ks0223VehicleControlTests
    {
        [Test]
        public void ApplyControl_NonZeroThrottle_WakesSleepingBody()
        {
            var go = new GameObject("ks0223-wake-test");
            try
            {
                var body = go.AddComponent<Rigidbody>();
                var vehicle = go.AddComponent<Ks0223Vehicle>();
                typeof(Ks0223Vehicle)
                    .GetField("body", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(vehicle, body);

                body.Sleep();
                Assert.IsTrue(body.IsSleeping());

                vehicle.ApplyControl(new ControlCommand
                {
                    throttle = 0.55f,
                    steer = 0f,
                    brake = 0f,
                    extensions = new ConfigKeyValue[0],
                });

                Assert.IsFalse(body.IsSleeping());
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void UseCityCarPresentationProfile_DisablesGravityAndFreezesHeight()
        {
            var go = new GameObject("ks0223-city-profile-test");
            try
            {
                var body = go.AddComponent<Rigidbody>();
                var vehicle = go.AddComponent<Ks0223Vehicle>();
                typeof(Ks0223Vehicle)
                    .GetField("body", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(vehicle, body);

                body.useGravity = true;
                body.constraints = RigidbodyConstraints.None;

                vehicle.UseCityCarPresentationProfile();

                Assert.IsFalse(body.useGravity);
                Assert.IsTrue((body.constraints & RigidbodyConstraints.FreezePositionY) != 0);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CityCarChaseCamera_UsesHighOverCarPresentationPose()
        {
            var go = new GameObject("ks0223-city-chase-camera-test");
            try
            {
                var body = go.AddComponent<Rigidbody>();
                var vehicle = go.AddComponent<Ks0223Vehicle>();
                typeof(Ks0223Vehicle)
                    .GetField("body", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(vehicle, body);

                typeof(Ks0223Vehicle)
                    .GetMethod("EnsureFrontCamera", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(vehicle, null);

                vehicle.UseCityCarPresentationProfile();
                vehicle.ApplyVehicleConfig(new[] { new ConfigKeyValue { key = "camera.mode", value = "chase" } });

                var camera = go.GetComponentInChildren<Camera>(includeInactive: true);
                Assert.NotNull(camera, "City car chase mode should create a camera sensor.");
                Assert.GreaterOrEqual(camera.transform.localPosition.y, 3.6f);
                Assert.GreaterOrEqual(camera.transform.localRotation.eulerAngles.x, 25f);
                Assert.LessOrEqual(camera.fieldOfView, 64f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
