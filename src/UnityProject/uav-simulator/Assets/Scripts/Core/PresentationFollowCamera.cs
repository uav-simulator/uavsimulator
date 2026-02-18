using UavSimulator.Vehicles;
using UnityEngine;

namespace UavSimulator.Core
{
    public sealed class PresentationFollowCamera : MonoBehaviour
    {
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 2.8f, -4.8f);
        [SerializeField] private float positionLerp = 4.5f;
        [SerializeField] private float lookAheadDistance = 2.2f;
        [SerializeField] private float lookHeight = 0.55f;

        private Transform target;

        private void Start()
        {
            var vehicle = FindFirstObjectByType<Ks0223Vehicle>();
            target = vehicle != null ? vehicle.transform : null;
            if (target != null)
            {
                SnapToTarget();
            }
        }

        private void LateUpdate()
        {
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

        private void SnapToTarget()
        {
            var desired = target.TransformPoint(followOffset);
            transform.position = desired;
            var lookAtPoint = target.position + target.forward * lookAheadDistance + Vector3.up * lookHeight;
            transform.rotation = Quaternion.LookRotation(lookAtPoint - transform.position);
        }
    }
}
