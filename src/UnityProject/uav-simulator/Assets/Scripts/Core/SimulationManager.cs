using System;
using System.Linq;
using UavSimulator.Contracts;
using UavSimulator.Plugins;
using UavSimulator.Tracks;
using UavSimulator.Vehicles;
using UnityEngine;

namespace UavSimulator.Core
{
    public sealed class SimulationManager : MonoBehaviour
    {
        [SerializeField] private Transform trackRoot;
        [SerializeField] private Transform vehicleRoot;

        private PluginRegistrySnapshot registry;
        private TrackBase activeTrack;
        private VehicleBase activeVehicle;

        private void Awake()
        {
            registry = PluginRegistry.Load();
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
            if (config == null) throw new ArgumentNullException(nameof(config));

            var vehicleDescriptor = FindRequired(registry.Vehicles, config.selectedVehicleId, "vehicle");
            var trackDescriptor = FindRequired(registry.Tracks, config.selectedTrackId, "track");

            DestroyActiveInstances();

            activeTrack = InstantiateTrack(trackDescriptor);
            activeVehicle = InstantiateVehicle(vehicleDescriptor);

            activeTrack.ResetTrack(config.seed);
            activeVehicle.ResetVehicle(config.seed);

            if (config.timeScale > 0f)
            {
                Time.timeScale = config.timeScale;
            }
        }

        public StepResult Step(ControlCommand command)
        {
            if (activeVehicle == null)
            {
                throw new InvalidOperationException("Active vehicle is not initialized. Call ResetSimulation first.");
            }

            activeVehicle.ApplyControl(command);

            var result = new StepResult
            {
                state = activeVehicle.ReadState(),
                reward = 0f,
                done = false,
                info = Array.Empty<ConfigKeyValue>(),
                frame = null,
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
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException($"Missing selected {kind} id.", nameof(id));
            }

            var match = (items ?? Array.Empty<T>()).FirstOrDefault(v => v != null && v.id == id);
            if (match == null)
            {
                throw new InvalidOperationException($"Unknown {kind} id: '{id}'.");
            }

            return match;
        }

        private VehicleBase InstantiateVehicle(VehiclePluginDescriptor descriptor)
        {
            if (descriptor.prefab == null)
            {
                throw new InvalidOperationException($"Vehicle plugin '{descriptor.id}' has no prefab assigned.");
            }

            var parent = vehicleRoot != null ? vehicleRoot : transform;
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
            if (descriptor.prefab == null)
            {
                throw new InvalidOperationException($"Track plugin '{descriptor.id}' has no prefab assigned.");
            }

            var parent = trackRoot != null ? trackRoot : transform;
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
            }

            if (activeTrack != null)
            {
                Destroy(activeTrack.gameObject);
                activeTrack = null;
            }
        }
    }
}
