using UavSimulator.Core;
using UnityEngine;

namespace UavSimulator.Api
{
    public sealed class HttpJsonApiHost : MonoBehaviour
    {
        [SerializeField] private SimulationManager simulationManager;
        [SerializeField] private int port = 8000;
        [SerializeField] private bool autoStart = true;

        private HttpJsonSimulatorApiServer server;
        public int Port => port;

        private void Awake()
        {
            if (simulationManager == null)
            {
                simulationManager = FindFirstObjectByType<SimulationManager>();
            }

            _ = UnityMainThreadDispatcher.Instance;

            if (simulationManager == null)
            {
                throw new System.InvalidOperationException($"{nameof(HttpJsonApiHost)} requires a {nameof(SimulationManager)} in the scene.");
            }

            var facade = new SimulatorApiFacade(simulationManager);
            server = new HttpJsonSimulatorApiServer(facade, port);
        }

        private void Start()
        {
            if (autoStart)
            {
                server.Start();
            }
        }

        private void OnDestroy()
        {
            server?.Stop();
        }
    }
}
