using System;
using System.Globalization;
using UavSimulator.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace UavSimulator.Vehicles
{
    public sealed class Ks0223Vehicle : VehicleBase
    {
        private const string LeftPwmKey = "drive.left_pwm_norm";
        private const string RightPwmKey = "drive.right_pwm_norm";
        private static readonly Vector2[] BasicArenaCenterline =
        {
            new Vector2(0f, -8f),
            new Vector2(0f, -1f),
            new Vector2(6f, -1f),
            new Vector2(6f, 5f),
        };
        private static readonly Vector2[] RoadSystemArenaCenterline =
        {
            new Vector2(-6f, -9f),
            new Vector2(-6f, -2.2f),
            new Vector2(0.2f, 2.8f),
            new Vector2(6.2f, -2f),
            new Vector2(6f, 7.2f),
        };
        private static readonly Vector2[] RoadSystemRealisticCenterline =
        {
            new Vector2(-11f, -14f),
            new Vector2(-11f, -4f),
            new Vector2(-6f, 4f),
            new Vector2(2f, 8f),
            new Vector2(10f, 2f),
            new Vector2(11f, -7f),
            new Vector2(4f, -13f),
        };

        [SerializeField] private float maxSpeedMps = 2.2f;
        [SerializeField] private float accelerationMps2 = 4.0f;
        [SerializeField] private float brakeDecelerationMps2 = 6.5f;
        [SerializeField] private float maxYawRateDegPerSec = 160f;
        [SerializeField] private Vector3 spawnPosition = new Vector3(0f, 0.2f, -6f);
        [SerializeField] private Vector3 spawnRotationEuler = Vector3.zero;
        [SerializeField] private float ultrasonicMaxDistanceM = 3.5f;
        [SerializeField] private float lineSensorForwardOffsetM = 0.19f;
        [SerializeField] private float lineSensorHalfSpanM = 0.08f;
        [SerializeField] private float lineSensorDetectionWidthM = 0.12f;
        [SerializeField] private int cameraImageWidth = 640;
        [SerializeField] private int cameraImageHeight = 480;
        [SerializeField] [Range(20, 100)] private int cameraJpegQuality = 100;
        [SerializeField] private Vector3 cameraLocalPosition = new Vector3(0f, 0.11f, 0.20f);
        [SerializeField] private Vector3 cameraLocalEuler = new Vector3(6f, 0f, 0f);

        private Rigidbody body;
        private Camera frontCamera;
        private RenderTexture frontCameraRt;
        private Texture2D frontCameraTexture;
        private float speedCmd;
        private float yawCmd;
        private float brakeCmd;
        private float leftPwmCmd;
        private float rightPwmCmd;
        private float currentSpeed;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (body == null)
            {
                body = gameObject.AddComponent<Rigidbody>();
            }

            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            body.linearDamping = 0.2f;
            body.angularDamping = 1.5f;
            body.mass = 1.0f;

            EnsureFrontCamera();
            if (transform.Find("VisualModel") == null)
            {
                EnsurePresentationVisuals();
                ApplyVisualPalette();
            }
        }

        public override void ApplyControl(ControlCommand command)
        {
            if (command == null)
            {
                speedCmd = 0f;
                yawCmd = 0f;
                brakeCmd = 1f;
                leftPwmCmd = 0f;
                rightPwmCmd = 0f;
                return;
            }

            if (TryGetExtension(command.extensions, LeftPwmKey, out var left) &&
                TryGetExtension(command.extensions, RightPwmKey, out var right))
            {
                leftPwmCmd = Mathf.Clamp(left, -1f, 1f);
                rightPwmCmd = Mathf.Clamp(right, -1f, 1f);

                speedCmd = Mathf.Clamp((leftPwmCmd + rightPwmCmd) * 0.5f, -1f, 1f);
                yawCmd = Mathf.Clamp((rightPwmCmd - leftPwmCmd) * 0.5f, -1f, 1f);
                brakeCmd = Mathf.Clamp01(command.brake);
                return;
            }

            speedCmd = Mathf.Clamp(command.throttle, -1f, 1f);
            yawCmd = Mathf.Clamp(command.steer, -1f, 1f);
            brakeCmd = Mathf.Clamp01(command.brake);
            leftPwmCmd = Mathf.Clamp(speedCmd - yawCmd, -1f, 1f);
            rightPwmCmd = Mathf.Clamp(speedCmd + yawCmd, -1f, 1f);
        }

        public override VehicleState ReadState()
        {
            var state = base.ReadState();

            var velocity = body != null ? body.linearVelocity : Vector3.zero;
            var angularVelocity = body != null ? body.angularVelocity : Vector3.zero;
            var lineValues = ReadLineSensors();
            var ultrasonicDistance = ReadUltrasonicDistance();
            var speedMps = new Vector2(velocity.x, velocity.z).magnitude;

            var pwmEffort = (Mathf.Abs(leftPwmCmd) + Mathf.Abs(rightPwmCmd)) * 0.5f;
            var powerLoad = Mathf.Clamp01((speedMps / Mathf.Max(maxSpeedMps, 0.01f)) * 0.75f + pwmEffort * 0.25f);
            var estimatedPowerW = Mathf.Lerp(2.4f, 21.5f, pwmEffort) * Mathf.Lerp(0.4f, 1f, powerLoad);
            var batteryVoltage = Mathf.Lerp(8.4f, 7.15f, pwmEffort * 0.8f);
            var batteryCurrent = estimatedPowerW / Mathf.Max(batteryVoltage, 0.1f);

            state.linearVelocity = new Vector3f { x = velocity.x, y = velocity.y, z = velocity.z };
            state.angularVelocity = new Vector3f { x = angularVelocity.x, y = angularVelocity.y, z = angularVelocity.z };
            state.speed = speedMps;
            state.telemetry = new[]
            {
                KV("drive.left_pwm_norm", leftPwmCmd),
                KV("drive.right_pwm_norm", rightPwmCmd),
                KV("sensor.speedometer.mps", speedMps),
                KV("sensor.ultrasonic.front.m", ultrasonicDistance),
                KV("sensor.line_tracker.s1_norm", lineValues[0]),
                KV("sensor.line_tracker.s2_norm", lineValues[1]),
                KV("sensor.line_tracker.s3_norm", lineValues[2]),
                KV("sensor.line_tracker.s4_norm", lineValues[3]),
                KV("sensor.line_tracker.s5_norm", lineValues[4]),
                KV("power.battery.voltage_v", batteryVoltage),
                KV("power.battery.current_a", batteryCurrent),
                KV("power.motor.estimated_w", estimatedPowerW),
            };

            return state;
        }

        public override bool TryReadCameraFrame(out CameraFrame frame)
        {
            frame = null;
            if (!IsCameraSensorReady())
            {
                return false;
            }

            var previousActiveRt = RenderTexture.active;
            var renderers = GetComponentsInChildren<Renderer>(includeInactive: false);
            var previousStates = new bool[renderers.Length];

            try
            {
                for (var i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    previousStates[i] = renderer.enabled;
                    renderer.enabled = false;
                }

                frontCamera.targetTexture = frontCameraRt;
                frontCamera.Render();

                RenderTexture.active = frontCameraRt;
                frontCameraTexture.ReadPixels(new Rect(0f, 0f, cameraImageWidth, cameraImageHeight), 0, 0, false);
                frontCameraTexture.Apply(false, false);

                var bytes = frontCameraTexture.EncodeToJPG(cameraJpegQuality);
                if (bytes == null || bytes.Length == 0)
                {
                    return false;
                }

                frame = new CameraFrame
                {
                    frameId = "camera/front/image_raw",
                    width = cameraImageWidth,
                    height = cameraImageHeight,
                    format = "jpeg",
                    encoding = "base64",
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    timeBase = "unix_ms",
                    bytesLength = bytes.Length,
                    dataRef = null,
                    dataBase64 = Convert.ToBase64String(bytes),
                };

                return true;
            }
            catch
            {
                frame = null;
                return false;
            }
            finally
            {
                for (var i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    renderer.enabled = previousStates[i];
                }

                RenderTexture.active = previousActiveRt;
                if (frontCamera != null)
                {
                    frontCamera.targetTexture = null;
                }
            }
        }

        public override void ResetVehicle(int seed)
        {
            transform.position = spawnPosition;
            transform.rotation = Quaternion.Euler(spawnRotationEuler);
            currentSpeed = 0f;
            speedCmd = 0f;
            yawCmd = 0f;
            brakeCmd = 0f;
            leftPwmCmd = 0f;
            rightPwmCmd = 0f;

            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private void OnDestroy()
        {
            if (frontCameraRt != null)
            {
                frontCameraRt.Release();
                Destroy(frontCameraRt);
                frontCameraRt = null;
            }

            if (frontCameraTexture != null)
            {
                Destroy(frontCameraTexture);
                frontCameraTexture = null;
            }
        }

        private void FixedUpdate()
        {
            var dt = Time.fixedDeltaTime;
            var targetSpeed = speedCmd * maxSpeedMps;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accelerationMps2 * dt);
            currentSpeed = Mathf.MoveTowards(currentSpeed, 0f, brakeCmd * brakeDecelerationMps2 * dt);

            var yawRateDeg = yawCmd * maxYawRateDegPerSec;
            var yawDelta = yawRateDeg * dt;
            var nextRotation = body.rotation * Quaternion.Euler(0f, yawDelta, 0f);
            var nextPosition = body.position + (nextRotation * Vector3.forward) * (currentSpeed * dt);

            body.MoveRotation(nextRotation);
            body.MovePosition(nextPosition);
        }

        private bool IsCameraSensorReady()
        {
            if (frontCamera == null || frontCameraRt == null || frontCameraTexture == null)
            {
                return false;
            }

            if (cameraImageWidth < 16 || cameraImageHeight < 16)
            {
                return false;
            }

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return false;
            }

            return true;
        }

        private void EnsureFrontCamera()
        {
            if (frontCamera != null)
            {
                return;
            }

            var camGo = new GameObject("FrontCameraSensor");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = cameraLocalPosition;
            camGo.transform.localRotation = Quaternion.Euler(cameraLocalEuler);

            frontCamera = camGo.AddComponent<Camera>();
            frontCamera.enabled = false;
            frontCamera.clearFlags = CameraClearFlags.SolidColor;
            frontCamera.backgroundColor = new Color(0.58f, 0.75f, 0.94f);
            frontCamera.nearClipPlane = 0.03f;
            frontCamera.farClipPlane = 40f;
            frontCamera.fieldOfView = 68f;
            frontCamera.allowHDR = false;
            frontCamera.allowMSAA = false;

            frontCameraRt = new RenderTexture(cameraImageWidth, cameraImageHeight, 16, RenderTextureFormat.ARGB32)
            {
                name = "KS0223.FrontCameraRT",
                antiAliasing = 1,
            };
            frontCameraRt.Create();

            frontCameraTexture = new Texture2D(cameraImageWidth, cameraImageHeight, TextureFormat.RGB24, false, false)
            {
                name = "KS0223.FrontCameraBuffer",
            };
        }

        private void EnsurePresentationVisuals()
        {
            EnsureVisualPart("Hood", PrimitiveType.Cube, new Vector3(0.30f, 0.06f, 0.18f), new Vector3(0f, 0.09f, 0.15f));
            EnsureVisualPart("Cabin", PrimitiveType.Cube, new Vector3(0.22f, 0.08f, 0.19f), new Vector3(0f, 0.13f, -0.03f));
            EnsureVisualPart("RearDeck", PrimitiveType.Cube, new Vector3(0.30f, 0.05f, 0.12f), new Vector3(0f, 0.09f, -0.19f));
            EnsureVisualPart("Windshield", PrimitiveType.Cube, new Vector3(0.20f, 0.05f, 0.03f), new Vector3(0f, 0.14f, 0.07f), new Vector3(-22f, 0f, 0f));
            EnsureVisualPart("RearWindow", PrimitiveType.Cube, new Vector3(0.20f, 0.05f, 0.03f), new Vector3(0f, 0.14f, -0.11f), new Vector3(22f, 0f, 0f));

            if (transform.Find("CameraPod") == null)
            {
                var cameraPod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cameraPod.name = "CameraPod";
                cameraPod.transform.SetParent(transform, false);
                cameraPod.transform.localScale = new Vector3(0.02f, 0.03f, 0.02f);
                cameraPod.transform.localPosition = new Vector3(0f, 0.20f, 0.20f);
                DisableCollider(cameraPod);
            }
        }

        private void EnsureVisualPart(
            string name,
            PrimitiveType primitiveType,
            Vector3 localScale,
            Vector3 localPosition,
            Vector3 localEuler = default)
        {
            if (transform.Find(name) != null)
            {
                return;
            }

            var part = GameObject.CreatePrimitive(primitiveType);
            part.name = name;
            part.transform.SetParent(transform, false);
            part.transform.localScale = localScale;
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.Euler(localEuler);
            DisableCollider(part);
        }

        private static void DisableCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.Destroy(collider);
            }
        }

        private void ApplyVisualPalette()
        {
            ApplyColor("Body", new Color(0.77f, 0.11f, 0.10f), 0.34f);
            ApplyColor("Hood", new Color(0.77f, 0.11f, 0.10f), 0.34f);
            ApplyColor("Cabin", new Color(0.10f, 0.10f, 0.11f), 0.28f);
            ApplyColor("RearDeck", new Color(0.77f, 0.11f, 0.10f), 0.33f);
            ApplyColor("Windshield", new Color(0.23f, 0.32f, 0.38f), 0.7f);
            ApplyColor("RearWindow", new Color(0.21f, 0.29f, 0.35f), 0.68f);
            ApplyColor("CameraPod", new Color(0.82f, 0.82f, 0.85f), 0.2f);
            ApplyColor("WheelFL", new Color(0.08f, 0.08f, 0.08f), 0.52f);
            ApplyColor("WheelFR", new Color(0.08f, 0.08f, 0.08f), 0.52f);
            ApplyColor("WheelRL", new Color(0.08f, 0.08f, 0.08f), 0.52f);
            ApplyColor("WheelRR", new Color(0.08f, 0.08f, 0.08f), 0.52f);
        }

        private void ApplyColor(string objectName, Color color, float smoothness)
        {
            var child = transform.Find(objectName);
            if (child == null)
            {
                return;
            }

            var renderer = child.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            var shader = ResolveRuntimeLitShader();
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }

            renderer.sharedMaterial = material;
        }

        private static Shader ResolveRuntimeLitShader()
        {
            var shader = Shader.Find("Unlit/Color");
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            shader = Shader.Find("Unlit/Texture");
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            shader = Shader.Find("Standard");
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            shader = Shader.Find("Legacy Shaders/Diffuse");
            if (shader != null)
            {
                return shader;
            }

            throw new MissingReferenceException("Unable to resolve runtime shader for KS0223 visuals.");
        }

        private float[] ReadLineSensors()
        {
            var values = new float[5];
            for (var i = 0; i < values.Length; i++)
            {
                var t = i / 4f;
                var localX = Mathf.Lerp(-lineSensorHalfSpanM, lineSensorHalfSpanM, t);
                var sampleWorld = transform.TransformPoint(new Vector3(localX, 0f, lineSensorForwardOffsetM));
                var distance = DistanceToRoadCenterline(new Vector2(sampleWorld.x, sampleWorld.z));
                values[i] = Mathf.Clamp01(1f - (distance / Mathf.Max(0.01f, lineSensorDetectionWidthM)));
            }

            return values;
        }

        private float ReadUltrasonicDistance()
        {
            var origin = transform.TransformPoint(new Vector3(0f, 0.07f, 0.23f));
            var direction = transform.forward;
            var hits = Physics.RaycastAll(origin, direction, ultrasonicMaxDistanceM, ~0, QueryTriggerInteraction.Ignore);

            var nearest = ultrasonicMaxDistanceM;
            var hasHit = false;

            foreach (var hit in hits)
            {
                if (hit.transform == null || hit.distance <= 0f)
                {
                    continue;
                }

                if (hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    hasHit = true;
                }
            }

            return hasHit ? nearest : ultrasonicMaxDistanceM;
        }

        private static float DistanceToRoadCenterline(Vector2 p)
        {
            var distanceBasicArena = DistanceToPolyline(p, BasicArenaCenterline);
            var distanceRoadSystem = DistanceToPolyline(p, RoadSystemArenaCenterline);
            var distanceRoadSystemRealistic = DistanceToPolyline(p, RoadSystemRealisticCenterline);
            return Mathf.Min(distanceBasicArena, Mathf.Min(distanceRoadSystem, distanceRoadSystemRealistic));
        }

        private static float DistanceToPolyline(Vector2 p, Vector2[] points)
        {
            if (points == null || points.Length < 2)
            {
                return float.MaxValue;
            }

            var best = float.MaxValue;
            for (var i = 0; i < points.Length - 1; i++)
            {
                var distance = DistanceToSegment(p, points[i], points[i + 1]);
                if (distance < best)
                {
                    best = distance;
                }
            }

            return best;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var abLen2 = ab.sqrMagnitude;
            if (abLen2 < 1e-5f)
            {
                return Vector2.Distance(p, a);
            }

            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLen2);
            var closest = a + ab * t;
            return Vector2.Distance(p, closest);
        }

        private static bool TryGetExtension(ConfigKeyValue[] extensions, string key, out float value)
        {
            value = 0f;
            if (extensions == null || extensions.Length == 0 || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            foreach (var kv in extensions)
            {
                if (kv == null || !string.Equals(kv.key, key, StringComparison.Ordinal))
                {
                    continue;
                }

                if (float.TryParse(kv.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    value = parsed;
                    return true;
                }

                return false;
            }

            return false;
        }

        private static ConfigKeyValue KV(string key, float value) => new ConfigKeyValue
        {
            key = key,
            value = value.ToString("0.###", CultureInfo.InvariantCulture),
        };
    }
}
