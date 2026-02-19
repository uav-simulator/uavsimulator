using System;
using UavSimulator.Contracts;
using UavSimulator.Tracks;
using UavSimulator.Vehicles;
using UnityEngine;

namespace UavSimulator.Plugins
{
    public static class BuiltinPluginFactory
    {
        public const string Ks0223VehicleId = "vehicle.ks0223.v1";
        public const string BasicArenaTrackId = "track.basic_arena.v1";

        public static PluginRegistrySnapshot CreateSnapshot()
        {
            var vehicle = ScriptableObject.CreateInstance<VehiclePluginDescriptor>();
            vehicle.id = Ks0223VehicleId;
            vehicle.displayName = "Keyestudio KS0223 (Runtime Fallback)";
            vehicle.description = "Built-in runtime fallback vehicle for local simulator validation.";
            vehicle.deviceContract = CreateKs0223Contract();

            var track = ScriptableObject.CreateInstance<TrackPluginDescriptor>();
            track.id = BasicArenaTrackId;
            track.displayName = "Basic Arena (Runtime Fallback)";
            track.description = "Built-in runtime fallback track with lane markings and boundaries.";
            track.parametersSchemaJson = "{\"type\":\"object\",\"properties\":{}}";

            return new PluginRegistrySnapshot(
                vehicles: new[] { vehicle },
                tracks: new[] { track });
        }

        public static bool TryCreateVehicleInstance(string descriptorId, Transform parent, out VehicleBase vehicle)
        {
            vehicle = null;
            if (!string.Equals(descriptorId, Ks0223VehicleId, StringComparison.Ordinal))
            {
                return false;
            }

            var root = new GameObject("KS0223Vehicle");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(0f, 0.2f, -6f);
            root.transform.rotation = Quaternion.identity;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.34f, 0.09f, 0.50f);
            body.transform.localPosition = new Vector3(0f, 0.05f, 0f);

            CreateDecorBlock(root.transform, "Hood", new Vector3(0.30f, 0.06f, 0.18f), new Vector3(0f, 0.09f, 0.15f));
            CreateDecorBlock(root.transform, "Cabin", new Vector3(0.22f, 0.08f, 0.19f), new Vector3(0f, 0.13f, -0.03f));
            CreateDecorBlock(root.transform, "RearDeck", new Vector3(0.30f, 0.05f, 0.12f), new Vector3(0f, 0.09f, -0.19f));
            CreateDecorBlock(root.transform, "Windshield", new Vector3(0.20f, 0.05f, 0.03f), new Vector3(0f, 0.14f, 0.07f), new Vector3(-22f, 0f, 0f));
            CreateDecorBlock(root.transform, "RearWindow", new Vector3(0.20f, 0.05f, 0.03f), new Vector3(0f, 0.14f, -0.11f), new Vector3(22f, 0f, 0f));

            var cameraPod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cameraPod.name = "CameraPod";
            cameraPod.transform.SetParent(root.transform, false);
            cameraPod.transform.localScale = new Vector3(0.02f, 0.03f, 0.02f);
            cameraPod.transform.localPosition = new Vector3(0f, 0.20f, 0.20f);
            DisableCollider(cameraPod);

            CreateWheel(root.transform, "WheelFL", new Vector3(-0.14f, 0.03f, 0.18f));
            CreateWheel(root.transform, "WheelFR", new Vector3(0.14f, 0.03f, 0.18f));
            CreateWheel(root.transform, "WheelRL", new Vector3(-0.14f, 0.03f, -0.18f));
            CreateWheel(root.transform, "WheelRR", new Vector3(0.14f, 0.03f, -0.18f));

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1.0f;
            rb.angularDamping = 1.5f;
            rb.linearDamping = 0.2f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            vehicle = root.AddComponent<Ks0223Vehicle>();
            return true;
        }

        public static bool TryCreateTrackInstance(string descriptorId, Transform parent, out TrackBase track)
        {
            track = null;
            if (!string.Equals(descriptorId, BasicArenaTrackId, StringComparison.Ordinal))
            {
                return false;
            }

            var root = new GameObject("BasicArenaTrack");
            root.transform.SetParent(parent, false);
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;

            track = root.AddComponent<BasicArenaTrack>();
            return true;
        }

        private static DeviceContractDescriptorAsset CreateKs0223Contract()
        {
            var asset = ScriptableObject.CreateInstance<DeviceContractDescriptorAsset>();
            asset.contractVersion = new ContractVersion(0, 1, 0);
            asset.descriptor = new DeviceContractDescriptor
            {
                deviceId = Ks0223VehicleId,
                deviceType = "ground_robot_differential",
                sensors = new[]
                {
                    new SensorDescriptor
                    {
                        id = "camera.front.rgb",
                        sensorType = "sensor.camera.rgb",
                        format = "jpeg",
                        unit = "pixel",
                        shape = new[] { 120, 160, 3 },
                        rateHz = 15f,
                    },
                    new SensorDescriptor
                    {
                        id = "speedometer",
                        sensorType = "sensor.speedometer",
                        format = "float32",
                        unit = "m/s",
                        shape = new[] { 1 },
                        rateHz = 20f,
                    },
                    new SensorDescriptor
                    {
                        id = "ultrasonic.front",
                        sensorType = "sensor.range",
                        format = "float32",
                        unit = "m",
                        shape = new[] { 1 },
                        rateHz = 10f,
                    },
                    new SensorDescriptor
                    {
                        id = "line_tracker.front",
                        sensorType = "sensor.line_tracker",
                        format = "float32",
                        unit = "norm",
                        shape = new[] { 5 },
                        rateHz = 30f,
                    },
                    new SensorDescriptor
                    {
                        id = "powertrain.estimate",
                        sensorType = "sensor.powertrain",
                        format = "float32",
                        unit = "mixed",
                        shape = new[] { 3 },
                        rateHz = 20f,
                    },
                },
                actuators = new[]
                {
                    new ActuatorDescriptor
                    {
                        id = "control.throttle_norm",
                        actuatorType = "vehicle_control.throttle",
                        unit = "norm",
                        min = -1f,
                        max = 1f,
                    },
                    new ActuatorDescriptor
                    {
                        id = "control.steer_norm",
                        actuatorType = "vehicle_control.steer",
                        unit = "norm",
                        min = -1f,
                        max = 1f,
                    },
                    new ActuatorDescriptor
                    {
                        id = "control.brake_norm",
                        actuatorType = "vehicle_control.brake",
                        unit = "norm",
                        min = 0f,
                        max = 1f,
                    },
                    new ActuatorDescriptor
                    {
                        id = "drive.left_pwm_norm",
                        actuatorType = "vehicle_control.left_pwm",
                        unit = "norm",
                        min = -1f,
                        max = 1f,
                    },
                    new ActuatorDescriptor
                    {
                        id = "drive.right_pwm_norm",
                        actuatorType = "vehicle_control.right_pwm",
                        unit = "norm",
                        min = -1f,
                        max = 1f,
                    },
                },
                observationSchemaJson = "{\"state\":[\"pose\",\"linearVelocity\",\"angularVelocity\",\"speed\"],\"camera\":\"camera/front/image_raw\",\"telemetry\":[\"sensor.speedometer.mps\",\"sensor.ultrasonic.front.m\",\"sensor.line_tracker.s1_norm\",\"sensor.line_tracker.s2_norm\",\"sensor.line_tracker.s3_norm\",\"sensor.line_tracker.s4_norm\",\"sensor.line_tracker.s5_norm\",\"power.battery.voltage_v\",\"power.battery.current_a\",\"power.motor.estimated_w\"]}",
                actionSchemaJson = "{\"base\":[\"throttle\",\"steer\",\"brake\"],\"extensions\":[\"drive.left_pwm_norm\",\"drive.right_pwm_norm\",\"control.throttle_norm\",\"control.steer_norm\",\"control.brake_norm\",\"camera.pan_norm\",\"camera.tilt_norm\"]}",
            };

            return asset;
        }

        private static void CreateWheel(Transform parent, string name, Vector3 localPosition)
        {
            var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = name;
            wheel.transform.SetParent(parent, false);
            wheel.transform.localScale = new Vector3(0.04f, 0.015f, 0.04f);
            wheel.transform.localPosition = localPosition;
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        private static void CreateDecorBlock(
            Transform parent,
            string name,
            Vector3 localScale,
            Vector3 localPosition,
            Vector3 localEuler = default)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localScale = localScale;
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.Euler(localEuler);
            DisableCollider(part);
        }

        private static void DisableCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(collider);
        }
    }
}
