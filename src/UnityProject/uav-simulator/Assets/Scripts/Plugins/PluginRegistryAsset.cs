using UnityEngine;

namespace UavSimulator.Plugins
{
    [CreateAssetMenu(menuName = "UavSimulator/Plugins/Registry", fileName = "PluginRegistry")]
    public sealed class PluginRegistryAsset : ScriptableObject
    {
        public VehiclePluginDescriptor[] vehicles;
        public TrackPluginDescriptor[] tracks;
    }
}

