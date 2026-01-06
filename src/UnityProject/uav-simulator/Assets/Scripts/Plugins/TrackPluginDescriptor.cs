using UnityEngine;

namespace UavSimulator.Plugins
{
    [CreateAssetMenu(menuName = "UavSimulator/Plugins/Track Plugin", fileName = "TrackPlugin")]
    public sealed class TrackPluginDescriptor : PluginDescriptorBase
    {
        public GameObject prefab;
        [TextArea] public string parametersSchemaJson;
    }
}

