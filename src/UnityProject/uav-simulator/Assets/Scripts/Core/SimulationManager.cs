using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UavSimulator.CityDemo;
using UavSimulator.Contracts;
using UavSimulator.Plugins;
using UavSimulator.Tracks;
using UavSimulator.Vehicles;
using UnityEngine;

namespace UavSimulator.Core
{
    [Serializable]
    public sealed class SimulationRuntimeDiagnostics
    {
        public string pluginRegistrySource;
        public int availableVehicles;
        public int availableTracks;
        public string activeAgentId;
        public string activeVehicleId;
        public string activeTrackId;
        public int activeVehicleCount;
        public string[] activeAgentIds;
        public string[] activeVehicleIds;
    }

    public sealed class SimulationManager : MonoBehaviour
    {
        private const string RouteWaypointsKey = "route.waypoints";
        private const string RouteReachDistanceKey = "route.reach_distance_m";
        private const string RouteLoopKey = "route.loop";
        private const string SpawnPositionKey = "spawn.position";
        private const string SpawnYawDegKey = "spawn.yaw_deg";
        // Plan 4 (rev39): spawn pose jitter for sim-to-real generalization.
        // Master-plan sim audit #1 — without this, policy memorizes
        // "after start drive 20 steps then turn left" instead of learning
        // "turn when vanishing point shows corner".
        private const string SpawnJitterMKey = "spawn.jitter_m";
        private const string SpawnYawJitterDegKey = "spawn.yaw_jitter_deg";
        private const string RenderQualityProfileKey = "render.quality_profile";
        private const string AllowEmptyAgentsKey = "agents.allow_empty";
        private const string ModelCaptureModeKey = "camera.model_capture_mode";
        private const string ControllerProfileKey = "controller.profile";
        private const string WaypointFollowerProfile = "waypoint_follower";
        private const string GateLookAheadMKey = "gate.look_ahead_m";
        private const string GateBrakeStartMKey = "gate.brake_start_m";
        private static readonly Vector3[] CardboardCorridorDefaultRoute =
        {
            new Vector3(0f, 0f, -0.55f),
            new Vector3(0f, 0f, -0.15f),
            new Vector3(0.18f, 0f, 0f),
            new Vector3(0.60f, 0f, 0f),
        };

        [SerializeField] private Transform trackRoot;
        [SerializeField] private Transform vehicleRoot;

        [Serializable]
        private sealed class ActiveAgentRuntime
        {
            public string AgentId;
            public string VehicleId;
            public bool IsPrimary;
            public VehicleBase Vehicle;
            public ConfigKeyValue[] TrackParams;
            public ConfigKeyValue[] VehicleParams;
            public bool WaypointFollowerEnabled;
            public Vector3[] RouteWaypoints = Array.Empty<Vector3>();
            public int RouteWaypointIndex;
            public float RouteReachDistance = 1f;
            public bool RouteLoop;
        }

        private sealed class ResolvedAgentConfig
        {
            public string AgentId;
            public bool IsPrimary;
            public VehiclePluginDescriptor Descriptor;
            public ConfigKeyValue[] TrackParams;
            public ConfigKeyValue[] VehicleParams;
        }

        private PluginRegistrySnapshot registry;
        private TrackBase activeTrack;
        private VehicleBase activeVehicle;
        private readonly List<ActiveAgentRuntime> activeAgents = new List<ActiveAgentRuntime>();
        private float defaultTimeScale = 1f;
        private Vector3[] activeRouteWaypoints = Array.Empty<Vector3>();
        private int activeRouteWaypointIndex;
        private float activeRouteReachDistance = 1f;
        private bool activeRouteLoop;
        private bool activeRouteCompleted;
        private bool activeRouteWaypointsConfigured;
        private string activeTrackId = string.Empty;
        private string activeAgentId = string.Empty;
        private string activeVehicleId = string.Empty;

        private void Awake()
        {
            registry = PluginRegistry.Load();
            defaultTimeScale = Time.timeScale;
            ApplyRuntimeGraphicsProfile("high");
            BindExistingSceneObjects();
        }

        public void ConfigureRoots(Transform sceneTrackRoot, Transform sceneVehicleRoot)
        {
            if (sceneTrackRoot != null)
            {
                trackRoot = sceneTrackRoot;
            }

            if (sceneVehicleRoot != null)
            {
                vehicleRoot = sceneVehicleRoot;
            }

            BindExistingSceneObjects();
        }

        public void RemoveSceneVehiclePlaceholders()
        {
            if (vehicleRoot == null)
            {
                return;
            }

            var sceneVehicles = vehicleRoot.GetComponentsInChildren<VehicleBase>(includeInactive: true);
            foreach (var sceneVehicle in sceneVehicles)
            {
                if (sceneVehicle == null)
                {
                    continue;
                }

                if (sceneVehicle == activeVehicle)
                {
                    activeVehicle = null;
                }

                if (Application.isPlaying)
                {
                    Destroy(sceneVehicle.gameObject);
                }
                else
                {
                    DestroyImmediate(sceneVehicle.gameObject);
                }
            }

            activeAgents.Clear();
            activeAgentId = string.Empty;
            activeVehicleId = string.Empty;
        }

        public SimulatorContractDescriptor GetContract()
        {
            var contract = new SimulatorContractDescriptor
            {
                simulatorId = Application.productName,
                simulatorName = Application.productName,
                contractVersion = "0.1.0",
                transportInfo = null,
                availableVehicles = registry.Vehicles
                    .Where(v => v != null && v.deviceContract != null && v.deviceContract.descriptor != null)
                    .Select(v => v.deviceContract.descriptor)
                    .ToArray(),
                availableTracks = registry.Tracks
                    .Where(t => t != null)
                    .Select(t => new TrackContractDescriptor
                    {
                        trackId = t.id,
                        displayName = t.displayName,
                        parametersSchemaJson = t.parametersSchemaJson,
                    })
                    .ToArray(),
            };

            return contract;
        }

        public void ResetSimulation(SimulationConfig config)
        {
            var validation = SimulationConfigValidator.Validate(config, registry);
            var allowEmptyAgents = TryReadFlag(config.flags, AllowEmptyAgentsKey, out var allowEmptyValue) && allowEmptyValue;
            var resolvedAgents = ResolveAgentConfigs(config, validation.Vehicle, allowEmptyAgents);
            var qualityProfile = ReadConfigValue(config.flags, RenderQualityProfileKey);
            ApplyRuntimeGraphicsProfile(string.IsNullOrWhiteSpace(qualityProfile) ? "high" : qualityProfile);

            DestroyActiveInstances();

            activeTrack = InstantiateTrack(validation.Track);
            activeTrackId = validation.Track != null ? validation.Track.id ?? string.Empty : string.Empty;
            activeTrack.ApplyTrackParams(config.trackParams ?? Array.Empty<ConfigKeyValue>());
            activeTrack.ResetTrack(config.seed);
            Time.timeScale = validation.TimeScale;
            ConfigureRoute(config.trackParams);

            for (var index = 0; index < resolvedAgents.Count; index++)
            {
                var agent = resolvedAgents[index];
                var vehicle = InstantiateVehicle(agent.Descriptor);
                vehicle.ApplyVehicleConfig(agent.VehicleParams);
                vehicle.ResetVehicle(config.seed + index);
                ApplyVehicleSpawn(vehicle, agent.TrackParams, index, config.seed);
                AttachCityGate(vehicle, agent.VehicleParams);

                var runtimeAgent = new ActiveAgentRuntime
                {
                    AgentId = agent.AgentId,
                    VehicleId = agent.Descriptor.id ?? string.Empty,
                    IsPrimary = agent.IsPrimary,
                    Vehicle = vehicle,
                    TrackParams = agent.TrackParams ?? Array.Empty<ConfigKeyValue>(),
                    VehicleParams = agent.VehicleParams ?? Array.Empty<ConfigKeyValue>(),
                };
                ConfigureAgentWaypointFollower(runtimeAgent);
                activeAgents.Add(runtimeAgent);
            }

            var primary = activeAgents.FirstOrDefault(agent => agent.IsPrimary) ?? activeAgents.FirstOrDefault();
            if (primary == null)
            {
                if (!allowEmptyAgents)
                {
                    throw new InvalidOperationException("No active vehicles were created for the simulation.");
                }

                activeVehicle = null;
                activeAgentId = string.Empty;
                activeVehicleId = string.Empty;
                ApplyAgentInteractions(config.flags);
                return;
            }

            activeVehicle = primary.Vehicle;
            activeAgentId = primary.AgentId ?? string.Empty;
            activeVehicleId = primary.VehicleId ?? string.Empty;
            ApplyAgentInteractions(config.flags);
        }

        public SimulationRuntimeDiagnostics GetDiagnostics()
        {
            return new SimulationRuntimeDiagnostics
            {
                pluginRegistrySource = registry != null ? registry.Source.ToString() : PluginRegistrySource.BuiltinFactory.ToString(),
                availableVehicles = registry?.Vehicles?.Length ?? 0,
                availableTracks = registry?.Tracks?.Length ?? 0,
                activeAgentId = activeAgentId ?? string.Empty,
                activeVehicleId = activeVehicleId ?? string.Empty,
                activeTrackId = activeTrackId ?? string.Empty,
                activeVehicleCount = activeAgents.Count,
                activeAgentIds = activeAgents.Select(agent => agent.AgentId ?? string.Empty).ToArray(),
                activeVehicleIds = activeAgents.Select(agent => agent.VehicleId ?? string.Empty).ToArray(),
            };
        }

        public StepResult Step(ControlCommand command)
        {
            var target = ResolveTargetAgent(command?.targetAgentId, command?.targetVehicleId);
            if (target == null)
            {
                throw new InvalidOperationException("Active vehicle is not initialized. Call ResetSimulation first.");
            }

            var effectiveCommand = BuildEffectiveControlCommand(target, command);
            target.Vehicle.ApplyControl(effectiveCommand);
            target.Vehicle.TryReadCameraFrame(out var frame);
            CameraFrame modelFrame = null;
            if (TryReadConfigValue(effectiveCommand?.extensions, ModelCaptureModeKey, out var modelCaptureMode))
            {
                target.Vehicle.TryReadCameraFrame(modelCaptureMode, out modelFrame);
            }

            var state = target.Vehicle.ReadState();
            var routeCompleted = target.IsPrimary && UpdateRouteProgress(state);
            return BuildStepResult(target, state, frame, modelFrame, routeCompleted);
        }

        private void ConfigureAgentWaypointFollower(ActiveAgentRuntime agent)
        {
            if (agent == null || agent.Vehicle == null)
            {
                return;
            }

            if (!TryReadConfigValue(agent.VehicleParams, ControllerProfileKey, out var profile) ||
                !string.Equals(profile, WaypointFollowerProfile, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var route = activeRouteWaypoints;
            if (TryReadTrackParam(agent.TrackParams, RouteWaypointsKey, out var rawRoute) &&
                TryParseWaypoints(rawRoute, out var parsedRoute))
            {
                route = parsedRoute;
            }

            if (route == null || route.Length == 0)
            {
                return;
            }

            agent.RouteWaypoints = (Vector3[])route.Clone();
            agent.RouteReachDistance = activeRouteReachDistance;
            agent.RouteLoop = activeRouteLoop;

            if (TryReadTrackParam(agent.TrackParams, RouteReachDistanceKey, out var rawReach) &&
                float.TryParse(rawReach, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedReach) &&
                parsedReach > 0f)
            {
                agent.RouteReachDistance = parsedReach;
            }

            if (TryReadTrackParam(agent.TrackParams, RouteLoopKey, out var rawLoop) &&
                TryParseBool(rawLoop, out var parsedLoop))
            {
                agent.RouteLoop = parsedLoop;
            }

            agent.RouteWaypointIndex = ResolveInitialWaypointIndex(
                agent.Vehicle.transform.position,
                agent.RouteWaypoints,
                agent.RouteReachDistance,
                agent.RouteLoop);
            agent.WaypointFollowerEnabled = true;
        }

        private static int ResolveInitialWaypointIndex(Vector3 position, Vector3[] waypoints, float reachDistance, bool loop)
        {
            if (waypoints == null || waypoints.Length == 0)
            {
                return 0;
            }

            var bestIndex = 0;
            var bestDistanceSq = float.PositiveInfinity;
            for (var i = 0; i < waypoints.Length; i++)
            {
                var distanceSq = XzDistanceSq(position, waypoints[i]);
                if (distanceSq >= bestDistanceSq)
                {
                    continue;
                }

                bestDistanceSq = distanceSq;
                bestIndex = i;
            }

            var reach = Mathf.Max(0.1f, reachDistance);
            if (bestDistanceSq <= reach * reach)
            {
                var next = bestIndex + 1;
                if (next < waypoints.Length)
                {
                    return next;
                }

                return loop ? 0 : bestIndex;
            }

            return bestIndex;
        }

        private ControlCommand BuildEffectiveControlCommand(ActiveAgentRuntime agent, ControlCommand requested)
        {
            if (agent == null || !agent.WaypointFollowerEnabled || agent.RouteWaypoints == null || agent.RouteWaypoints.Length == 0)
            {
                return requested;
            }

            var command = BuildWaypointFollowerCommand(agent, requested);
            var gate = ResolveMovementGate(agent.Vehicle);
            if (gate != null && gate.ShouldBrake(out var brakeIntensity) && brakeIntensity >= 0.99f)
            {
                command.throttle = 0f;
                command.brake = Mathf.Clamp01(brakeIntensity);
            }

            return command;
        }

        private static IMovementGate ResolveMovementGate(VehicleBase vehicle)
        {
            if (vehicle == null)
            {
                return null;
            }

            var onnxGate = vehicle.GetComponent<OnnxTrafficLightAwareController>();
            if (onnxGate != null)
            {
                return onnxGate;
            }

            var behaviours = vehicle.GetComponents<MonoBehaviour>();
            for (var i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IMovementGate gate)
                {
                    return gate;
                }
            }

            return null;
        }

        private static ControlCommand BuildWaypointFollowerCommand(ActiveAgentRuntime agent, ControlCommand requested)
        {
            AdvanceAgentWaypoint(agent);
            if (agent.RouteWaypointIndex >= agent.RouteWaypoints.Length)
            {
                return CloneCommand(requested, agent, throttle: 0f, steer: 0f, brake: 1f);
            }

            var vehicleTransform = agent.Vehicle.transform;
            var target = agent.RouteWaypoints[agent.RouteWaypointIndex];
            var delta = target - vehicleTransform.position;
            delta.y = 0f;

            if (delta.sqrMagnitude < 0.0001f)
            {
                return CloneCommand(requested, agent, throttle: 0f, steer: 0f, brake: 0.2f);
            }

            var targetYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            var signedAngle = Mathf.DeltaAngle(vehicleTransform.eulerAngles.y, targetYaw);
            var steer = Mathf.Clamp(signedAngle / 55f, -1f, 1f);
            var angleFactor = Mathf.Clamp01(Mathf.Abs(signedAngle) / 105f);
            var throttle = Mathf.Lerp(0.55f, 0.20f, angleFactor);

            return CloneCommand(requested, agent, throttle, steer, brake: 0f);
        }

        private static void AdvanceAgentWaypoint(ActiveAgentRuntime agent)
        {
            while (agent.RouteWaypointIndex < agent.RouteWaypoints.Length)
            {
                var target = agent.RouteWaypoints[agent.RouteWaypointIndex];
                if (XzDistanceSq(agent.Vehicle.transform.position, target) >
                    agent.RouteReachDistance * agent.RouteReachDistance)
                {
                    return;
                }

                agent.RouteWaypointIndex++;
                if (agent.RouteWaypointIndex < agent.RouteWaypoints.Length)
                {
                    continue;
                }

                if (agent.RouteLoop)
                {
                    agent.RouteWaypointIndex = 0;
                    continue;
                }

                return;
            }
        }

        private static float XzDistanceSq(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static ControlCommand CloneCommand(
            ControlCommand requested,
            ActiveAgentRuntime agent,
            float throttle,
            float steer,
            float brake)
        {
            return new ControlCommand
            {
                throttle = throttle,
                steer = steer,
                brake = brake,
                targetAgentId = agent.AgentId,
                targetVehicleId = agent.VehicleId,
                timestamp = requested != null && requested.timestamp > 0
                    ? requested.timestamp
                    : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                timeBase = requested != null && !string.IsNullOrWhiteSpace(requested.timeBase)
                    ? requested.timeBase
                    : "unix_ms",
                extensions = FilterWaypointFollowerExtensions(requested?.extensions),
            };
        }

        private static ConfigKeyValue[] FilterWaypointFollowerExtensions(ConfigKeyValue[] extensions)
        {
            if (extensions == null || extensions.Length == 0)
            {
                return extensions;
            }

            var filtered = extensions
                .Where(item => item != null && !IsDrivePwmExtension(item.key))
                .ToArray();
            return filtered.Length == extensions.Length ? extensions : filtered;
        }

        private static bool IsDrivePwmExtension(string key) =>
            string.Equals(key, "drive.left_pwm_norm", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "drive.right_pwm_norm", StringComparison.OrdinalIgnoreCase);

        public StepResult ReadSnapshot(string targetAgentId = null, string targetVehicleId = null, bool includeFrame = true)
        {
            var target = ResolveTargetAgent(targetAgentId, targetVehicleId);
            if (target == null)
            {
                return new StepResult
                {
                    activeAgentId = string.Empty,
                    activeVehicleId = string.Empty,
                    state = new VehicleState
                    {
                        pose = new Posef
                        {
                            position = new Vector3f(),
                            rotation = new Quaternionf { w = 1f },
                        },
                        linearVelocity = new Vector3f(),
                        angularVelocity = new Vector3f(),
                        speed = 0f,
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        timeBase = "unix_ms",
                        telemetry = Array.Empty<ConfigKeyValue>(),
                    },
                    reward = 0f,
                    done = false,
                    info = Array.Empty<ConfigKeyValue>(),
                    frame = null,
                    modelFrame = null,
                    agents = Array.Empty<AgentStepResult>(),
                };
            }

            var state = target.Vehicle.ReadState();
            CameraFrame frame = null;
            var hasFrame = includeFrame && target.Vehicle.TryReadCameraFrame(out frame);
            return BuildStepResult(target, state, hasFrame ? frame : null, modelFrame: null, routeCompleted: false);
        }

        public VehicleState ReadState()
        {
            if (activeVehicle == null)
            {
                throw new InvalidOperationException("Active vehicle is not initialized. Call ResetSimulation first.");
            }

            return activeVehicle.ReadState();
        }

        public bool TryReadCameraFrame(out CameraFrame frame)
        {
            if (activeVehicle == null)
            {
                frame = null;
                return false;
            }

            return activeVehicle.TryReadCameraFrame(out frame);
        }

        private static T FindRequired<T>(T[] items, string id, string kind) where T : PluginDescriptorBase
        {
            var match = (items ?? Array.Empty<T>()).FirstOrDefault(v => v != null && v.id == id);
            if (match == null)
            {
                throw new InvalidOperationException($"Unknown {kind} id: '{id}'.");
            }

            return match;
        }

        private VehicleBase InstantiateVehicle(VehiclePluginDescriptor descriptor)
        {
            var parent = vehicleRoot != null ? vehicleRoot : transform;

            if (descriptor.prefab == null)
            {
                if (BuiltinPluginFactory.TryCreateVehicleInstance(descriptor.id, parent, out var runtimeVehicle))
                {
                    return runtimeVehicle;
                }

                throw new InvalidOperationException($"Vehicle plugin '{descriptor.id}' has no prefab assigned.");
            }

            var instance = Instantiate(descriptor.prefab, parent);
            var vehicle = instance.GetComponentInChildren<VehicleBase>();
            if (vehicle == null)
            {
                throw new InvalidOperationException($"Vehicle prefab for '{descriptor.id}' does not contain VehicleBase.");
            }

            return vehicle;
        }

        /// <summary>
        /// Attach IMovementGate + IVehicleStateExtender components based on the
        /// scenario's <c>gate.kind</c> vehicle param. Runs after spawn so the
        /// vehicle GameObject (including its in-scene camera) is fully built.
        ///
        /// Recognised kinds:
        ///   "ground_truth" → TrafficLightAwareController (raycast-based).
        ///   "onnx"         → OnnxTrafficLightAwareController + the ground-truth controller
        ///                    so telemetry still publishes nearest-light snapshot.
        /// Either kind also gets a CityVehicleTelemetryExtender so VehicleState.telemetry
        /// publishes nearestTrafficLight.* entries for auto_label and observers.
        /// </summary>
        private static void AttachCityGate(VehicleBase vehicle, ConfigKeyValue[] vehicleParams)
        {
            if (vehicle == null || vehicleParams == null) return;
            string kind = null;
            foreach (var kv in vehicleParams)
            {
                if (kv == null || string.IsNullOrEmpty(kv.key)) continue;
                if (kv.key == "gate.kind")
                {
                    kind = kv.value?.Trim().ToLowerInvariant();
                    break;
                }
            }
            if (string.IsNullOrEmpty(kind)) return;

            var go = vehicle.gameObject;

            // Always attach the ground-truth controller — it provides the telemetry snapshot
            // via raycast against TrafficLightTriggerZone (no-op if none in scene).
            var groundTruthGate = go.GetComponent<UavSimulator.Vehicles.TrafficLightAwareController>();
            if (groundTruthGate == null)
            {
                groundTruthGate = go.AddComponent<UavSimulator.Vehicles.TrafficLightAwareController>();
            }
            if (TryReadConfigValue(vehicleParams, GateLookAheadMKey, out var rawLookAhead) &&
                TryParseFloat(rawLookAhead, out var lookAheadM))
            {
                groundTruthGate.lookAheadDistance = Mathf.Clamp(lookAheadM, 1f, 30f);
            }
            if (TryReadConfigValue(vehicleParams, GateBrakeStartMKey, out var rawBrakeStart) &&
                TryParseFloat(rawBrakeStart, out var brakeStartM))
            {
                groundTruthGate.brakeStartDistance = Mathf.Clamp(brakeStartM, 0.25f, 15f);
            }
            if (go.GetComponent<UavSimulator.CityDemo.CityVehicleTelemetryExtender>() == null)
            {
                go.AddComponent<UavSimulator.CityDemo.CityVehicleTelemetryExtender>();
            }

            // Onnx kind layers an ONNX-driven gate on top.
            if (kind == "onnx" && go.GetComponent<UavSimulator.CityDemo.OnnxTrafficLightAwareController>() == null)
            {
                go.AddComponent<UavSimulator.CityDemo.OnnxTrafficLightAwareController>();
            }
        }

        private TrackBase InstantiateTrack(TrackPluginDescriptor descriptor)
        {
            var parent = trackRoot != null ? trackRoot : transform;

            if (descriptor.prefab == null)
            {
                if (BuiltinPluginFactory.TryCreateTrackInstance(descriptor.id, parent, out var runtimeTrack))
                {
                    return runtimeTrack;
                }

                throw new InvalidOperationException($"Track plugin '{descriptor.id}' has no prefab assigned.");
            }

            var instance = Instantiate(descriptor.prefab, parent);
            var track = instance.GetComponentInChildren<TrackBase>();
            if (track == null)
            {
                throw new InvalidOperationException($"Track prefab for '{descriptor.id}' does not contain TrackBase.");
            }

            return track;
        }

        private void DestroyActiveInstances()
        {
            foreach (var vehicle in activeAgents.Select(agent => agent.Vehicle).Where(vehicle => vehicle != null).Distinct())
            {
                Destroy(vehicle.gameObject);
            }

            activeAgents.Clear();
            activeVehicle = null;
            activeAgentId = string.Empty;
            activeVehicleId = string.Empty;

            if (activeTrack != null)
            {
                Destroy(activeTrack.gameObject);
                activeTrack = null;
                activeTrackId = string.Empty;
            }

            Time.timeScale = defaultTimeScale;
        }

        private void BindExistingSceneObjects()
        {
            if (activeTrack == null && trackRoot != null)
            {
                activeTrack = trackRoot.GetComponentInChildren<TrackBase>(includeInactive: true);
            }
        }

        private void ConfigureRoute(ConfigKeyValue[] trackParams)
        {
            activeRouteWaypoints = Array.Empty<Vector3>();
            activeRouteWaypointIndex = 0;
            activeRouteReachDistance = 1f;
            activeRouteLoop = false;
            activeRouteCompleted = false;
            activeRouteWaypointsConfigured = false;
            var hasExplicitWaypoints = false;
            var hasExplicitReachDistance = false;

            if (trackParams == null || trackParams.Length == 0)
            {
                ApplyDefaultRouteIfAvailable(ref hasExplicitWaypoints, ref hasExplicitReachDistance);
                return;
            }

            foreach (var item in trackParams)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.key))
                {
                    continue;
                }

                if (string.Equals(item.key, RouteWaypointsKey, StringComparison.OrdinalIgnoreCase) &&
                    TryParseWaypoints(item.value, out var waypoints))
                {
                    activeRouteWaypoints = waypoints;
                    hasExplicitWaypoints = true;
                    activeRouteWaypointsConfigured = true;
                    continue;
                }

                if (string.Equals(item.key, RouteReachDistanceKey, StringComparison.OrdinalIgnoreCase) &&
                    float.TryParse(item.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var reachDistance) &&
                    reachDistance > 0f)
                {
                    activeRouteReachDistance = reachDistance;
                    hasExplicitReachDistance = true;
                    continue;
                }

                if (string.Equals(item.key, RouteLoopKey, StringComparison.OrdinalIgnoreCase) &&
                    TryParseBool(item.value, out var isLooping))
                {
                    activeRouteLoop = isLooping;
                }
            }

            ApplyDefaultRouteIfAvailable(ref hasExplicitWaypoints, ref hasExplicitReachDistance);
        }

        private void ApplyDefaultRouteIfAvailable(ref bool hasExplicitWaypoints, ref bool hasExplicitReachDistance)
        {
            if (!hasExplicitWaypoints)
            {
                // Prefer track-provided waypoints (e.g. from procedural maze)
                var trackWaypoints = activeTrack != null ? activeTrack.GetDefaultWaypoints() : null;
                if (trackWaypoints != null && trackWaypoints.Length > 0)
                {
                    activeRouteWaypoints = new Vector3[trackWaypoints.Length];
                    for (int i = 0; i < trackWaypoints.Length; i++)
                    {
                        activeRouteWaypoints[i] = new Vector3(trackWaypoints[i].x, 0f, trackWaypoints[i].y);
                    }
                    activeRouteWaypointsConfigured = true;
                }
                else
                {
                    activeRouteWaypoints = GetDefaultRouteWaypoints(activeTrackId);
                }
            }

            if (!hasExplicitReachDistance && activeRouteWaypoints.Length > 0)
            {
                activeRouteReachDistance = GetDefaultRouteReachDistance(activeTrackId);
            }
        }

        private bool UpdateRouteProgress(VehicleState state)
        {
            if (activeRouteWaypoints.Length == 0)
            {
                return false;
            }

            if (IsRouteCompleted(routeCompleted: false))
            {
                return true;
            }

            if (state == null || state.pose == null || state.pose.position == null)
            {
                return false;
            }

            var position = ToVector3(state.pose.position);
            while (activeRouteWaypointIndex < activeRouteWaypoints.Length)
            {
                var target = activeRouteWaypoints[activeRouteWaypointIndex];
                if (Vector3.Distance(position, target) > activeRouteReachDistance)
                {
                    break;
                }

                activeRouteWaypointIndex++;
                if (activeRouteWaypointIndex < activeRouteWaypoints.Length)
                {
                    continue;
                }

                if (activeRouteLoop)
                {
                    activeRouteWaypointIndex = 0;
                    activeRouteCompleted = false;
                    return false;
                }

                activeRouteCompleted = true;
                return true;
            }

            return false;
        }

        private ConfigKeyValue[] BuildRouteInfo(VehicleState state, bool routeCompleted)
        {
            if (activeRouteWaypoints.Length == 0)
            {
                return Array.Empty<ConfigKeyValue>();
            }

            var safeIndex = Mathf.Clamp(activeRouteWaypointIndex, 0, activeRouteWaypoints.Length);
            var remaining = activeRouteWaypoints.Length - safeIndex;
            var distanceToTarget = 0f;
            var completed = IsRouteCompleted(routeCompleted);

            if (safeIndex < activeRouteWaypoints.Length && state != null && state.pose != null && state.pose.position != null)
            {
                distanceToTarget = Vector3.Distance(ToVector3(state.pose.position), activeRouteWaypoints[safeIndex]);
            }

            return new[]
            {
                KV("route.total_waypoints", activeRouteWaypoints.Length.ToString(CultureInfo.InvariantCulture)),
                KV("route.current_index", safeIndex.ToString(CultureInfo.InvariantCulture)),
                KV("route.remaining_waypoints", remaining.ToString(CultureInfo.InvariantCulture)),
                KV("route.reach_distance_m", FormatFloat(activeRouteReachDistance)),
                KV("route.loop", activeRouteLoop ? "true" : "false"),
                KV("route.completed", completed ? "true" : "false"),
                KV("route.distance_to_target_m", FormatFloat(distanceToTarget)),
            };
        }

        private bool IsRouteCompleted(bool routeCompleted)
        {
            if (routeCompleted)
            {
                return true;
            }

            if (activeRouteLoop)
            {
                return false;
            }

            return activeRouteCompleted ||
                (activeRouteWaypoints.Length > 0 && activeRouteWaypointIndex >= activeRouteWaypoints.Length);
        }

        private static bool TryParseWaypoints(string raw, out Vector3[] waypoints)
        {
            waypoints = Array.Empty<Vector3>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var points = new List<Vector3>();
            var pointChunks = raw.Split(new[] { ';', '|', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var chunk in pointChunks)
            {
                var parts = chunk.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    continue;
                }

                if (!TryParseFloat(parts[0], out var x))
                {
                    continue;
                }

                if (parts.Length == 2)
                {
                    if (!TryParseFloat(parts[1], out var z))
                    {
                        continue;
                    }

                    points.Add(new Vector3(x, 0f, z));
                    continue;
                }

                if (!TryParseFloat(parts[1], out var y) || !TryParseFloat(parts[2], out var z3))
                {
                    continue;
                }

                points.Add(new Vector3(x, y, z3));
            }

            if (points.Count == 0)
            {
                return false;
            }

            waypoints = points.ToArray();
            return true;
        }

        private static bool TryParseFloat(string value, out float parsed)
        {
            return float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
        }

        private static bool TryParseBool(string value, out bool parsed)
        {
            parsed = false;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (bool.TryParse(value.Trim(), out parsed))
            {
                return true;
            }

            var normalized = value.Trim();
            if (string.Equals(normalized, "1", StringComparison.Ordinal))
            {
                parsed = true;
                return true;
            }

            if (string.Equals(normalized, "0", StringComparison.Ordinal))
            {
                parsed = false;
                return true;
            }

            return false;
        }

        private static string ReadConfigValue(ConfigKeyValue[] values, string key)
        {
            if (values == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            for (var i = 0; i < values.Length; i++)
            {
                var item = values[i];
                if (item == null || string.IsNullOrWhiteSpace(item.key))
                {
                    continue;
                }

                if (string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return item.value;
                }
            }

            return null;
        }

        private static void ApplyRuntimeGraphicsProfile(string rawProfile)
        {
            var profile = string.IsNullOrWhiteSpace(rawProfile) ? "high" : rawProfile.Trim().ToLowerInvariant();
            var qualityNames = QualitySettings.names;
            if (qualityNames == null || qualityNames.Length == 0)
            {
                return;
            }

            var explicitIndex = Array.FindIndex(qualityNames, name => string.Equals(name, rawProfile, StringComparison.OrdinalIgnoreCase));
            if (explicitIndex >= 0)
            {
                QualitySettings.SetQualityLevel(explicitIndex, applyExpensiveChanges: true);
            }
            else
            {
                var targetIndex = profile switch
                {
                    "performance" => 0,
                    "balanced" => Mathf.Clamp((qualityNames.Length - 1) / 2, 0, qualityNames.Length - 1),
                    "ultra" => qualityNames.Length - 1,
                    _ => Mathf.Clamp(qualityNames.Length - 2, 0, qualityNames.Length - 1),
                };
                QualitySettings.SetQualityLevel(targetIndex, applyExpensiveChanges: true);
            }

            switch (profile)
            {
                case "performance":
                    QualitySettings.antiAliasing = 0;
                    QualitySettings.shadowDistance = 35f;
                    QualitySettings.lodBias = 0.9f;
                    Application.targetFrameRate = 60;
                    break;
                case "balanced":
                    QualitySettings.antiAliasing = 0;
                    QualitySettings.shadowDistance = 65f;
                    QualitySettings.lodBias = 1.3f;
                    Application.targetFrameRate = 75;
                    break;
                case "ultra":
                    QualitySettings.antiAliasing = 0;
                    QualitySettings.shadowDistance = 140f;
                    QualitySettings.lodBias = 2.2f;
                    Application.targetFrameRate = 120;
                    break;
                default:
                    QualitySettings.antiAliasing = 0;
                    QualitySettings.shadowDistance = 100f;
                    QualitySettings.lodBias = 1.8f;
                    Application.targetFrameRate = 90;
                    break;
            }

            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.globalTextureMipmapLimit = 0;
            QualitySettings.vSyncCount = 0;
        }

        private static Vector3 ToVector3(Vector3f value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        private static ConfigKeyValue KV(string key, string value)
        {
            return new ConfigKeyValue
            {
                key = key,
                value = value,
            };
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void ApplyVehicleSpawn(VehicleBase vehicle, ConfigKeyValue[] trackParams, int agentIndex, int seed)
        {
            if (vehicle == null)
            {
                return;
            }

            var spawn = ResolveSpawnPose(trackParams, agentIndex, seed);
            vehicle.transform.position = spawn.position;
            vehicle.transform.rotation = Quaternion.Euler(0f, spawn.yawDeg, 0f);

            if (vehicle.TryGetComponent<Rigidbody>(out var body) && body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private (Vector3 position, float yawDeg) ResolveSpawnPose(ConfigKeyValue[] trackParams, int agentIndex, int seed)
        {
            var spawn = GetDefaultSpawnPose(activeTrackId);
            var hasExplicitSpawn = false;

            // If the track provides its own spawn (e.g. procedural maze), use it
            var trackSpawnPos = activeTrack != null ? activeTrack.GetDefaultSpawnPosition() : null;
            var trackSpawnYaw = activeTrack != null ? activeTrack.GetDefaultSpawnYawDeg() : null;
            if (trackSpawnPos.HasValue) spawn.position = trackSpawnPos.Value;
            if (trackSpawnYaw.HasValue) spawn.yawDeg = trackSpawnYaw.Value;

            // Plan 4 (rev39): per-episode spawn pose jitter. Sample from seed +
            // agentIndex so different agents get different jitter, and same
            // (seed, agent) reproduces same offset.
            float spawnJitterM = 0f;
            float spawnYawJitterDeg = 0f;
            if (TryReadTrackParam(trackParams, SpawnJitterMKey, out var jM) &&
                float.TryParse(jM, NumberStyles.Float, CultureInfo.InvariantCulture, out var pj))
            {
                spawnJitterM = Mathf.Max(0f, pj);
            }
            if (TryReadTrackParam(trackParams, SpawnYawJitterDegKey, out var jD) &&
                float.TryParse(jD, NumberStyles.Float, CultureInfo.InvariantCulture, out var yj))
            {
                spawnYawJitterDeg = Mathf.Max(0f, yj);
            }
            if (spawnJitterM > 0f || spawnYawJitterDeg > 0f)
            {
                var jitterSeed = unchecked(seed * 1000003 + agentIndex * 17);
                var rng = new System.Random(jitterSeed);
                if (spawnJitterM > 0f)
                {
                    var dx = ((float)rng.NextDouble() - 0.5f) * 2f * spawnJitterM;
                    var dz = ((float)rng.NextDouble() - 0.5f) * 2f * spawnJitterM;
                    spawn.position.x += dx;
                    spawn.position.z += dz;
                }
                if (spawnYawJitterDeg > 0f)
                {
                    var dyaw = ((float)rng.NextDouble() - 0.5f) * 2f * spawnYawJitterDeg;
                    spawn.yawDeg = (spawn.yawDeg + dyaw + 360f) % 360f;
                }
            }

            if (activeRouteWaypointsConfigured && activeRouteWaypoints.Length >= 2)
            {
                var first = activeRouteWaypoints[0];
                var second = activeRouteWaypoints[1];
                spawn.position = new Vector3(first.x, spawn.position.y, first.z);
                var direction = second - first;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    spawn.yawDeg = Quaternion.LookRotation(direction.normalized, Vector3.up).eulerAngles.y;
                }
            }

            if (TryReadTrackParam(trackParams, SpawnPositionKey, out var rawSpawnPosition) &&
                TryParseSpawnPosition(rawSpawnPosition, spawn.position.y, out var parsedPosition))
            {
                spawn.position = parsedPosition;
                hasExplicitSpawn = true;
            }

            if (TryReadTrackParam(trackParams, SpawnYawDegKey, out var rawYaw) &&
                float.TryParse(rawYaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedYaw))
            {
                spawn.yawDeg = parsedYaw;
            }

            if (!hasExplicitSpawn && agentIndex > 0)
            {
                spawn = OffsetSpawnPose(spawn, agentIndex);
            }

            return spawn;
        }

        private List<ResolvedAgentConfig> ResolveAgentConfigs(SimulationConfig config, VehiclePluginDescriptor defaultVehicle, bool allowEmptyAgents)
        {
            var result = new List<ResolvedAgentConfig>();
            var globalTrackParams = config.trackParams ?? Array.Empty<ConfigKeyValue>();
            var globalVehicleParams = config.vehicleParams ?? Array.Empty<ConfigKeyValue>();
            var configuredAgents = config.agents ?? Array.Empty<SimulationAgentConfig>();

            if (configuredAgents.Length == 0)
            {
                if (allowEmptyAgents)
                {
                    return result;
                }

                result.Add(new ResolvedAgentConfig
                {
                    AgentId = "ego",
                    IsPrimary = true,
                    Descriptor = defaultVehicle,
                    TrackParams = globalTrackParams,
                    VehicleParams = globalVehicleParams,
                });
                return result;
            }

            var hasPrimaryFlag = configuredAgents.Any(agent => agent != null && agent.isPrimary);
            for (var index = 0; index < configuredAgents.Length; index++)
            {
                var configured = configuredAgents[index];
                if (configured == null)
                {
                    continue;
                }

                var agentId = string.IsNullOrWhiteSpace(configured.agentId) ? $"agent-{index + 1}" : configured.agentId.Trim();
                var vehicleId = string.IsNullOrWhiteSpace(configured.vehicleId) ? defaultVehicle.id : configured.vehicleId.Trim();
                var descriptor = FindRequired(registry.Vehicles, vehicleId, "vehicle");

                result.Add(new ResolvedAgentConfig
                {
                    AgentId = agentId,
                    IsPrimary = hasPrimaryFlag ? configured.isPrimary : index == 0,
                    Descriptor = descriptor,
                    TrackParams = MergeConfig(config.trackParams, configured.trackParams),
                    VehicleParams = MergeConfig(config.vehicleParams, configured.vehicleParams),
                });
            }

            if (result.Count == 0)
            {
                if (allowEmptyAgents)
                {
                    return result;
                }

                throw new InvalidOperationException("Simulation config contains an empty agents list.");
            }

            if (!result.Any(agent => agent.IsPrimary))
            {
                result[0].IsPrimary = true;
            }

            return result;
        }

        private ActiveAgentRuntime ResolveTargetAgent(string targetAgentId, string targetVehicleId)
        {
            if (activeAgents.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(targetAgentId))
            {
                var matchByAgent = activeAgents.FirstOrDefault(agent =>
                    string.Equals(agent.AgentId, targetAgentId.Trim(), StringComparison.Ordinal));
                if (matchByAgent != null)
                {
                    return matchByAgent;
                }
            }

            if (!string.IsNullOrWhiteSpace(targetVehicleId))
            {
                var matchesByVehicle = activeAgents
                    .Where(agent => string.Equals(agent.VehicleId, targetVehicleId.Trim(), StringComparison.Ordinal))
                    .ToArray();
                if (matchesByVehicle.Length == 1)
                {
                    return matchesByVehicle[0];
                }

                if (matchesByVehicle.Length > 1)
                {
                    throw new InvalidOperationException(
                        $"Vehicle id '{targetVehicleId}' is ambiguous. Use targetAgentId to address a specific agent.");
                }
            }

            return activeAgents.FirstOrDefault(agent => agent.IsPrimary) ?? activeAgents[0];
        }

        private StepResult BuildStepResult(
            ActiveAgentRuntime target,
            VehicleState targetState,
            CameraFrame targetFrame,
            CameraFrame modelFrame,
            bool routeCompleted)
        {
            var targetRouteCompleted = target != null && target.IsPrimary && IsRouteCompleted(routeCompleted);
            var agentResults = new AgentStepResult[activeAgents.Count];
            for (var index = 0; index < activeAgents.Count; index++)
            {
                var agent = activeAgents[index];
                var state = ReferenceEquals(agent, target) ? targetState : agent.Vehicle.ReadState();
                agentResults[index] = new AgentStepResult
                {
                    agentId = agent.AgentId,
                    vehicleId = agent.VehicleId,
                    state = state,
                    frame = ReferenceEquals(agent, target) ? targetFrame : null,
                };
            }

            return new StepResult
            {
                activeAgentId = target.AgentId,
                activeVehicleId = target.VehicleId,
                state = targetState,
                reward = 0f,
                done = targetRouteCompleted,
                info = BuildStepInfo(target, targetState, targetRouteCompleted),
                frame = targetFrame,
                modelFrame = modelFrame,
                agents = agentResults,
            };
        }

        private ConfigKeyValue[] BuildStepInfo(ActiveAgentRuntime target, VehicleState state, bool routeCompleted)
        {
            var baseInfo = target != null && target.IsPrimary
                ? BuildRouteInfo(state, routeCompleted)
                : Array.Empty<ConfigKeyValue>();

            var extra = new[]
            {
                KV("agent.id", target?.AgentId ?? string.Empty),
                KV("agent.vehicle_id", target?.VehicleId ?? string.Empty),
                KV("agents.active_count", activeAgents.Count.ToString(CultureInfo.InvariantCulture)),
            };

            return baseInfo.Concat(extra).ToArray();
        }

        private void ApplyAgentInteractions(ConfigKeyValue[] flags)
        {
            var isolated = TryReadFlag(flags, "agents.isolated", out var isolatedValue) && isolatedValue;
            var seeEachOther = TryReadFlag(flags, "agents.see_each_other", out var visibleValue)
                ? visibleValue
                : !isolated;
            var collisionsEnabled = TryReadFlag(flags, "agents.collisions_enabled", out var collisionsValue)
                ? collisionsValue
                : !isolated;

            foreach (var agent in activeAgents)
            {
                if (agent?.Vehicle == null)
                {
                    continue;
                }

                SetLayerRecursively(agent.Vehicle.gameObject, seeEachOther ? 0 : VehicleBase.PeerVehicleLayer);
                agent.Vehicle.SetPeerVisibility(seeEachOther);
            }

            for (var i = 0; i < activeAgents.Count; i++)
            {
                for (var j = i + 1; j < activeAgents.Count; j++)
                {
                    SetCollisionPair(activeAgents[i].Vehicle, activeAgents[j].Vehicle, collisionsEnabled);
                }
            }
        }

        private static bool TryReadFlag(ConfigKeyValue[] flags, string key, out bool value)
        {
            value = false;
            if (flags == null || flags.Length == 0)
            {
                return false;
            }

            foreach (var flag in flags)
            {
                if (flag == null || string.IsNullOrWhiteSpace(flag.key))
                {
                    continue;
                }

                if (!string.Equals(flag.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return TryParseBool(flag.value, out value);
            }

            return false;
        }

        private static void SetCollisionPair(VehicleBase first, VehicleBase second, bool collisionsEnabled)
        {
            if (first == null || second == null)
            {
                return;
            }

            var firstColliders = first.GetComponentsInChildren<Collider>(includeInactive: false);
            var secondColliders = second.GetComponentsInChildren<Collider>(includeInactive: false);
            foreach (var firstCollider in firstColliders)
            {
                if (firstCollider == null)
                {
                    continue;
                }

                foreach (var secondCollider in secondColliders)
                {
                    if (secondCollider == null)
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(firstCollider, secondCollider, !collisionsEnabled);
                }
            }
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null)
            {
                return;
            }

            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                if (child == null)
                {
                    continue;
                }

                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static ConfigKeyValue[] MergeConfig(ConfigKeyValue[] shared, ConfigKeyValue[] specific)
        {
            if ((shared == null || shared.Length == 0) && (specific == null || specific.Length == 0))
            {
                return Array.Empty<ConfigKeyValue>();
            }

            var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void addItems(ConfigKeyValue[] source)
            {
                if (source == null)
                {
                    return;
                }

                foreach (var item in source)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.key))
                    {
                        continue;
                    }

                    items[item.key.Trim()] = item.value ?? string.Empty;
                }
            }

            addItems(shared);
            addItems(specific);

            return items
                .Select(item => new ConfigKeyValue
                {
                    key = item.Key,
                    value = item.Value,
                })
                .ToArray();
        }

        private static (Vector3 position, float yawDeg) OffsetSpawnPose((Vector3 position, float yawDeg) spawn, int agentIndex)
        {
            var row = (agentIndex + 1) / 2;
            var side = agentIndex % 2 == 0 ? 1f : -1f;
            var rotation = Quaternion.Euler(0f, spawn.yawDeg, 0f);
            var lateral = rotation * Vector3.right * (0.7f * row * side);
            var longitudinal = rotation * Vector3.back * (1.2f * row);
            return (spawn.position + lateral + longitudinal, spawn.yawDeg);
        }

        private static bool TryReadTrackParam(ConfigKeyValue[] trackParams, string key, out string value)
        {
            value = null;
            if (trackParams == null || trackParams.Length == 0)
            {
                return false;
            }

            foreach (var param in trackParams)
            {
                if (param == null || string.IsNullOrWhiteSpace(param.key))
                {
                    continue;
                }

                if (!string.Equals(param.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = param.value;
                return !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }

        private static bool TryReadConfigValue(ConfigKeyValue[] items, string key, out string value)
        {
            value = null;
            if (items == null || items.Length == 0)
            {
                return false;
            }

            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.key))
                {
                    continue;
                }

                if (!string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = item.value;
                return !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }

        private static bool TryParseSpawnPosition(string raw, float defaultY, out Vector3 position)
        {
            position = new Vector3(0f, defaultY, 0f);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var parts = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return false;
            }

            if (!TryParseFloat(parts[0], out var x))
            {
                return false;
            }

            if (parts.Length == 2)
            {
                if (!TryParseFloat(parts[1], out var z))
                {
                    return false;
                }

                position = new Vector3(x, defaultY, z);
                return true;
            }

            if (!TryParseFloat(parts[1], out var y) || !TryParseFloat(parts[2], out var z3))
            {
                return false;
            }

            position = new Vector3(x, y, z3);
            return true;
        }

        private static (Vector3 position, float yawDeg) GetDefaultSpawnPose(string trackId)
        {
            if (string.Equals(trackId, BuiltinPluginFactory.CardboardCorridorTrackId, StringComparison.Ordinal))
            {
                return (new Vector3(0f, 0.01f, -0.85f), 0f);
            }

            if (string.Equals(trackId, BuiltinPluginFactory.RoadSystemRealisticTrackId, StringComparison.Ordinal))
            {
                return (new Vector3(-11f, 0.2f, -11.8f), 3f);
            }

            if (string.Equals(trackId, BuiltinPluginFactory.RoadSystemArenaTrackId, StringComparison.Ordinal))
            {
                return (new Vector3(-6f, 0.2f, -8.5f), 0f);
            }

            if (string.Equals(trackId, BuiltinPluginFactory.BasicArenaTrackId, StringComparison.Ordinal))
            {
                return (new Vector3(0f, 0.2f, -7.5f), 0f);
            }

            return (new Vector3(0f, 0.2f, -6f), 0f);
        }

        private static Vector3[] GetDefaultRouteWaypoints(string trackId)
        {
            if (string.Equals(trackId, BuiltinPluginFactory.CardboardCorridorTrackId, StringComparison.Ordinal))
            {
                return (Vector3[])CardboardCorridorDefaultRoute.Clone();
            }

            return Array.Empty<Vector3>();
        }

        private static float GetDefaultRouteReachDistance(string trackId)
        {
            if (string.Equals(trackId, BuiltinPluginFactory.CardboardCorridorTrackId, StringComparison.Ordinal))
            {
                return 0.25f;
            }

            return 1f;
        }
    }
}
