using UnityEngine;

namespace UavSimulator.Tracks
{
    public abstract class TrackBase : MonoBehaviour
    {
        [SerializeField] private string trackId;

        public string TrackId => trackId;

        public virtual void ResetTrack(int seed)
        {
        }
    }
}

