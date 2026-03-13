using System;
using UavSimulator.Contracts;
using UnityEngine;

namespace UavSimulator.Vehicles
{
    public abstract class VehicleBase : MonoBehaviour
    {
        public const int PeerVehicleLayer = 30;

        [SerializeField] private string vehicleId;

        public string VehicleId => vehicleId;

        public abstract void ApplyControl(ControlCommand command);

        public virtual VehicleState ReadState()
        {
            var state = new VehicleState
            {
                pose = new Posef
                {
                    position = new Vector3f
                    {
                        x = transform.position.x,
                        y = transform.position.y,
                        z = transform.position.z,
                    },
                    rotation = new Quaternionf
                    {
                        x = transform.rotation.x,
                        y = transform.rotation.y,
                        z = transform.rotation.z,
                        w = transform.rotation.w,
                    },
                },
                linearVelocity = new Vector3f(),
                angularVelocity = new Vector3f(),
                speed = 0f,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                timeBase = "unix_ms",
            };

            return state;
        }

        public virtual bool TryReadCameraFrame(out CameraFrame frame)
        {
            frame = null;
            return false;
        }

        public virtual void ApplyVehicleConfig(ConfigKeyValue[] vehicleParams)
        {
        }

        public virtual void SetPeerVisibility(bool visible)
        {
        }

        public virtual void ResetVehicle(int seed)
        {
        }
    }
}
