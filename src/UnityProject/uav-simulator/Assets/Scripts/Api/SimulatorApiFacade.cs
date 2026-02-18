using System;
using UavSimulator.Contracts;
using UavSimulator.Core;
using UnityEngine;

namespace UavSimulator.Api
{
    public sealed class SimulatorApiFacade
    {
        private readonly SimulationManager simulationManager;

        public SimulatorApiFacade(SimulationManager simulationManager)
        {
            this.simulationManager = simulationManager != null
                ? simulationManager
                : throw new ArgumentNullException(nameof(simulationManager));
        }

        public SimulatorContractDescriptor GetContract() => simulationManager.GetContract();

        public StepResult Reset(SimulationConfig config)
        {
            simulationManager.ResetSimulation(config);
            simulationManager.TryReadCameraFrame(out var frame);

            var result = new StepResult
            {
                state = simulationManager.ReadState(),
                reward = 0f,
                done = false,
                info = Array.Empty<ConfigKeyValue>(),
                frame = frame,
            };

            return result;
        }

        public StepResult Step(ControlCommand command) => simulationManager.Step(command);

        public static string ToJson<T>(T value) => JsonUtility.ToJson(value, prettyPrint: false);

        public static T FromJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Request body is empty.", nameof(json));
            }

            return JsonUtility.FromJson<T>(json);
        }
    }
}
