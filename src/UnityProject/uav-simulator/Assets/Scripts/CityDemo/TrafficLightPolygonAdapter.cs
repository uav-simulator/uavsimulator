using System.Collections.Generic;
using UavSimulator.Core;
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

        private static readonly Color InactiveBulbColor = new Color(0.025f, 0.025f, 0.02f, 1f);

        private readonly Dictionary<Material, Material> compatibleMaterialCache = new();
        private readonly Dictionary<Material, Material> inactiveMaterialCache = new();

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

            var materials = trafficLightRenderer.sharedMaterials;

            ApplyToSlots(materials, redSlots, ResolveRuntimeMaterial(state == TrafficLightState.Red ? redOn : redOff, state == TrafficLightState.Red));
            ApplyToSlots(materials, yellowSlots, ResolveRuntimeMaterial(state == TrafficLightState.Yellow ? yellowOn : yellowOff, state == TrafficLightState.Yellow));
            ApplyToSlots(materials, greenSlots, ResolveRuntimeMaterial(state == TrafficLightState.Green ? greenOn : greenOff, state == TrafficLightState.Green));

            trafficLightRenderer.sharedMaterials = materials;
        }

        private Material ResolveRuntimeMaterial(Material source, bool active)
        {
            if (source == null)
            {
                return null;
            }

            if (!active)
            {
                return ResolveInactiveRuntimeMaterial(source);
            }

            if (!RuntimeMaterialCompatibility.NeedsReplacement(source))
            {
                return source;
            }

            if (compatibleMaterialCache.TryGetValue(source, out var cached) && cached != null)
            {
                return cached;
            }

            var replacement = RuntimeMaterialCompatibility.CreateReplacementMaterial(source, defaultSmoothness: 0.2f, copyTextures: true);
            replacement.name = $"{source.name} Runtime";
            compatibleMaterialCache[source] = replacement;
            return replacement;
        }

        private Material ResolveInactiveRuntimeMaterial(Material source)
        {
            if (inactiveMaterialCache.TryGetValue(source, out var cached) && cached != null)
            {
                return cached;
            }

            var replacement = RuntimeMaterialCompatibility.CreateReplacementMaterial(source, defaultSmoothness: 0.2f, copyTextures: false);
            replacement.name = $"{source.name} Dim Runtime";
            ApplyDisplayColor(replacement, InactiveBulbColor);
            if (replacement.HasProperty("_EmissionColor"))
            {
                replacement.SetColor("_EmissionColor", Color.black);
            }

            replacement.DisableKeyword("_EMISSION");
            replacement.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            inactiveMaterialCache[source] = replacement;
            return replacement;
        }

        private static void ApplyDisplayColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
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
