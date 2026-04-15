using UavSimulator.Vehicles;
using UnityEngine;

namespace UavSimulator.Core
{
    /// <summary>
    /// Main camera controller with two modes:
    /// - Follow mode (default): chase camera behind the vehicle
    /// - Free-fly mode (press F): WASD + mouse look to explore the scene
    /// Press F to toggle between modes. Scroll wheel adjusts speed in free-fly.
    /// </summary>
    public sealed class PresentationFollowCamera : MonoBehaviour
    {
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 2.8f, -4.8f);
        [SerializeField] private float positionLerp = 4.5f;
        [SerializeField] private float lookAheadDistance = 2.2f;
        [SerializeField] private float lookHeight = 0.55f;

        [Header("Free-Fly")]
        [SerializeField] private float flySpeed = 2.0f;
        [SerializeField] private float flySpeedFast = 6.0f;
        [SerializeField] private float mouseSensitivity = 2.5f;

        private Transform target;
        private bool freeFlying;
        private float yaw;
        private float pitch;
        private float currentFlySpeed;

        // Auto-attach disabled — TrackOverviewCamera is now the default camera.
        // This class is kept for backward compatibility but no longer auto-attaches.

        private void Start()
        {
            var vehicle = FindFirstObjectByType<Ks0223Vehicle>();
            target = vehicle != null ? vehicle.transform : null;
            currentFlySpeed = flySpeed;
            if (target != null)
            {
                SnapToTarget();
            }
        }

        private void Update()
        {
            // Toggle free-fly with F key
            if (Input.GetKeyDown(KeyCode.F))
            {
                freeFlying = !freeFlying;
                if (freeFlying)
                {
                    var euler = transform.eulerAngles;
                    yaw = euler.y;
                    pitch = euler.x;
                    if (pitch > 180f) pitch -= 360f;
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }

            // Scroll wheel adjusts fly speed
            if (freeFlying)
            {
                var scroll = Input.GetAxis("Mouse ScrollWheel");
                if (scroll != 0f)
                {
                    currentFlySpeed = Mathf.Clamp(currentFlySpeed + scroll * 3f, 0.2f, 20f);
                }
            }
        }

        private void LateUpdate()
        {
            if (freeFlying)
            {
                UpdateFreeFly();
                return;
            }

            if (target == null)
            {
                var vehicle = FindFirstObjectByType<Ks0223Vehicle>();
                target = vehicle != null ? vehicle.transform : null;
                if (target == null)
                {
                    return;
                }
            }

            var desired = target.TransformPoint(followOffset);
            var alpha = 1f - Mathf.Exp(-positionLerp * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, alpha);

            var lookAtPoint = target.position + target.forward * lookAheadDistance + Vector3.up * lookHeight;
            transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.LookRotation(lookAtPoint - transform.position), alpha);
        }

        private void UpdateFreeFly()
        {
            // Mouse look
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            // WASD + QE movement
            var speed = Input.GetKey(KeyCode.LeftShift) ? flySpeedFast : currentFlySpeed;
            var move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += transform.forward;
            if (Input.GetKey(KeyCode.S)) move -= transform.forward;
            if (Input.GetKey(KeyCode.A)) move -= transform.right;
            if (Input.GetKey(KeyCode.D)) move += transform.right;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;
            transform.position += move.normalized * (speed * Time.deltaTime);
        }

        private void SnapToTarget()
        {
            var desired = target.TransformPoint(followOffset);
            transform.position = desired;
            var lookAtPoint = target.position + target.forward * lookAheadDistance + Vector3.up * lookHeight;
            transform.rotation = Quaternion.LookRotation(lookAtPoint - transform.position);
        }
    }
}
