using System.Collections.Generic;
using System.Globalization;
using UavSimulator.Contracts;
using UavSimulator.Vehicles;
using UnityEngine;

namespace UavSimulator.Core
{
    public sealed class PresentationTelemetryHud : MonoBehaviour
    {
        [SerializeField] private bool visible = true;

        private Ks0223Vehicle vehicle;
        private GUIStyle labelStyle;
        private GUIStyle boxStyle;

        private void Start()
        {
            vehicle = FindFirstObjectByType<Ks0223Vehicle>();
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                normal = { textColor = new Color(0.96f, 0.97f, 0.99f) },
            };
            boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = Texture2D.blackTexture },
            };
        }

        private void OnGUI()
        {
            if (!visible || vehicle == null)
            {
                return;
            }

            var state = vehicle.ReadState();
            var telemetry = ToMap(state.telemetry);

            var speed = state.speed;
            var ultrasonic = Parse(telemetry, "sensor.ultrasonic.front.m");
            var voltage = Parse(telemetry, "power.battery.voltage_v");
            var current = Parse(telemetry, "power.battery.current_a");
            var power = Parse(telemetry, "power.motor.estimated_w");
            var s1 = Parse(telemetry, "sensor.line_tracker.s1_norm");
            var s2 = Parse(telemetry, "sensor.line_tracker.s2_norm");
            var s3 = Parse(telemetry, "sensor.line_tracker.s3_norm");
            var s4 = Parse(telemetry, "sensor.line_tracker.s4_norm");
            var s5 = Parse(telemetry, "sensor.line_tracker.s5_norm");

            GUILayout.BeginArea(new Rect(16f, 16f, 480f, 180f), boxStyle);
            GUILayout.Label("KS0223 Sensor HUD", labelStyle);
            GUILayout.Label($"speedometer: {speed:0.00} m/s   ultrasonic: {ultrasonic:0.00} m", labelStyle);
            GUILayout.Label($"battery: {voltage:0.00} V   current: {current:0.00} A   motor power: {power:0.00} W", labelStyle);
            GUILayout.Label($"line tracker: [{Fmt(s1)}, {Fmt(s2)}, {Fmt(s3)}, {Fmt(s4)}, {Fmt(s5)}]", labelStyle);
            GUILayout.EndArea();
        }

        private static string Fmt(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        private static float Parse(Dictionary<string, string> telemetry, string key)
        {
            if (telemetry.TryGetValue(key, out var value) &&
                float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return 0f;
        }

        private static Dictionary<string, string> ToMap(ConfigKeyValue[] telemetry)
        {
            var map = new Dictionary<string, string>();
            if (telemetry == null)
            {
                return map;
            }

            foreach (var kv in telemetry)
            {
                if (kv == null || string.IsNullOrWhiteSpace(kv.key))
                {
                    continue;
                }

                map[kv.key] = kv.value ?? string.Empty;
            }

            return map;
        }
    }
}
