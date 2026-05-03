using UnityEngine;

namespace UavSimulator.CityDemo
{
    /// <summary>
    /// State of a single traffic-light bulb cluster.
    /// </summary>
    public enum TrafficLightState
    {
        Red,
        Green,
        Yellow,
    }

    /// <summary>
    /// Single traffic light. Pure state container + bulb emission swap.
    /// Time-based advancement is delegated to <see cref="TrafficLightController"/>;
    /// this component only reacts to <see cref="SetState"/>.
    /// </summary>
    public sealed class TrafficLight : MonoBehaviour
    {
        // Per-light cycle data. Read by the controller that drives this light.
        public float redSeconds = 8f;
        public float greenSeconds = 10f;
        public float yellowSeconds = 2f;

        [SerializeField] private MeshRenderer redBulb;
        [SerializeField] private MeshRenderer greenBulb;
        [SerializeField] private MeshRenderer yellowBulb;

        [SerializeField] private Color redColor = new Color(1.0f, 0.10f, 0.05f);
        [SerializeField] private Color greenColor = new Color(0.10f, 1.0f, 0.20f);
        [SerializeField] private Color yellowColor = new Color(1.0f, 0.85f, 0.10f);
        [SerializeField] private Color offColor = new Color(0.05f, 0.05f, 0.05f);

        public TrafficLightState State { get; private set; } = TrafficLightState.Red;

        public event System.Action<TrafficLightState> StateChanged;

        private void OnEnable()
        {
            ApplyEmission(State);
        }

        /// <summary>
        /// Set the current state and update bulb emissions.
        /// Fires <see cref="StateChanged"/> only when the state actually changes.
        /// </summary>
        public void SetState(TrafficLightState newState)
        {
            var changed = State != newState;
            State = newState;
            ApplyEmission(newState);
            if (changed)
            {
                StateChanged?.Invoke(newState);
            }
        }

        /// <summary>
        /// Reset cycle with a deterministic seed. Currently sets state to Red;
        /// per-light starting offset randomization is the controller's job, but
        /// this method exists so external code can request a deterministic reset.
        /// </summary>
        public void ResetCycle(int seed)
        {
            // Seed retained for future use (e.g. random initial state in standalone mode).
            // Controller-driven mode just resets to Red.
            SetState(TrafficLightState.Red);
        }

        private void ApplyEmission(TrafficLightState state)
        {
            SetBulb(redBulb, state == TrafficLightState.Red ? redColor : offColor);
            SetBulb(greenBulb, state == TrafficLightState.Green ? greenColor : offColor);
            SetBulb(yellowBulb, state == TrafficLightState.Yellow ? yellowColor : offColor);
        }

        private static void SetBulb(MeshRenderer renderer, Color color)
        {
            if (renderer == null) return;
            // Use the instance material so multiple lights don't share one mat.
            var mat = renderer.material;
            if (mat == null) return;
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", color);
                mat.EnableKeyword("_EMISSION");
            }
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
            else if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }
        }
    }
}
