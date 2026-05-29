using System;
using System.Collections.Generic;
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

            state.telemetry = MergeTelemetry(state.telemetry);
            return state;
        }

        /// <summary>
        /// Combine vehicle-specific telemetry (if any) with entries produced by
        /// every <see cref="IVehicleStateExtender"/> component on this GameObject.
        /// Subclasses that fill <c>state.telemetry</c> in their own override should
        /// call this with their telemetry array so plug-in extender entries are
        /// preserved on the wire.
        /// </summary>
        protected ConfigKeyValue[] MergeTelemetry(ConfigKeyValue[] existing)
        {
            var extenders = GetComponents<IVehicleStateExtender>();
            if (extenders.Length == 0) return existing;

            var list = new List<ConfigKeyValue>();
            if (existing != null) list.AddRange(existing);

            foreach (var e in extenders)
            {
                var entries = e.BuildExtensions();
                if (entries == null) continue;
                foreach (var kv in entries)
                {
                    if (kv != null) list.Add(kv);
                }
            }
            return list.Count > 0 ? list.ToArray() : existing;
        }

        public virtual bool TryReadCameraFrame(out CameraFrame frame)
        {
            frame = null;
            return false;
        }

        public virtual bool TryReadCameraFrame(string captureMode, out CameraFrame frame)
        {
            return TryReadCameraFrame(out frame);
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
