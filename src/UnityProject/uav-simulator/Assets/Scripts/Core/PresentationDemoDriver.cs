using System.Globalization;
using UavSimulator.Contracts;
using UavSimulator.Vehicles;
using UnityEngine;

namespace UavSimulator.Core
{
    public sealed class PresentationDemoDriver : MonoBehaviour
    {
        [SerializeField] private bool autoDrive = true;
        [SerializeField] private float startDelaySec = 0.8f;

        private VehicleBase vehicle;
        private float elapsed;
        private int cycleIndex = -1;

        private void Start()
        {
            vehicle = FindFirstObjectByType<Ks0223Vehicle>();
            elapsed = 0f;
        }

        private void FixedUpdate()
        {
            if (!autoDrive || vehicle == null)
            {
                return;
            }

            elapsed += Time.fixedDeltaTime;
            if (elapsed < startDelaySec)
            {
                return;
            }

            var driveTime = elapsed - startDelaySec;
            var cycle = Mathf.FloorToInt(driveTime / 12f);
            if (cycle != cycleIndex)
            {
                cycleIndex = cycle;
                vehicle.ResetVehicle(seed: cycleIndex);
            }

            var t = driveTime % 12f;
            float left;
            float right;

            if (t < 3f)
            {
                left = 0.72f;
                right = 0.72f;
            }
            else if (t < 5f)
            {
                left = 0.45f;
                right = 0.78f;
            }
            else if (t < 8f)
            {
                left = 0.70f;
                right = 0.70f;
            }
            else if (t < 10f)
            {
                left = 0.78f;
                right = 0.45f;
            }
            else
            {
                left = 0.70f;
                right = 0.70f;
            }

            var command = new ControlCommand
            {
                throttle = 0f,
                steer = 0f,
                brake = 0f,
                timestamp = Mathf.RoundToInt(elapsed * 1000f),
                timeBase = "sim_ms",
                extensions = new[]
                {
                    new ConfigKeyValue { key = "drive.left_pwm_norm", value = left.ToString("0.###", CultureInfo.InvariantCulture) },
                    new ConfigKeyValue { key = "drive.right_pwm_norm", value = right.ToString("0.###", CultureInfo.InvariantCulture) },
                },
            };

            vehicle.ApplyControl(command);
        }
    }
}
