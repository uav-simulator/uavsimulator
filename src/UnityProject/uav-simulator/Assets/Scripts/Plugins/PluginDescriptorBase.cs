using UnityEngine;
using UavSimulator.Contracts;

namespace UavSimulator.Plugins
{
    public abstract class PluginDescriptorBase : ScriptableObject
    {
        public string id;
        public string displayName;
        public ContractVersion version;
        [TextArea] public string description;
    }
}

