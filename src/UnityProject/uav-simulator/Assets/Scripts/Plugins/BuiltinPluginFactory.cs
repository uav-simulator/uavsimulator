using System;
using UavSimulator.Contracts;
using UavSimulator.Tracks;
using UavSimulator.Vehicles;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UavSimulator.Plugins
{
    public static class BuiltinPluginFactory
    {
        public const string Ks0223VehicleId = "vehicle.ks0223.v1";
        public const string Ks0223ArcadeBlueVehicleId = "vehicle.ks0223.arcade.blue.v1";
        public const string Ks0223ArcadeRedVehicleId = "vehicle.ks0223.arcade.red.v1";
        public const string Ks0223ArcadeGrayVehicleId = "vehicle.ks0223.arcade.gray.v1";
        public const string Ks0223ArcadePurpleVehicleId = "vehicle.ks0223.arcade.purple.v1";
        public const string SimpleDroneVehicleId = "vehicle.drone.simple.v1";
        public const string BasicArenaTrackId = "track.basic_arena.v1";
        public const string RoadSystemArenaTrackId = "track.roadsystem_arena.v1";
        public const string RoadSystemRealisticTrackId = "track.roadsystem_realistic.v2";

        private const string PrometeoPrefabPath = "Assets/PROMETEO - Car Controller/Prefabs/Prometheus.prefab";
        private const string ArcadeBluePrefabPath = "Assets/ARCADE - FREE Racing Car/Prefabs (Meshes Only)/Free Racing Car Blue Variant.prefab";
        private const string ArcadeRedPrefabPath = "Assets/ARCADE - FREE Racing Car/Prefabs (Meshes Only)/Free Racing Car Red Variant.prefab";
        private const string ArcadeGrayPrefabPath = "Assets/ARCADE - FREE Racing Car/Prefabs (Meshes Only)/Free Racing Car Gray Variant.prefab";
        private const string ArcadePurplePrefabPath = "Assets/ARCADE - FREE Racing Car/Prefabs (Meshes Only)/Free Racing Car Purple Variant.prefab";
        private static readonly string[] SimpleDronePrefabCandidates =
        {
            "Assets/Simple Drone/Prefabs/Simple Drone.prefab",
            "Assets/Simple Drone/Prefabs/Drone.prefab",
            "Assets/Simple Drone/Prefabs/SimpleDrone.prefab",
            "Assets/ExternalModels/Simple Drone.prefab",
        };

        private const float TargetVehicleLength = 0.52f;
        private const float VehicleVisualGroundOffset = 0.01f;

        public static PluginRegistrySnapshot CreateSnapshot(PluginRegistrySource source = PluginRegistrySource.BuiltinFactory)
        {
            var vehicle = CreateVehicleDescriptor(
                Ks0223VehicleId,
                "Keyestudio KS0223 (Unity Simulator)",
                "Built-in KS0223 simulator profile with PROMETEO car body (fallback visuals if asset is unavailable).");
            var arcadeBlueVehicle = CreateVehicleDescriptor(
                Ks0223ArcadeBlueVehicleId,
                "Keyestudio KS0223 (Arcade Blue)",
                "KS0223 physics profile with ARCADE blue visual body.");
            var arcadeRedVehicle = CreateVehicleDescriptor(
                Ks0223ArcadeRedVehicleId,
                "Keyestudio KS0223 (Arcade Red)",
                "KS0223 physics profile with ARCADE red visual body.");
            var arcadeGrayVehicle = CreateVehicleDescriptor(
                Ks0223ArcadeGrayVehicleId,
                "Keyestudio KS0223 (Arcade Gray)",
                "KS0223 physics profile with ARCADE gray visual body.");
            var arcadePurpleVehicle = CreateVehicleDescriptor(
                Ks0223ArcadePurpleVehicleId,
                "Keyestudio KS0223 (Arcade Purple)",
                "KS0223 physics profile with ARCADE purple visual body.");
            var simpleDrone = CreateVehicleDescriptor(
                SimpleDroneVehicleId,
                "Simple Drone (Quadcopter)",
                "Simple quadcopter plugin with camera and state telemetry.");

            var track = ScriptableObject.CreateInstance<TrackPluginDescriptor>();
            track.id = BasicArenaTrackId;
            track.displayName = "Basic Arena (Runtime Fallback)";
            track.description = "Built-in runtime fallback track with lane markings and boundaries.";
            track.parametersSchemaJson = "{\"type\":\"object\",\"properties\":{}}";

            var roadSystemTrack = ScriptableObject.CreateInstance<TrackPluginDescriptor>();
            roadSystemTrack.id = RoadSystemArenaTrackId;
            roadSystemTrack.displayName = "RoadSystem Arena (Runtime)";
            roadSystemTrack.description = "RoadSystem spline-based arena with boundaries and markings.";
            roadSystemTrack.parametersSchemaJson = "{\"type\":\"object\",\"properties\":{}}";

            var roadSystemRealisticTrack = ScriptableObject.CreateInstance<TrackPluginDescriptor>();
            roadSystemRealisticTrack.id = RoadSystemRealisticTrackId;
            roadSystemRealisticTrack.displayName = "RoadSystem Realistic v2 (Runtime)";
            roadSystemRealisticTrack.description = "RoadSystem-based realistic track with curbs, start/finish markers and richer environment.";
            roadSystemRealisticTrack.parametersSchemaJson = "{\"type\":\"object\",\"properties\":{}}";

            return new PluginRegistrySnapshot(
                vehicles: new[] { vehicle, arcadeBlueVehicle, arcadeRedVehicle, arcadeGrayVehicle, arcadePurpleVehicle, simpleDrone },
                tracks: new[] { roadSystemTrack, roadSystemRealisticTrack, track },
                source: source);
        }

        public static bool TryCreateVehicleInstance(string descriptorId, Transform parent, out VehicleBase vehicle)
        {
            vehicle = null;
            if (string.Equals(descriptorId, SimpleDroneVehicleId, StringComparison.Ordinal))
            {
                vehicle = CreateSimpleDroneInstance(parent);
                return vehicle != null;
            }

            if (!TryGetVehicleVisualProfile(descriptorId, out var visualProfile))
            {
                return false;
            }

            var root = new GameObject("KS0223Vehicle");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(0f, 0.2f, -6f);
            root.transform.rotation = Quaternion.identity;

            var chassisCollider = root.AddComponent<BoxCollider>();
            chassisCollider.center = new Vector3(0f, 0.08f, 0f);
            chassisCollider.size = new Vector3(0.34f, 0.16f, 0.52f);

            var hasCustomVisual = TryAttachVisual(root.transform, visualProfile);
            if (!hasCustomVisual)
            {
                CreateFallbackVisualShell(root.transform);
            }

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
            if (string.Equals(descriptorId, BasicArenaTrackId, StringComparison.Ordinal))
            {
                var root = new GameObject("BasicArenaTrack");
                root.transform.SetParent(parent, false);
                root.transform.position = Vector3.zero;
                root.transform.rotation = Quaternion.identity;

                track = root.AddComponent<BasicArenaTrack>();
                return true;
            }

            if (string.Equals(descriptorId, RoadSystemArenaTrackId, StringComparison.Ordinal))
            {
                var root = new GameObject("RoadSystemArenaTrack");
                root.transform.SetParent(parent, false);
                root.transform.position = Vector3.zero;
                root.transform.rotation = Quaternion.identity;
                track = root.AddComponent<RoadSystemArenaTrack>();
                return true;
            }

            if (string.Equals(descriptorId, RoadSystemRealisticTrackId, StringComparison.Ordinal))
            {
                var root = new GameObject("RoadSystemRealisticTrack");
                root.transform.SetParent(parent, false);
                root.transform.position = Vector3.zero;
                root.transform.rotation = Quaternion.identity;
                track = root.AddComponent<RoadSystemRealisticTrack>();
                return true;
            }

            return false;
        }

        private static VehiclePluginDescriptor CreateVehicleDescriptor(string id, string displayName, string description)
        {
            var vehicle = ScriptableObject.CreateInstance<VehiclePluginDescriptor>();
            vehicle.id = id;
            vehicle.displayName = displayName;
            vehicle.description = description;
            vehicle.deviceContract = string.Equals(id, SimpleDroneVehicleId, StringComparison.Ordinal)
                ? CreateSimpleDroneContract(id)
                : CreateKs0223Contract(id);
            return vehicle;
        }

        private static DeviceContractDescriptorAsset CreateKs0223Contract(string deviceId)
        {
            var asset = ScriptableObject.CreateInstance<DeviceContractDescriptorAsset>();
            asset.contractVersion = new ContractVersion(0, 1, 0);
            asset.descriptor = new DeviceContractDescriptor
            {
                deviceId = string.IsNullOrWhiteSpace(deviceId) ? Ks0223VehicleId : deviceId,
                deviceType = "ground_robot_differential",
                sensors = new[]
                {
                    new SensorDescriptor
                    {
                        id = "camera.front.rgb",
                        sensorType = "sensor.camera.rgb",
                        format = "jpeg",
                        unit = "pixel",
                        shape = new[] { 480, 640, 3 },
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

        private static DeviceContractDescriptorAsset CreateSimpleDroneContract(string deviceId)
        {
            var asset = ScriptableObject.CreateInstance<DeviceContractDescriptorAsset>();
            asset.contractVersion = new ContractVersion(0, 1, 0);
            asset.descriptor = new DeviceContractDescriptor
            {
                deviceId = string.IsNullOrWhiteSpace(deviceId) ? SimpleDroneVehicleId : deviceId,
                deviceType = "drone_quadcopter",
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
                        id = "altimeter",
                        sensorType = "sensor.altimeter",
                        format = "float32",
                        unit = "m",
                        shape = new[] { 1 },
                        rateHz = 20f,
                    },
                    new SensorDescriptor
                    {
                        id = "airspeed",
                        sensorType = "sensor.airspeed",
                        format = "float32",
                        unit = "m/s",
                        shape = new[] { 1 },
                        rateHz = 20f,
                    },
                    new SensorDescriptor
                    {
                        id = "powertrain.estimate",
                        sensorType = "sensor.powertrain",
                        format = "float32",
                        unit = "mixed",
                        shape = new[] { 2 },
                        rateHz = 10f,
                    },
                },
                actuators = new[]
                {
                    new ActuatorDescriptor
                    {
                        id = "drone.pitch_norm",
                        actuatorType = "drone_control.pitch",
                        unit = "norm",
                        min = -1f,
                        max = 1f,
                    },
                    new ActuatorDescriptor
                    {
                        id = "drone.yaw_norm",
                        actuatorType = "drone_control.yaw",
                        unit = "norm",
                        min = -1f,
                        max = 1f,
                    },
                    new ActuatorDescriptor
                    {
                        id = "drone.thrust_norm",
                        actuatorType = "drone_control.thrust",
                        unit = "norm",
                        min = -1f,
                        max = 1f,
                    },
                },
                observationSchemaJson = "{\"state\":[\"pose\",\"linearVelocity\",\"angularVelocity\",\"speed\"],\"camera\":\"camera/front/image_raw\",\"telemetry\":[\"sensor.altimeter.m\",\"sensor.airspeed.mps\",\"sensor.vertical_speed.mps\",\"power.battery.voltage_v\",\"power.motor.estimated_w\"]}",
                actionSchemaJson = "{\"base\":[\"throttle\",\"steer\",\"brake\"],\"extensions\":[\"drone.pitch_norm\",\"drone.yaw_norm\",\"drone.thrust_norm\"]}",
            };

            return asset;
        }

        private static VehicleBase CreateSimpleDroneInstance(Transform parent)
        {
            var root = new GameObject("SimpleDroneVehicle");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(0f, 1.3f, -6f);
            root.transform.rotation = Quaternion.identity;

            var collider = root.AddComponent<SphereCollider>();
            collider.radius = 0.20f;
            collider.center = new Vector3(0f, 0.10f, 0f);

            var dronePrefab = LoadDronePrefab();
            if (dronePrefab != null)
            {
                var visualRoot = UnityEngine.Object.Instantiate(dronePrefab, root.transform, false);
                visualRoot.name = "VisualModel";
                visualRoot.transform.localPosition = Vector3.zero;
                visualRoot.transform.localRotation = Quaternion.identity;
                visualRoot.transform.localScale = Vector3.one;
                FitVisualToVehicleBounds(root.transform, visualRoot.transform);
                StripVisualPhysicsAndScripts(visualRoot);
            }

            return root.AddComponent<SimpleDroneVehicle>();
        }

        private static bool TryGetVehicleVisualProfile(string descriptorId, out VehicleVisualProfile profile)
        {
            profile = default;
            if (string.Equals(descriptorId, Ks0223VehicleId, StringComparison.Ordinal))
            {
                profile = new VehicleVisualProfile(PrometeoPrefabPath, 0f);
                return true;
            }

            if (string.Equals(descriptorId, Ks0223ArcadeBlueVehicleId, StringComparison.Ordinal))
            {
                profile = new VehicleVisualProfile(ArcadeBluePrefabPath, 0f);
                return true;
            }

            if (string.Equals(descriptorId, Ks0223ArcadeRedVehicleId, StringComparison.Ordinal))
            {
                profile = new VehicleVisualProfile(ArcadeRedPrefabPath, 0f);
                return true;
            }

            if (string.Equals(descriptorId, Ks0223ArcadeGrayVehicleId, StringComparison.Ordinal))
            {
                profile = new VehicleVisualProfile(ArcadeGrayPrefabPath, 0f);
                return true;
            }

            if (string.Equals(descriptorId, Ks0223ArcadePurpleVehicleId, StringComparison.Ordinal))
            {
                profile = new VehicleVisualProfile(ArcadePurplePrefabPath, 0f);
                return true;
            }

            return false;
        }

        private static bool TryAttachVisual(Transform parent, VehicleVisualProfile visualProfile)
        {
            var prefab = LoadVisualPrefab(visualProfile.PrefabPath);
            if (prefab == null)
            {
                return false;
            }

            var visualRoot = UnityEngine.Object.Instantiate(prefab, parent, false);
            visualRoot.name = "VisualModel";
            visualRoot.transform.localRotation = Quaternion.Euler(0f, visualProfile.YawOffsetDeg, 0f);
            visualRoot.transform.localPosition = Vector3.zero;
            visualRoot.transform.localScale = Vector3.one;

            FitVisualToVehicleBounds(parent, visualRoot.transform);
            StripVisualPhysicsAndScripts(visualRoot);

            return true;
        }

        private static void FitVisualToVehicleBounds(Transform vehicleRoot, Transform visualRoot)
        {
            if (!TryCalculateRenderBounds(vehicleRoot, visualRoot, out var localBounds))
            {
                return;
            }

            var horizontalSize = Mathf.Max(localBounds.size.x, localBounds.size.z);
            if (horizontalSize > 0.001f)
            {
                var scale = TargetVehicleLength / horizontalSize;
                visualRoot.localScale *= scale;
            }

            if (!TryCalculateRenderBounds(vehicleRoot, visualRoot, out localBounds))
            {
                return;
            }

            var correction = new Vector3(
                -localBounds.center.x,
                VehicleVisualGroundOffset - localBounds.min.y,
                -localBounds.center.z);
            visualRoot.localPosition += correction;
        }

        private static bool TryCalculateRenderBounds(Transform vehicleRoot, Transform visualRoot, out Bounds localBounds)
        {
            localBounds = default;
            var renderers = visualRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            var initialized = false;

            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                var worldBounds = renderer.bounds;
                foreach (var corner in GetBoundsCorners(worldBounds))
                {
                    var localPoint = vehicleRoot.InverseTransformPoint(corner);
                    if (!initialized)
                    {
                        localBounds = new Bounds(localPoint, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localPoint);
                    }
                }
            }

            return initialized;
        }

        private static Vector3[] GetBoundsCorners(Bounds bounds)
        {
            var min = bounds.min;
            var max = bounds.max;
            return new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z),
            };
        }

        private static void StripVisualPhysicsAndScripts(GameObject visualRoot)
        {
            var rigidbodies = visualRoot.GetComponentsInChildren<Rigidbody>(includeInactive: true);
            foreach (var rigidbody in rigidbodies)
            {
                DestroyComponent(rigidbody);
            }

            var colliders = visualRoot.GetComponentsInChildren<Collider>(includeInactive: true);
            foreach (var collider in colliders)
            {
                DestroyComponent(collider);
            }

            var joints = visualRoot.GetComponentsInChildren<Joint>(includeInactive: true);
            foreach (var joint in joints)
            {
                DestroyComponent(joint);
            }

            var scripts = visualRoot.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            foreach (var script in scripts)
            {
                if (script == null)
                {
                    continue;
                }

                script.enabled = false;
            }

            var cameras = visualRoot.GetComponentsInChildren<Camera>(includeInactive: true);
            foreach (var camera in cameras)
            {
                if (camera != null)
                {
                    camera.enabled = false;
                }
            }

            var audioListeners = visualRoot.GetComponentsInChildren<AudioListener>(includeInactive: true);
            foreach (var listener in audioListeners)
            {
                if (listener != null)
                {
                    listener.enabled = false;
                }
            }
        }

        private static void CreateFallbackVisualShell(Transform parent)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(parent, false);
            body.transform.localScale = new Vector3(0.34f, 0.09f, 0.50f);
            body.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            DisableCollider(body);

            CreateDecorBlock(parent, "Hood", new Vector3(0.30f, 0.06f, 0.18f), new Vector3(0f, 0.09f, 0.15f));
            CreateDecorBlock(parent, "Cabin", new Vector3(0.22f, 0.08f, 0.19f), new Vector3(0f, 0.13f, -0.03f));
            CreateDecorBlock(parent, "RearDeck", new Vector3(0.30f, 0.05f, 0.12f), new Vector3(0f, 0.09f, -0.19f));
            CreateDecorBlock(parent, "Windshield", new Vector3(0.20f, 0.05f, 0.03f), new Vector3(0f, 0.14f, 0.07f), new Vector3(-22f, 0f, 0f));
            CreateDecorBlock(parent, "RearWindow", new Vector3(0.20f, 0.05f, 0.03f), new Vector3(0f, 0.14f, -0.11f), new Vector3(22f, 0f, 0f));

            var cameraPod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cameraPod.name = "CameraPod";
            cameraPod.transform.SetParent(parent, false);
            cameraPod.transform.localScale = new Vector3(0.02f, 0.03f, 0.02f);
            cameraPod.transform.localPosition = new Vector3(0f, 0.20f, 0.20f);
            DisableCollider(cameraPod);

            CreateWheel(parent, "WheelFL", new Vector3(-0.14f, 0.03f, 0.18f));
            CreateWheel(parent, "WheelFR", new Vector3(0.14f, 0.03f, 0.18f));
            CreateWheel(parent, "WheelRL", new Vector3(-0.14f, 0.03f, -0.18f));
            CreateWheel(parent, "WheelRR", new Vector3(0.14f, 0.03f, -0.18f));
        }

        private static GameObject LoadVisualPrefab(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
#else
            return null;
#endif
        }

        private static GameObject LoadDronePrefab()
        {
#if UNITY_EDITOR
            for (var i = 0; i < SimpleDronePrefabCandidates.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimpleDronePrefabCandidates[i]);
                if (prefab != null)
                {
                    return prefab;
                }
            }
#endif
            return null;
        }

        private static void DestroyComponent(Component component)
        {
            if (component == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(component);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        private static void CreateWheel(Transform parent, string name, Vector3 localPosition)
        {
            var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = name;
            wheel.transform.SetParent(parent, false);
            wheel.transform.localScale = new Vector3(0.04f, 0.015f, 0.04f);
            wheel.transform.localPosition = localPosition;
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            DisableCollider(wheel);
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

        private readonly struct VehicleVisualProfile
        {
            public readonly string PrefabPath;
            public readonly float YawOffsetDeg;

            public VehicleVisualProfile(string prefabPath, float yawOffsetDeg)
            {
                PrefabPath = prefabPath;
                YawOffsetDeg = yawOffsetDeg;
            }
        }
    }
}
