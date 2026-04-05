using UnityEngine;

namespace UavSimulator.Contracts
{
    [CreateAssetMenu(menuName = "UavSimulator/Contracts/Device Contract Descriptor", fileName = "DeviceContractDescriptor")]
    public sealed class DeviceContractDescriptorAsset : ScriptableObject
    {
        public ContractVersion contractVersion;
        public DeviceContractDescriptor descriptor;
    }
}
