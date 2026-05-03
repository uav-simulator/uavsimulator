using UavSimulator.Tracks;
using UnityEngine;

namespace UavSimulator.CityDemo
{
    /// <summary>
    /// Track for the City Demo scenario. Owns the intersection controllers
    /// and a transform root reserved for future NPC traffic spawning.
    ///
    /// Geometry (buildings, roads, props) is expected to come from imported
    /// city assets and is not constructed procedurally here. <see cref="ResetTrack"/>
    /// only restarts the traffic-light cycles deterministically per seed.
    /// </summary>
    public sealed class CityDemoTrack : TrackBase
    {
        [SerializeField] private TrafficLightController[] trafficLightControllers;
        [SerializeField] private Transform npcTrafficSpawnerRoot;

        public override void ResetTrack(int seed)
        {
            if (trafficLightControllers == null) return;

            var rng = new System.Random(seed);
            for (var i = 0; i < trafficLightControllers.Length; i++)
            {
                var controller = trafficLightControllers[i];
                if (controller == null) continue;
                controller.ResetCycle(rng.Next());
                controller.StartCycle();
            }
        }
    }
}
