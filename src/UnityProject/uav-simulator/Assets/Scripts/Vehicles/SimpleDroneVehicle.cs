using System;
using System.Globalization;
using UavSimulator.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace UavSimulator.Vehicles
{
    public sealed class SimpleDroneVehicle : VehicleBase
    {
        private const string ThrustKey = "drone.thrust_norm";
        private const string PitchKey = "drone.pitch_norm";
        private const string YawKey = "drone.yaw_norm";

        [SerializeField] private float maxForwardSpeedMps = 4.0f;
        [SerializeField] private float maxVerticalSpeedMps = 2.5f;
        [SerializeField] private float maxYawRateDegPerSec = 110f;
        [SerializeField] private float forwardAccelerationMps2 = 4.2f;
        [SerializeField] private float verticalAccelerationMps2 = 3.2f;
        [SerializeField] private Vector3 spawnPosition = new Vector3(0f, 1.3f, -6f);
        [SerializeField] private Vector3 spawnRotationEuler = Vector3.zero;
        [SerializeField] private float minimumAltitudeM = 0.35f;
        [SerializeField] private int cameraImageWidth = 160;
        [SerializeField] private int cameraImageHeight = 120;
        [SerializeField] [Range(20, 95)] private int cameraJpegQuality = 65;
        [SerializeField] private Vector3 cameraLocalPosition = new Vector3(0f, 0.03f, 0.15f);
        [SerializeField] private Vector3 cameraLocalEuler = new Vector3(10f, 0f, 0f);

        private Rigidbody body;
        private Camera frontCamera;
        private RenderTexture frontCameraRt;
        private Texture2D frontCameraTexture;
        private int defaultCameraCullingMask = ~0;
        private string cameraMode = "driver";

        private float pitchCmd;
        private float yawCmd;
        private float thrustCmd;
        private float forwardSpeed;
        private float verticalSpeed;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (body == null)
            {
                body = gameObject.AddComponent<Rigidbody>();
            }

            body.useGravity = false;
            body.mass = 1.2f;
            body.linearDamping = 1.0f;
            body.angularDamping = 2.0f;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            EnsureFrontCamera();
            if (transform.Find("VisualModel") == null)
            {
                EnsureFallbackVisuals();
                ApplyVisualPalette();
            }
        }

        public override void ApplyControl(ControlCommand command)
        {
            if (command == null)
            {
                pitchCmd = 0f;
                yawCmd = 0f;
                thrustCmd = 0f;
                return;
            }

            pitchCmd = Mathf.Clamp(command.throttle, -1f, 1f);
            yawCmd = Mathf.Clamp(command.steer, -1f, 1f);
            thrustCmd = Mathf.Clamp(command.brake * 2f - 1f, -1f, 1f);

            if (TryGetExtension(command.extensions, PitchKey, out var pitchValue))
            {
                pitchCmd = Mathf.Clamp(pitchValue, -1f, 1f);
            }

            if (TryGetExtension(command.extensions, YawKey, out var yawValue))
            {
                yawCmd = Mathf.Clamp(yawValue, -1f, 1f);
            }

            if (TryGetExtension(command.extensions, ThrustKey, out var thrustValue))
            {
                thrustCmd = Mathf.Clamp(thrustValue, -1f, 1f);
            }
        }

        public override VehicleState ReadState()
        {
            var state = base.ReadState();
            var velocity = body != null ? body.linearVelocity : Vector3.zero;
            var angularVelocity = body != null ? body.angularVelocity : Vector3.zero;
            var speedMps = new Vector2(velocity.x, velocity.z).magnitude;
            var altitudeM = transform.position.y;
            var estimatedPower = Mathf.Lerp(4f, 38f, Mathf.Clamp01(Mathf.Abs(thrustCmd) * 0.65f + Mathf.Abs(pitchCmd) * 0.35f));
            var batteryVoltage = Mathf.Lerp(12.6f, 10.9f, Mathf.Clamp01(Mathf.Abs(thrustCmd) * 0.8f));

            state.linearVelocity = new Vector3f { x = velocity.x, y = velocity.y, z = velocity.z };
            state.angularVelocity = new Vector3f { x = angularVelocity.x, y = angularVelocity.y, z = angularVelocity.z };
            state.speed = speedMps;
            state.telemetry = new[]
            {
                KV("sensor.altimeter.m", altitudeM),
                KV("sensor.airspeed.mps", speedMps),
                KV("sensor.vertical_speed.mps", velocity.y),
                KV("power.battery.voltage_v", batteryVoltage),
                KV("power.motor.estimated_w", estimatedPower),
                KV("drone.cmd.pitch_norm", pitchCmd),
                KV("drone.cmd.yaw_norm", yawCmd),
                KV("drone.cmd.thrust_norm", thrustCmd),
            };

            state.telemetry = MergeTelemetry(state.telemetry);
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
            try
            {
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
                RenderTexture.active = previousActiveRt;
                if (frontCamera != null)
                {
                    frontCamera.targetTexture = null;
                }
            }
        }

        public override bool TryReadCameraFrame(string captureMode, out CameraFrame frame)
        {
            var normalizedMode = NormalizeCameraMode(captureMode);
            if (string.Equals(normalizedMode, cameraMode, StringComparison.Ordinal))
            {
                return TryReadCameraFrame(out frame);
            }

            var previousMode = cameraMode;
            try
            {
                cameraMode = normalizedMode;
                ApplyCameraMode();
                return TryReadCameraFrame(out frame);
            }
            finally
            {
                cameraMode = previousMode;
                ApplyCameraMode();
            }
        }

        public override void ResetVehicle(int seed)
        {
            _ = seed;
            transform.position = spawnPosition;
            transform.rotation = Quaternion.Euler(spawnRotationEuler);
            pitchCmd = 0f;
            yawCmd = 0f;
            thrustCmd = 0f;
            forwardSpeed = 0f;
            verticalSpeed = 0f;

            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        public override void ApplyVehicleConfig(ConfigKeyValue[] vehicleParams)
        {
            if (TryGetConfigValue(vehicleParams, "camera.mode", out var mode))
            {
                cameraMode = NormalizeCameraMode(mode);
                ApplyCameraMode();
            }
        }

        public override void SetPeerVisibility(bool visible)
        {
            if (frontCamera == null)
            {
                return;
            }

            frontCamera.cullingMask = visible
                ? defaultCameraCullingMask
                : defaultCameraCullingMask & ~(1 << PeerVehicleLayer);
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
            var targetForwardSpeed = pitchCmd * maxForwardSpeedMps;
            var targetVerticalSpeed = thrustCmd * maxVerticalSpeedMps;

            forwardSpeed = Mathf.MoveTowards(forwardSpeed, targetForwardSpeed, forwardAccelerationMps2 * dt);
            verticalSpeed = Mathf.MoveTowards(verticalSpeed, targetVerticalSpeed, verticalAccelerationMps2 * dt);

            var yawDelta = yawCmd * maxYawRateDegPerSec * dt;
            var nextRotation = body.rotation * Quaternion.Euler(0f, yawDelta, 0f);
            var horizontalMove = nextRotation * Vector3.forward * (forwardSpeed * dt);
            var verticalMove = Vector3.up * (verticalSpeed * dt);
            var nextPosition = body.position + horizontalMove + verticalMove;
            if (nextPosition.y < minimumAltitudeM)
            {
                nextPosition.y = minimumAltitudeM;
                verticalSpeed = Mathf.Max(0f, verticalSpeed);
            }

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
            frontCamera.farClipPlane = 50f;
            frontCamera.fieldOfView = 78f;
            frontCamera.allowHDR = false;
            frontCamera.allowMSAA = false;
            defaultCameraCullingMask = frontCamera.cullingMask;
            ApplyCameraMode();

            frontCameraRt = new RenderTexture(cameraImageWidth, cameraImageHeight, 16, RenderTextureFormat.ARGB32)
            {
                name = "SimpleDrone.FrontCameraRT",
                antiAliasing = 1,
            };
            frontCameraRt.Create();

            frontCameraTexture = new Texture2D(cameraImageWidth, cameraImageHeight, TextureFormat.RGB24, false, false)
            {
                name = "SimpleDrone.FrontCameraBuffer",
            };
        }

        private void ApplyCameraMode()
        {
            if (frontCamera == null)
            {
                return;
            }

            Vector3 localPosition;
            Vector3 localEuler;
            float fieldOfView;

            switch (cameraMode)
            {
                case "chase":
                    localPosition = new Vector3(0f, 0.55f, -1.45f);
                    localEuler = new Vector3(18f, 0f, 0f);
                    fieldOfView = 78f;
                    break;
                case "spectator":
                    localPosition = new Vector3(1.1f, 0.85f, -1.65f);
                    localEuler = new Vector3(20f, -20f, 0f);
                    fieldOfView = 72f;
                    break;
                case "bumper":
                    localPosition = new Vector3(0f, 0.04f, 0.22f);
                    localEuler = new Vector3(8f, 0f, 0f);
                    fieldOfView = 84f;
                    break;
                default:
                    localPosition = cameraLocalPosition;
                    localEuler = cameraLocalEuler;
                    fieldOfView = 78f;
                    break;
            }

            frontCamera.transform.localPosition = localPosition;
            frontCamera.transform.localRotation = Quaternion.Euler(localEuler);
            frontCamera.fieldOfView = fieldOfView;
        }

        private void EnsureFallbackVisuals()
        {
            var bodyPart = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bodyPart.name = "Body";
            bodyPart.transform.SetParent(transform, false);
            bodyPart.transform.localScale = new Vector3(0.22f, 0.10f, 0.22f);
            bodyPart.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            DisableCollider(bodyPart);

            CreateArm("ArmX", new Vector3(0f, 0.06f, 0f), Vector3.zero);
            CreateArm("ArmZ", new Vector3(0f, 0.06f, 0f), new Vector3(0f, 45f, 0f));

            CreateRotor("RotorFL", new Vector3(-0.17f, 0.08f, 0.17f));
            CreateRotor("RotorFR", new Vector3(0.17f, 0.08f, 0.17f));
            CreateRotor("RotorRL", new Vector3(-0.17f, 0.08f, -0.17f));
            CreateRotor("RotorRR", new Vector3(0.17f, 0.08f, -0.17f));
        }

        private void CreateArm(string name, Vector3 localPosition, Vector3 localEuler)
        {
            var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = name;
            arm.transform.SetParent(transform, false);
            arm.transform.localPosition = localPosition;
            arm.transform.localRotation = Quaternion.Euler(localEuler);
            arm.transform.localScale = new Vector3(0.42f, 0.025f, 0.06f);
            DisableCollider(arm);
        }

        private void CreateRotor(string name, Vector3 localPosition)
        {
            var rotor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rotor.name = name;
            rotor.transform.SetParent(transform, false);
            rotor.transform.localPosition = localPosition;
            rotor.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            rotor.transform.localScale = new Vector3(0.055f, 0.005f, 0.055f);
            DisableCollider(rotor);
        }

        private void ApplyVisualPalette()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                var color = renderer.gameObject.name switch
                {
                    "Body" => new Color(0.16f, 0.16f, 0.17f),
                    "ArmX" => new Color(0.20f, 0.20f, 0.22f),
                    "ArmZ" => new Color(0.20f, 0.20f, 0.22f),
                    "RotorFL" => new Color(0.08f, 0.08f, 0.08f),
                    "RotorFR" => new Color(0.08f, 0.08f, 0.08f),
                    "RotorRL" => new Color(0.08f, 0.08f, 0.08f),
                    "RotorRR" => new Color(0.08f, 0.08f, 0.08f),
                    _ => new Color(0.75f, 0.75f, 0.77f),
                };

                material.color = color;
                material.SetFloat("_Smoothness", 0.28f);
                renderer.sharedMaterial = material;
            }
        }

        private static bool TryGetExtension(ConfigKeyValue[] extensions, string key, out float value)
        {
            value = 0f;
            if (extensions == null)
            {
                return false;
            }

            for (var i = 0; i < extensions.Length; i++)
            {
                var kv = extensions[i];
                if (!string.Equals(kv.key, key, StringComparison.Ordinal))
                {
                    continue;
                }

                if (float.TryParse(kv.value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }

                return false;
            }

            return false;
        }

        private static bool TryGetConfigValue(ConfigKeyValue[] items, string key, out string value)
        {
            value = string.Empty;
            if (items == null)
            {
                return false;
            }

            for (var i = 0; i < items.Length; i++)
            {
                var kv = items[i];
                if (!string.Equals(kv.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = kv.value ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }

            return false;
        }

        private static string NormalizeCameraMode(string value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized switch
            {
                "bumper" => "bumper",
                "chase" => "chase",
                "spectator" => "spectator",
                _ => "driver",
            };
        }

        private static ConfigKeyValue KV(string key, float value)
        {
            return new ConfigKeyValue
            {
                key = key,
                value = value.ToString("0.###", CultureInfo.InvariantCulture),
            };
        }

        private static void DisableCollider(GameObject gameObject)
        {
            var collider = gameObject.GetComponent<Collider>();
            if (collider == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(collider);
            }
            else
            {
                DestroyImmediate(collider);
            }
        }
    }
}
