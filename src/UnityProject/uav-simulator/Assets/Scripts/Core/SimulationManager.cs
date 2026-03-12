using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        public string activeVehicleId;
        public string activeTrackId;
    }

    public sealed class SimulationManager : MonoBehaviour
    {
        private const string RouteWaypointsKey = "route.waypoints";
        private const string RouteReachDistanceKey = "route.reach_distance_m";
        private const string RouteLoopKey = "route.loop";

        [SerializeField] private Transform trackRoot;
        [SerializeField] private Transform vehicleRoot;

        private PluginRegistrySnapshot registry;
        private TrackBase activeTrack;
        private VehicleBase activeVehicle;
        private float defaultTimeScale = 1f;
        private Vector3[] activeRouteWaypoints = Array.Empty<Vector3>();
        private int activeRouteWaypointIndex;
        private float activeRouteReachDistance = 1f;
        private bool activeRouteLoop;
        private string activeTrackId = string.Empty;
        private string activeVehicleId = string.Empty;

        private void Awake()
        {
            registry = PluginRegistry.Load();
            defaultTimeScale = Time.timeScale;
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

            DestroyActiveInstances();

            activeTrack = InstantiateTrack(validation.Track);
            activeVehicle = InstantiateVehicle(validation.Vehicle);
            activeTrackId = validation.Track != null ? validation.Track.id ?? string.Empty : string.Empty;
            activeVehicleId = validation.Vehicle != null ? validation.Vehicle.id ?? string.Empty : string.Empty;

            activeTrack.ResetTrack(config.seed);
            activeVehicle.ResetVehicle(config.seed);

            Time.timeScale = validation.TimeScale;
            ConfigureRoute(config.trackParams);
        }

        public SimulationRuntimeDiagnostics GetDiagnostics()
        {
            return new SimulationRuntimeDiagnostics
            {
                pluginRegistrySource = registry != null ? registry.Source.ToString() : PluginRegistrySource.BuiltinFactory.ToString(),
                availableVehicles = registry?.Vehicles?.Length ?? 0,
                availableTracks = registry?.Tracks?.Length ?? 0,
                activeVehicleId = activeVehicleId ?? string.Empty,
                activeTrackId = activeTrackId ?? string.Empty,
            };
        }

        public StepResult Step(ControlCommand command)
        {
            if (activeVehicle == null)
            {
                throw new InvalidOperationException("Active vehicle is not initialized. Call ResetSimulation first.");
            }

            activeVehicle.ApplyControl(command);
            activeVehicle.TryReadCameraFrame(out var frame);
            var state = activeVehicle.ReadState();
            var routeCompleted = UpdateRouteProgress(state);

            var result = new StepResult
            {
                state = state,
                reward = 0f,
                done = routeCompleted,
                info = BuildRouteInfo(state, routeCompleted),
                frame = frame,
            };

            return result;
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
            if (activeVehicle != null)
            {
                Destroy(activeVehicle.gameObject);
                activeVehicle = null;
                activeVehicleId = string.Empty;
            }

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

            if (trackParams == null || trackParams.Length == 0)
            {
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
                    continue;
                }

                if (string.Equals(item.key, RouteReachDistanceKey, StringComparison.OrdinalIgnoreCase) &&
                    float.TryParse(item.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var reachDistance) &&
                    reachDistance > 0f)
                {
                    activeRouteReachDistance = reachDistance;
                    continue;
                }

                if (string.Equals(item.key, RouteLoopKey, StringComparison.OrdinalIgnoreCase) &&
                    TryParseBool(item.value, out var isLooping))
                {
                    activeRouteLoop = isLooping;
                }
            }
        }

        private bool UpdateRouteProgress(VehicleState state)
        {
            if (activeRouteWaypoints.Length == 0)
            {
                return false;
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
                    return false;
                }

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
                KV("route.completed", routeCompleted ? "true" : "false"),
                KV("route.distance_to_target_m", FormatFloat(distanceToTarget)),
            };
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
    }
}
