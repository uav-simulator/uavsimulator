using UavSimulator.Core;
using UnityEngine;

namespace UavSimulator.Api
{
    public sealed class HttpJsonApiHost : MonoBehaviour
    {
        [SerializeField] private SimulationManager simulationManager;
        [SerializeField] private int port = 8000;
        [SerializeField] private string host = "127.0.0.1";
        [SerializeField] private bool autoStart = true;

        private HttpJsonSimulatorApiServer server;
        public int Port => port;
        public string Host => host;

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

            ApplyEnvironmentOverrides();

            var facade = new SimulatorApiFacade(simulationManager);
            server = new HttpJsonSimulatorApiServer(facade, port, host);
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

        private void ApplyEnvironmentOverrides()
        {
            var hostOverride = System.Environment.GetEnvironmentVariable("UAVSIM_API_HOST");
            if (!string.IsNullOrWhiteSpace(hostOverride))
            {
                host = hostOverride.Trim();
            }

            var portOverride = System.Environment.GetEnvironmentVariable("UAVSIM_API_PORT");
            if (!string.IsNullOrWhiteSpace(portOverride) && int.TryParse(portOverride, out var parsedPort))
            {
                port = Mathf.Clamp(parsedPort, 1, 65535);
            }
        }
    }
}
