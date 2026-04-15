using System;
using UavSimulator.Core;
using UavSimulator.Contracts;
using UavSimulator.Tracks;
using UavSimulator.Vehicles;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UavSimulator.Plugins
{
    public static class BuiltinPluginFactory
    {
        public const string Ks0223VehicleId = "vehicle.ks0223.v1";
        public const string PrometeoSportVehicleId = "vehicle.prometeo.sport.v1";
        public const string ArcadeBlueVehicleId = "vehicle.arcade.blue.v1";
        public const string ArcadeRedVehicleId = "vehicle.arcade.red.v1";
        public const string ArcadeGrayVehicleId = "vehicle.arcade.gray.v1";
        public const string ArcadePurpleVehicleId = "vehicle.arcade.purple.v1";
        public const string SimpleDroneVehicleId = "vehicle.drone.simple.v1";
        public const string BasicArenaTrackId = "track.basic_arena.v1";
        public const string RoadSystemArenaTrackId = "track.roadsystem_arena.v1";
        public const string RoadSystemRealisticTrackId = "track.roadsystem_realistic.v2";
        public const string CardboardCorridorTrackId = "track.cardboard_corridor.v1";

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
            var ks0223Vehicle = CreateVehicleDescriptor(
                Ks0223VehicleId,
                "KS0223 Robot",
                "Ground robot profile for KS0223 differential-drive robot with line tracker, ultrasonic and camera sensors.");
            var vehicle = CreateVehicleDescriptor(
                PrometeoSportVehicleId,
                "PROMETEO Sport Car",
                "Ground robot profile with PROMETEO sport car body (fallback visuals if asset is unavailable).");
            var arcadeBlueVehicle = CreateVehicleDescriptor(
                ArcadeBlueVehicleId,
                "Arcade Free Racing Car (Blue)",
                "Ground robot profile with ARCADE Free Racing Car blue visual body.");
            var arcadeRedVehicle = CreateVehicleDescriptor(
                ArcadeRedVehicleId,
                "Arcade Free Racing Car (Red)",
                "Ground robot profile with ARCADE Free Racing Car red visual body.");
            var arcadeGrayVehicle = CreateVehicleDescriptor(
                ArcadeGrayVehicleId,
                "Arcade Free Racing Car (Gray)",
                "Ground robot profile with ARCADE Free Racing Car gray visual body.");
            var arcadePurpleVehicle = CreateVehicleDescriptor(
                ArcadePurpleVehicleId,
                "Arcade Free Racing Car (Purple)",
                "Ground robot profile with ARCADE Free Racing Car purple visual body.");
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

            var cardboardCorridorTrack = ScriptableObject.CreateInstance<TrackPluginDescriptor>();
            cardboardCorridorTrack.id = CardboardCorridorTrackId;
            cardboardCorridorTrack.displayName = "Cardboard Corridor (Sim-to-Real)";
            cardboardCorridorTrack.description = "L-shaped cardboard corridor matching real-world apartment test setup. Narrow walls, wood floor, ArUco finish marker.";
            cardboardCorridorTrack.parametersSchemaJson = "{\"type\":\"object\",\"properties\":{}}";

            return new PluginRegistrySnapshot(
                vehicles: new[] { ks0223Vehicle, vehicle, arcadeBlueVehicle, arcadeRedVehicle, arcadeGrayVehicle, arcadePurpleVehicle, simpleDrone },
                tracks: new[] { roadSystemTrack, roadSystemRealisticTrack, track, cardboardCorridorTrack },
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

            var root = new GameObject("GroundRobotVehicle");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(0f, 0.2f, -6f);
            root.transform.rotation = Quaternion.identity;

            var chassisCollider = root.AddComponent<BoxCollider>();
            chassisCollider.center = new Vector3(0f, 0.08f, 0f);
            chassisCollider.size = new Vector3(0.34f, 0.16f, 0.52f);

            var hasCustomVisual = TryAttachVisual(root.transform, visualProfile);
            if (!hasCustomVisual)
            {
                var accentColor = GetFallbackAccentColor(descriptorId);
                CreateFallbackVisualShell(root.transform, accentColor);
                SanitizeRendererMaterials(root);
                Debug.LogWarning(
                    $"[BuiltinPluginFactory] Imported vehicle visual is unavailable for '{descriptorId}'. " +
                    $"Fallback shell is used instead (prefab path: {visualProfile.PrefabPath}).");
            }

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1.0f;
            rb.angularDamping = 1.5f;
            rb.linearDamping = 0.2f;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            vehicle = root.AddComponent<Ks0223Vehicle>();
            if (vehicle is Ks0223Vehicle ks0223Vehicle)
            {
                ks0223Vehicle.SetPresentationAccentColor(GetFallbackAccentColor(descriptorId));
            }

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

            if (string.Equals(descriptorId, CardboardCorridorTrackId, StringComparison.Ordinal))
            {
                var root = new GameObject("CardboardCorridorTrack");
                root.transform.SetParent(parent, false);
                root.transform.position = Vector3.zero;
                root.transform.rotation = Quaternion.identity;
                track = root.AddComponent<CardboardCorridorTrack>();
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
                : CreateGroundRobotContract(id);
            return vehicle;
        }

        private static DeviceContractDescriptorAsset CreateGroundRobotContract(string deviceId)
        {
            var asset = ScriptableObject.CreateInstance<DeviceContractDescriptorAsset>();
            asset.contractVersion = new ContractVersion(0, 1, 0);
            asset.descriptor = new DeviceContractDescriptor
            {
                deviceId = string.IsNullOrWhiteSpace(deviceId) ? PrometeoSportVehicleId : deviceId,
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

            if (string.Equals(descriptorId, PrometeoSportVehicleId, StringComparison.Ordinal))
            {
                profile = new VehicleVisualProfile(PrometeoPrefabPath, 0f);
                return true;
            }

            if (string.Equals(descriptorId, ArcadeBlueVehicleId, StringComparison.Ordinal))
            {
                profile = new VehicleVisualProfile(ArcadeBluePrefabPath, 0f);
                return true;
            }

            if (string.Equals(descriptorId, ArcadeRedVehicleId, StringComparison.Ordinal))
            {
                profile = new VehicleVisualProfile(ArcadeRedPrefabPath, 0f);
                return true;
            }

            if (string.Equals(descriptorId, ArcadeGrayVehicleId, StringComparison.Ordinal))
            {
                profile = new VehicleVisualProfile(ArcadeGrayPrefabPath, 0f);
                return true;
            }

            if (string.Equals(descriptorId, ArcadePurpleVehicleId, StringComparison.Ordinal))
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
            // Keep original style from imported assets and only replace shader-incompatible materials.
            SanitizeRendererMaterials(visualRoot, forceFallback: false, copyTextures: true);

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

        private static void SanitizeRendererMaterials(GameObject visualRoot, bool forceFallback = false, bool copyTextures = true)
        {
            var renderers = visualRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                var sharedMaterials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < sharedMaterials.Length; i++)
                {
                    var source = sharedMaterials[i];
                    if (source == null)
                    {
                        continue;
                    }

                    if (!forceFallback && !RuntimeMaterialCompatibility.NeedsReplacement(source))
                    {
                        continue;
                    }

                    sharedMaterials[i] = CreateFallbackMaterial(source, copyTextures);
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = sharedMaterials;
                }
            }
        }

        private static Material CreateFallbackMaterial(Material source, bool copyTextures)
        {
            var fallback = RuntimeMaterialCompatibility.CreateReplacementMaterial(source, defaultSmoothness: 0.2f, copyTextures: copyTextures);
            fallback.color = RuntimeMaterialCompatibility.ReadSourceColor(source);

            return fallback;
        }

        private static Shader ResolveRuntimeLitShader()
            => RuntimeMaterialCompatibility.ResolveCompatibleLitShader();

        private static bool IsUrpActive()
            => RuntimeMaterialCompatibility.IsUrpActive();

        private static bool IsBuiltinCompatibleShader(Shader shader)
            => RuntimeMaterialCompatibility.IsShaderCompatibleForCurrentPipeline(shader);

        private static void CreateFallbackVisualShell(Transform parent, Color accentColor)
        {
            var visualRoot = new GameObject("VisualModel");
            visualRoot.transform.SetParent(parent, false);
            visualRoot.transform.localPosition = Vector3.zero;
            visualRoot.transform.localRotation = Quaternion.identity;
            visualRoot.transform.localScale = Vector3.one;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(visualRoot.transform, false);
            body.transform.localScale = new Vector3(0.34f, 0.09f, 0.50f);
            body.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            DisableCollider(body);
            ApplyPrimitiveMaterial(body, accentColor, 0.24f);

            CreateDecorBlock(visualRoot.transform, "Hood", new Vector3(0.30f, 0.06f, 0.18f), new Vector3(0f, 0.09f, 0.15f));
            CreateDecorBlock(visualRoot.transform, "Cabin", new Vector3(0.22f, 0.08f, 0.19f), new Vector3(0f, 0.13f, -0.03f));
            CreateDecorBlock(visualRoot.transform, "RearDeck", new Vector3(0.30f, 0.05f, 0.12f), new Vector3(0f, 0.09f, -0.19f));
            CreateDecorBlock(visualRoot.transform, "Windshield", new Vector3(0.20f, 0.05f, 0.03f), new Vector3(0f, 0.14f, 0.07f), new Vector3(-22f, 0f, 0f));
            CreateDecorBlock(visualRoot.transform, "RearWindow", new Vector3(0.20f, 0.05f, 0.03f), new Vector3(0f, 0.14f, -0.11f), new Vector3(22f, 0f, 0f));

            CreateWheel(visualRoot.transform, "WheelFL", new Vector3(-0.14f, 0.03f, 0.18f));
            CreateWheel(visualRoot.transform, "WheelFR", new Vector3(0.14f, 0.03f, 0.18f));
            CreateWheel(visualRoot.transform, "WheelRL", new Vector3(-0.14f, 0.03f, -0.18f));
            CreateWheel(visualRoot.transform, "WheelRR", new Vector3(0.14f, 0.03f, -0.18f));
        }

        private static GameObject LoadVisualPrefab(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab != null)
            {
                return prefab;
            }

            var fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            var guids = AssetDatabase.FindAssets($"t:Prefab {fileName}");
            for (var i = 0; i < guids.Length; i++)
            {
                var candidatePath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrWhiteSpace(candidatePath))
                {
                    continue;
                }

                if (!candidatePath.Contains("ARCADE - FREE Racing Car", StringComparison.OrdinalIgnoreCase) &&
                    !candidatePath.Contains("PROMETEO - Car Controller", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(candidatePath);
                if (prefab != null)
                {
                    return prefab;
                }
            }

            Debug.LogWarning($"[BuiltinPluginFactory] Vehicle visual prefab not found: {assetPath}");
            return null;
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
            ApplyPrimitiveMaterial(wheel, new Color(0.10f, 0.10f, 0.11f), 0.12f);
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
            ApplyPrimitiveMaterial(part, new Color(0.82f, 0.84f, 0.87f), 0.16f);
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

        private static Color GetFallbackAccentColor(string descriptorId)
        {
            if (string.Equals(descriptorId, ArcadeRedVehicleId, StringComparison.Ordinal))
            {
                return new Color(0.78f, 0.20f, 0.20f);
            }

            if (string.Equals(descriptorId, ArcadeGrayVehicleId, StringComparison.Ordinal))
            {
                return new Color(0.56f, 0.58f, 0.60f);
            }

            if (string.Equals(descriptorId, ArcadePurpleVehicleId, StringComparison.Ordinal))
            {
                return new Color(0.54f, 0.36f, 0.74f);
            }

            return new Color(0.19f, 0.44f, 0.79f);
        }

        private static void ApplyPrimitiveMaterial(GameObject gameObject, Color color, float smoothness)
        {
            var renderer = gameObject != null ? gameObject.GetComponent<Renderer>() : null;
            if (renderer == null)
            {
                return;
            }

            var shader = ResolveRuntimeLitShader();
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }

            renderer.sharedMaterial = material;
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
