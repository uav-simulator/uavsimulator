using UnityEngine;

namespace UavSimulator.CityDemo
{
    /// <summary>
    /// Adapter wiring a <see cref="TrafficLight"/> FSM to a POLYGON-style
    /// traffic-light prefab whose bulbs share a single MeshRenderer with
    /// multiple material slots (lit/unlit variants per color).
    ///
    /// On state change, swaps the relevant material array entries on the
    /// renderer so that only the current colour appears illuminated.
    /// Use this on POLYGON City Pack "Traffic light 1-4 Prefab" instances;
    /// for hand-built traffic lights with three separate MeshRenderers,
    /// keep using the bulb references on <see cref="TrafficLight"/> directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrafficLightPolygonAdapter : MonoBehaviour
    {
        [Tooltip("MeshRenderer of the POLYGON traffic-light mesh (typically on a child named Traffic_light_N).")]
        [SerializeField] private MeshRenderer trafficLightRenderer;

        [Tooltip("FSM source. Adapter listens to its StateChanged event.")]
        [SerializeField] private TrafficLight trafficLight;

        [Header("Material slot indices (defaults match POLYGON Traffic light 1)")]
        [Tooltip("Material array indices that show the red bulb. Both lit and unlit slots are toggled together.")]
        [SerializeField] private int[] redSlots = new[] { 1, 4 };

        [Tooltip("Material array indices that show the yellow bulb.")]
        [SerializeField] private int[] yellowSlots = new[] { 2, 5 };

        [Tooltip("Material array indices that show the green bulb.")]
        [SerializeField] private int[] greenSlots = new[] { 3, 6 };

        [Header("Materials (drag from POLYGON City Pack/Materials/traffic light/)")]
        [SerializeField] private Material redOff;
        [SerializeField] private Material redOn;
        [SerializeField] private Material yellowOff;
        [SerializeField] private Material yellowOn;
        [SerializeField] private Material greenOff;
        [SerializeField] private Material greenOn;

        private void OnEnable()
        {
            if (trafficLight != null)
            {
                trafficLight.StateChanged += OnStateChanged;
                OnStateChanged(trafficLight.State);
            }
        }

        private void OnDisable()
        {
            if (trafficLight != null)
            {
                trafficLight.StateChanged -= OnStateChanged;
            }
        }

        private void OnStateChanged(TrafficLightState state)
        {
            if (trafficLightRenderer == null)
            {
                return;
            }

            // .materials creates per-instance copies, allowing multiple traffic
            // lights to display different states without sharing one material.
            var materials = trafficLightRenderer.materials;

            ApplyToSlots(materials, redSlots, state == TrafficLightState.Red ? redOn : redOff);
            ApplyToSlots(materials, yellowSlots, state == TrafficLightState.Yellow ? yellowOn : yellowOff);
            ApplyToSlots(materials, greenSlots, state == TrafficLightState.Green ? greenOn : greenOff);

            trafficLightRenderer.materials = materials;
        }

        private static void ApplyToSlots(Material[] materials, int[] slots, Material target)
        {
            if (target == null || slots == null)
            {
                return;
            }

            for (var i = 0; i < slots.Length; i++)
            {
                var idx = slots[i];
                if (idx >= 0 && idx < materials.Length)
                {
                    materials[idx] = target;
                }
            }
        }

        /// <summary>
        /// Procedural setup helper: assigns the six material variants in code.
        /// Yellow has only one variant in the shipping POLYGON pack — pass the
        /// same material for both yellow parameters.
        /// </summary>
        public void SetMaterials(
            Material redOff,
            Material redOn,
            Material yellowOff,
            Material yellowOn,
            Material greenOff,
            Material greenOn)
        {
            this.redOff = redOff;
            this.redOn = redOn;
            this.yellowOff = yellowOff;
            this.yellowOn = yellowOn;
            this.greenOff = greenOff;
            this.greenOn = greenOn;
        }

        /// <summary>
        /// Fluent helper for procedural setup (e.g. CityPolygonTrack):
        /// links this adapter to its FSM and renderer in code.
        /// </summary>
        public void Bind(TrafficLight light, MeshRenderer renderer)
        {
            if (trafficLight != null)
            {
                trafficLight.StateChanged -= OnStateChanged;
            }
            trafficLight = light;
            trafficLightRenderer = renderer;
            if (trafficLight != null && isActiveAndEnabled)
            {
                trafficLight.StateChanged += OnStateChanged;
                OnStateChanged(trafficLight.State);
            }
        }
    }
}
