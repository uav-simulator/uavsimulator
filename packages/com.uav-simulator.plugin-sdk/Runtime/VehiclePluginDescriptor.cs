using UnityEngine;
using UavSimulator.Contracts;

namespace UavSimulator.Plugins
{
    [CreateAssetMenu(menuName = "UavSimulator/Plugins/Vehicle Plugin", fileName = "VehiclePlugin")]
    public sealed class VehiclePluginDescriptor : PluginDescriptorBase
    {
        public GameObject prefab;
        public DeviceContractDescriptorAsset deviceContract;
    }
}
