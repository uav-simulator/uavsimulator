using UnityEngine;

namespace UavSimulator.CityDemo
{
    /// <summary>
    /// Drives a camera through a sequence of cinematic angles defined in a
    /// <see cref="CinematicAngles"/> asset. Each angle is held for
    /// <see cref="CinematicAngles.Angle.durationSec"/> seconds (minimum 2s)
    /// before the controller cuts to the next entry. The sequence loops.
    ///
    /// Set <see cref="IsRunning"/> = false to pause cycling without disabling
    /// the component (useful when a downstream UI takes control of the camera).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CinematicCameraController : MonoBehaviour
    {
        [SerializeField] private CinematicAngles angles;
        [SerializeField] private bool runOnStart = true;

        public bool IsRunning { get; set; }

        private Camera cam;
        private int index;
        private float nextSwitchAt;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            IsRunning = runOnStart;
            if (angles != null && angles.angles.Count > 0) Apply(angles.angles[0]);
            ScheduleNext();
        }

        private void Update()
        {
            if (!IsRunning || angles == null || angles.angles.Count == 0) return;
            if (Time.time < nextSwitchAt) return;
            index = (index + 1) % angles.angles.Count;
            Apply(angles.angles[index]);
            ScheduleNext();
        }

        private void Apply(CinematicAngles.Angle a)
        {
            transform.position = a.position;
            transform.eulerAngles = a.eulerAngles;
            cam.fieldOfView = a.fov > 0 ? a.fov : 60f;
        }

        private void ScheduleNext()
        {
            if (angles == null || angles.angles.Count == 0)
            {
                nextSwitchAt = float.PositiveInfinity;
                return;
            }
            nextSwitchAt = Time.time + Mathf.Max(2f, angles.angles[index].durationSec);
        }
    }
}
