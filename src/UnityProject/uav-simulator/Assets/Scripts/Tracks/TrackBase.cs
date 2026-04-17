using UavSimulator.Contracts;
using UnityEngine;

namespace UavSimulator.Tracks
{
    public abstract class TrackBase : MonoBehaviour
    {
        [SerializeField] private string trackId;

        public string TrackId => trackId;

        /// <summary>
        /// Apply track-specific parameters from the reset payload.
        /// Called BEFORE ResetTrack so the track can generate geometry based on params.
        /// </summary>
        public virtual void ApplyTrackParams(ConfigKeyValue[] trackParams)
        {
        }

        public virtual void ResetTrack(int seed)
        {
        }

        /// <summary>
        /// Optional: track-specific default spawn position (world coords).
        /// Returns null if track has no specific spawn — scenario or fallback is used.
        /// </summary>
        public virtual Vector3? GetDefaultSpawnPosition() => null;

        /// <summary>
        /// Optional: track-specific default spawn yaw (degrees).
        /// </summary>
        public virtual float? GetDefaultSpawnYawDeg() => null;

        /// <summary>
        /// Optional: track-specific waypoints (world XZ coords).
        /// If non-null and non-empty, SimulationManager may use these when no waypoints in scenario.
        /// </summary>
        public virtual Vector2[] GetDefaultWaypoints() => null;
    }
}
