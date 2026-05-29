using System;
using System.Globalization;
using UavSimulator.Contracts;
using UavSimulator.Core;
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

        // Calibrated against real Keyestudio KS0223 (4x 4.5V 200 RPM motors,
        // ~65 mm wheels, ~150 mm wheelbase).
        //
        // Linear: ruler-measured 0.73 m/s steady state (Apr 25 calibration).
        // Yaw: 3-point calibration Apr 26
        //   90deg burst (238ms) -> 50deg actual = 210 deg/s avg (spinup)
        //   180deg burst (466ms) -> 180deg actual = 386 deg/s avg
        //   360deg burst (927ms) -> 350deg actual = 378 deg/s avg
        // Steady state ~380 deg/s. Spinup ~200ms gives angular accel ~1900.
        [SerializeField] private float maxSpeedMps = 0.73f;
        [SerializeField] private float accelerationMps2 = 4.0f;
        [SerializeField] private float brakeDecelerationMps2 = 6.5f;
        // Yaw tuning — original 380°/s + 1900°/s² felt jerky to human operators
        // (sub-second full rotation). Halved values give smoother manual driving
        // for BC recording while still matching the open-loop characteristic of
        // the real KS0223 (~90-180°/s effective per DirLeft/DirRight tick).
        [SerializeField] private float maxYawRateDegPerSec = 180f;
        [SerializeField] private float yawAccelerationDegPerSec2 = 700f;
        [SerializeField] private Vector3 spawnPosition = new Vector3(0f, 0.2f, -6f);
        [SerializeField] private Vector3 spawnRotationEuler = Vector3.zero;
        [SerializeField] private float ultrasonicMaxDistanceM = 3.5f;
        [SerializeField] private float lineSensorForwardOffsetM = 0.19f;
        [SerializeField] private float lineSensorHalfSpanM = 0.08f;
        [SerializeField] private float lineSensorDetectionWidthM = 0.12f;
        [SerializeField] private int cameraImageWidth = 1280;
        [SerializeField] private int cameraImageHeight = 720;
        [SerializeField] [Range(20, 100)] private int cameraJpegQuality = 95;
        [SerializeField] [Range(1, 8)] private int cameraMsaaSamples = 1;
        [SerializeField] [Range(0, 16)] private int cameraAnisoLevel = 8;
        [SerializeField] private bool cameraAllowHdr = true;
        [SerializeField] private Vector3 cameraLocalPosition = new Vector3(0f, 0.09f, 0.12f);
        [SerializeField] private Vector3 cameraLocalEuler = new Vector3(6f, 0f, 0f);
        [SerializeField] private Color presentationAccentColor = new Color(0.77f, 0.11f, 0.10f);

        // Plan 4 (rev39): per-episode dynamics randomization for sim2real generalization.
        // master-plan sim audit #4: real KS0223 mass varies 0.8-1.2 kg with battery,
        // motor strength asymmetric per-wheel by ±10%. Camera pitch jitter (audit #3):
        // declared in CardboardCorridorTrack but never wired up — applied here.
        // All toggle-able: set jitter ranges to 0 to disable.
        [SerializeField] private bool randomizeDynamics = true;
        [SerializeField] private float massJitterPct = 0.20f;       // ±20% around 1.0kg
        [SerializeField] private float dampingJitterPct = 0.20f;    // ±20% around defaults
        [SerializeField] private float motorAsymmetryPct = 0.10f;   // ±10% per-wheel
        [SerializeField] private float cameraPitchJitterDeg = 4f;   // ±4° around base pitch

        private Rigidbody body;
        private Camera frontCamera;
        private RenderTexture frontCameraRt;
        private Texture2D frontCameraTexture;
        private int defaultCameraCullingMask = ~0;
        private string cameraMode = "driver";
        private float speedCmd;
        private float yawCmd;
        private float brakeCmd;
        private float leftPwmCmd;
        private float rightPwmCmd;
        private float currentSpeed;
        private float currentYawRateDeg;
        // Plan 4: per-episode motor asymmetry — multiplies leftPwm/rightPwm
        // independently so DirForward (left=right=1) gives slight yaw bias.
        // Real KS0223 motors are not perfectly matched.
        private float leftMotorMult = 1f;
        private float rightMotorMult = 1f;
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

            // KNOWN ISSUE: KS0223 has no body collider, so when the rigidbody
            // pushes against a wall the camera mast (at z=0.12) physically
            // penetrates the wall mesh and renders skybox/floor through it. We
            // tried adding a BoxCollider here but it stalls the velocity-driven
            // motion (any contact with floor mesh applies friction that opposes
            // body.linearVelocity even when set directly each FixedUpdate; the
            // wheels are visual-only with no PhysicMaterial). For now, demo
            // recorders should avoid contact; PPO/BC training uses position-based
            // OOB termination so the policy never relies on wall-contact frames.

            EnsureFrontCamera();
            if (!HasImportedVisualModel())
            {
                EnsurePresentationVisuals();
                ApplyVisualPalette();
            }
            else
            {
                RemovePresentationVisuals();
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

                // Plan 4: motor asymmetry applies AFTER PWM derivation.
                var effLeft = leftPwmCmd * leftMotorMult;
                var effRight = rightPwmCmd * rightMotorMult;
                speedCmd = Mathf.Clamp((effLeft + effRight) * 0.5f, -1f, 1f);
                yawCmd = Mathf.Clamp((effRight - effLeft) * 0.5f, -1f, 1f);
                brakeCmd = Mathf.Clamp01(command.brake);
                return;
            }

            speedCmd = Mathf.Clamp(command.throttle, -1f, 1f);
            yawCmd = Mathf.Clamp(command.steer, -1f, 1f);
            brakeCmd = Mathf.Clamp01(command.brake);
            leftPwmCmd = Mathf.Clamp(speedCmd - yawCmd, -1f, 1f);
            rightPwmCmd = Mathf.Clamp(speedCmd + yawCmd, -1f, 1f);
            // Plan 4: re-derive speed/yaw with motor asymmetry applied. For
            // throttle/steer command path, this means DirForward (throttle=1,
            // steer=0) -> leftPwm=rightPwm=1 -> with asymmetry, effective
            // yaw becomes non-zero (real-robot KS0223 DirRight bias, master
            // plan sim2real audit).
            var effLeft2 = leftPwmCmd * leftMotorMult;
            var effRight2 = rightPwmCmd * rightMotorMult;
            speedCmd = Mathf.Clamp((effLeft2 + effRight2) * 0.5f, -1f, 1f);
            yawCmd = Mathf.Clamp((effRight2 - effLeft2) * 0.5f, -1f, 1f);
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
            var renderers = GetComponentsInChildren<Renderer>(includeInactive: false);
            var previousStates = new bool[renderers.Length];

            try
            {
                var hideSelfGeometry = cameraMode is "bumper" or "chase" or "spectator";
                for (var i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    previousStates[i] = renderer.enabled;
                    if (hideSelfGeometry)
                    {
                        renderer.enabled = false;
                    }
                }

                if (cameraMode == "top_down")
                {
                    PositionTopDownOverTrack();
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
            transform.position = spawnPosition;
            transform.rotation = Quaternion.Euler(spawnRotationEuler);
            currentSpeed = 0f;
            currentYawRateDeg = 0f;
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

            // Plan 4 (rev39): per-episode dynamics + motor asymmetry + camera
            // pitch jitter. Sample from per-episode seed so results are
            // reproducible. Defaults to 1.0/0/etc when randomizeDynamics off.
            if (randomizeDynamics && body != null)
            {
                var rng = new System.Random(seed);
                if (massJitterPct > 0f)
                {
                    var massScale = 1f + ((float)rng.NextDouble() - 0.5f) * 2f * massJitterPct;
                    body.mass = 1.0f * massScale;
                }
                if (dampingJitterPct > 0f)
                {
                    var lDampScale = 1f + ((float)rng.NextDouble() - 0.5f) * 2f * dampingJitterPct;
                    var aDampScale = 1f + ((float)rng.NextDouble() - 0.5f) * 2f * dampingJitterPct;
                    body.linearDamping = 0.2f * lDampScale;
                    body.angularDamping = 1.5f * aDampScale;
                }
                if (motorAsymmetryPct > 0f)
                {
                    leftMotorMult = 1f + ((float)rng.NextDouble() - 0.5f) * 2f * motorAsymmetryPct;
                    rightMotorMult = 1f + ((float)rng.NextDouble() - 0.5f) * 2f * motorAsymmetryPct;
                }
                else
                {
                    leftMotorMult = 1f;
                    rightMotorMult = 1f;
                }
                // Pitch jitter is a sim-to-real domain-randomisation knob that only
                // makes sense for the driver-mode camera. Top-down / chase / spectator
                // modes have their own deliberate poses set by ApplyCameraMode — we
                // must not stomp them with the driver base euler here.
                if (cameraPitchJitterDeg > 0f && frontCamera != null && cameraMode == "driver")
                {
                    var pitchOffset = ((float)rng.NextDouble() - 0.5f) * 2f * cameraPitchJitterDeg;
                    var jittered = new Vector3(cameraLocalEuler.x + pitchOffset,
                                               cameraLocalEuler.y, cameraLocalEuler.z);
                    frontCamera.transform.localRotation = Quaternion.Euler(jittered);
                }
            }
        }

        public override void ApplyVehicleConfig(ConfigKeyValue[] vehicleParams)
        {
            if (TryGetConfigValue(vehicleParams, "camera.profile", out var profile))
            {
                ApplyCameraProfile(profile);
            }

            var shouldRecreateTargets = false;
            if (TryGetConfigInt(vehicleParams, "camera.width", out var width))
            {
                cameraImageWidth = Mathf.Clamp(width, 320, 1920);
                shouldRecreateTargets = true;
            }

            if (TryGetConfigInt(vehicleParams, "camera.height", out var height))
            {
                cameraImageHeight = Mathf.Clamp(height, 240, 1080);
                shouldRecreateTargets = true;
            }

            if (TryGetConfigInt(vehicleParams, "camera.jpeg_quality", out var quality))
            {
                cameraJpegQuality = Mathf.Clamp(quality, 20, 100);
            }

            if (TryGetConfigInt(vehicleParams, "camera.msaa", out var msaa))
            {
                cameraMsaaSamples = NormalizeMsaaSamples(msaa);
                shouldRecreateTargets = true;
            }

            if (TryGetConfigInt(vehicleParams, "camera.aniso", out var aniso))
            {
                cameraAnisoLevel = Mathf.Clamp(aniso, 0, 16);
                shouldRecreateTargets = true;
            }

            if (TryGetConfigBool(vehicleParams, "camera.hdr", out var hdr))
            {
                cameraAllowHdr = hdr;
                shouldRecreateTargets = true;
            }

            if (TryGetConfigValue(vehicleParams, "camera.mode", out var mode))
            {
                cameraMode = NormalizeCameraMode(mode);
            }

            if (shouldRecreateTargets)
            {
                RecreateCameraTargets();
            }

            ApplyCameraMode();
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

        public void SetPresentationAccentColor(Color color)
        {
            presentationAccentColor = color;
            if (!HasImportedVisualModel())
            {
                ApplyVisualPalette();
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

            // Angular speed has spinup dynamics (real KS0223: ~150-200 ms ramp).
            // Apply same MoveTowards pattern as linear speed for realism.
            var targetYawRateDeg = yawCmd * maxYawRateDegPerSec;
            currentYawRateDeg = Mathf.MoveTowards(
                currentYawRateDeg, targetYawRateDeg, yawAccelerationDegPerSec2 * dt);
            var yawDelta = currentYawRateDeg * dt;
            var nextRotation = body.rotation * Quaternion.Euler(0f, yawDelta, 0f);

            // Use velocity-based movement so Unity physics detects wall collisions.
            // MovePosition() is kinematic and passes through colliders.
            var forward = nextRotation * Vector3.forward;
            body.linearVelocity = forward * currentSpeed + new Vector3(0f, body.linearVelocity.y, 0f);
            body.MoveRotation(nextRotation);
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
            frontCamera.farClipPlane = 120f;
            frontCamera.fieldOfView = 68f;
            frontCamera.allowHDR = cameraAllowHdr;
            frontCamera.allowMSAA = cameraMsaaSamples > 1;
            frontCamera.useOcclusionCulling = false;
            defaultCameraCullingMask = frontCamera.cullingMask;
            ApplyCameraMode();
            RecreateCameraTargets();
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
                case "bumper":
                    localPosition = new Vector3(0f, 0.05f, 0.24f);
                    localEuler = new Vector3(6f, 0f, 0f);
                    fieldOfView = 76f;
                    break;
                case "chase":
                    localPosition = new Vector3(0f, 0.62f, -1.28f);
                    localEuler = new Vector3(18f, 0f, 0f);
                    fieldOfView = 72f;
                    break;
                case "spectator":
                    localPosition = new Vector3(1.25f, 1.0f, -1.65f);
                    localEuler = new Vector3(20f, -20f, 0f);
                    fieldOfView = 68f;
                    break;
                case "top_down":
                    localPosition = new Vector3(0f, 9f, 0f);
                    localEuler = new Vector3(90f, 0f, 0f);
                    fieldOfView = 90f;
                    break;
                default:
                    localPosition = cameraLocalPosition;
                    localEuler = cameraLocalEuler;
                    fieldOfView = 68f;
                    break;
            }

            frontCamera.transform.localPosition = localPosition;
            frontCamera.transform.localRotation = Quaternion.Euler(localEuler);
            frontCamera.fieldOfView = fieldOfView;
        }

        /// <summary>
        /// Re-positions the front camera in world space to frame the entire active
        /// track from directly above. Called every render tick when cameraMode ==
        /// "top_down" so the view doesn't follow the robot — operators see the whole
        /// maze + the green dot wherever the agent currently is.
        /// </summary>
        private void PositionTopDownOverTrack()
        {
            var track = FindFirstObjectByType<UavSimulator.Tracks.TrackBase>();
            if (track == null) return;
            var renderers = track.GetComponentsInChildren<Renderer>(includeInactive: false);
            if (renderers == null || renderers.Length == 0) return;

            bool hasBounds = false;
            Bounds combined = default;
            foreach (var r in renderers)
            {
                if (r == null || r.gameObject == null) continue;
                // Filter out the surrounding gray floor + ambient lights — they would
                // inflate bounds and shrink the maze itself in the framed view.
                var name = r.gameObject.name;
                if (name.StartsWith("SurroundFloor") || name.StartsWith("TrackLight")) continue;
                if (!hasBounds) { combined = r.bounds; hasBounds = true; }
                else combined.Encapsulate(r.bounds);
            }
            if (!hasBounds) return;

            var center = new Vector3(combined.center.x, 0f, combined.center.z);
            var maxExtent = Mathf.Max(combined.size.x, combined.size.z);
            // Height required to frame maxExtent at the current camera FOV (vertical).
            var fovRad = frontCamera.fieldOfView * Mathf.Deg2Rad;
            var paddingRatio = 0.25f;
            var visibleSpan = maxExtent * (1f + paddingRatio);
            var height = Mathf.Max(2f, (visibleSpan * 0.5f) / Mathf.Tan(fovRad * 0.5f));

            frontCamera.transform.position = new Vector3(center.x, height, center.z);
            frontCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private void ApplyCameraProfile(string profileRaw)
        {
            var profile = string.IsNullOrWhiteSpace(profileRaw) ? "high" : profileRaw.Trim().ToLowerInvariant();
            switch (profile)
            {
                case "performance":
                    cameraImageWidth = 640;
                    cameraImageHeight = 360;
                    cameraJpegQuality = 72;
                    cameraMsaaSamples = 1;
                    cameraAnisoLevel = 2;
                    cameraAllowHdr = false;
                    break;
                case "balanced":
                    cameraImageWidth = 960;
                    cameraImageHeight = 540;
                    cameraJpegQuality = 84;
                    cameraMsaaSamples = 1;
                    cameraAnisoLevel = 4;
                    cameraAllowHdr = false;
                    break;
                case "ultra":
                    cameraImageWidth = 1600;
                    cameraImageHeight = 900;
                    cameraJpegQuality = 96;
                    cameraMsaaSamples = 1;
                    cameraAnisoLevel = 12;
                    cameraAllowHdr = true;
                    break;
                default:
                    cameraImageWidth = 1280;
                    cameraImageHeight = 720;
                    cameraJpegQuality = 92;
                    cameraMsaaSamples = 1;
                    cameraAnisoLevel = 8;
                    cameraAllowHdr = true;
                    break;
            }

            cameraMsaaSamples = NormalizeMsaaSamples(cameraMsaaSamples);
            RecreateCameraTargets();
        }

        private void RecreateCameraTargets()
        {
            if (frontCamera == null)
            {
                return;
            }

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

            var msaa = NormalizeMsaaSamples(cameraMsaaSamples);
            frontCamera.allowHDR = cameraAllowHdr;
            frontCamera.allowMSAA = msaa > 1;
            frontCameraRt = new RenderTexture(cameraImageWidth, cameraImageHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "KS0223.FrontCameraRT",
                antiAliasing = msaa,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                anisoLevel = cameraAnisoLevel,
            };
            frontCameraRt.Create();
            frontCamera.targetTexture = frontCameraRt;

            frontCameraTexture = new Texture2D(cameraImageWidth, cameraImageHeight, TextureFormat.RGB24, false, false)
            {
                name = "KS0223.FrontCameraBuffer",
                filterMode = FilterMode.Bilinear,
                anisoLevel = cameraAnisoLevel,
            };
        }

        private static int NormalizeMsaaSamples(int requested)
        {
            _ = requested;
            // Unity 6 + URP standalone offscreen capture is stable only with single-sample RTs here.
            return 1;
        }

        private static bool TryGetConfigInt(ConfigKeyValue[] values, string key, out int parsed)
        {
            parsed = 0;
            if (values == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            for (var i = 0; i < values.Length; i++)
            {
                var item = values[i];
                if (item == null || !string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return int.TryParse(item.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
            }

            return false;
        }

        private static bool TryGetConfigBool(ConfigKeyValue[] values, string key, out bool parsed)
        {
            parsed = false;
            if (values == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            for (var i = 0; i < values.Length; i++)
            {
                var item = values[i];
                if (item == null || !string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (bool.TryParse(item.value, out parsed))
                {
                    return true;
                }

                if (string.Equals(item.value?.Trim(), "1", StringComparison.Ordinal))
                {
                    parsed = true;
                    return true;
                }

                if (string.Equals(item.value?.Trim(), "0", StringComparison.Ordinal))
                {
                    parsed = false;
                    return true;
                }
            }

            return false;
        }

        private void EnsurePresentationVisuals()
        {
            // KS0223 real robot: 15cm wide, 25cm long, 20cm tall (with camera mast)
            // All values in meters

            // Chassis body (dark blue)
            EnsureVisualPart("Chassis", PrimitiveType.Cube,
                new Vector3(0.14f, 0.05f, 0.22f), new Vector3(0f, 0.035f, 0f));

            // PCB top platform (green)
            EnsureVisualPart("TopPlatform", PrimitiveType.Cube,
                new Vector3(0.12f, 0.015f, 0.18f), new Vector3(0f, 0.065f, 0f));

            // Battery underneath (dark gray)
            EnsureVisualPart("Battery", PrimitiveType.Cube,
                new Vector3(0.08f, 0.02f, 0.05f), new Vector3(0f, 0.015f, -0.03f));

            // Front bumper / ultrasonic mount
            EnsureVisualPart("FrontBumper", PrimitiveType.Cube,
                new Vector3(0.10f, 0.03f, 0.02f), new Vector3(0f, 0.04f, 0.12f));

            // Ultrasonic sensors (two small cylinders at front)
            EnsureVisualPart("UltrasonicL", PrimitiveType.Cylinder,
                new Vector3(0.02f, 0.0075f, 0.02f), new Vector3(-0.02f, 0.05f, 0.125f));
            EnsureVisualPart("UltrasonicR", PrimitiveType.Cylinder,
                new Vector3(0.02f, 0.0075f, 0.02f), new Vector3(0.02f, 0.05f, 0.125f));

            // Wheels (rotated 90° Z so cylinder axis is horizontal)
            EnsureVisualPart("WheelFL", PrimitiveType.Cylinder,
                new Vector3(0.025f, 0.0175f, 0.025f), new Vector3(-0.07f, 0.018f, 0.07f), new Vector3(0f, 0f, 90f));
            EnsureVisualPart("WheelFR", PrimitiveType.Cylinder,
                new Vector3(0.025f, 0.0175f, 0.025f), new Vector3(0.07f, 0.018f, 0.07f), new Vector3(0f, 0f, 90f));
            EnsureVisualPart("WheelRL", PrimitiveType.Cylinder,
                new Vector3(0.025f, 0.0175f, 0.025f), new Vector3(-0.07f, 0.018f, -0.07f), new Vector3(0f, 0f, 90f));
            EnsureVisualPart("WheelRR", PrimitiveType.Cylinder,
                new Vector3(0.025f, 0.0175f, 0.025f), new Vector3(0.07f, 0.018f, -0.07f), new Vector3(0f, 0f, 90f));

            // Camera mast
            if (transform.Find("CameraMast") == null)
            {
                var mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                mast.name = "CameraMast";
                mast.transform.SetParent(transform, false);
                mast.transform.localScale = new Vector3(0.015f, 0.03f, 0.015f);
                mast.transform.localPosition = new Vector3(0f, 0.10f, 0.06f);
                DisableCollider(mast);
            }

            // Camera head (black box on top of mast)
            EnsureVisualPart("CameraHead", PrimitiveType.Cube,
                new Vector3(0.03f, 0.02f, 0.02f), new Vector3(0f, 0.14f, 0.06f));
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

        private bool HasImportedVisualModel()
            => transform.Find("VisualModel") != null;

        private void RemovePresentationVisuals()
        {
            // Current parts
            RemoveIfExists("Chassis");
            RemoveIfExists("TopPlatform");
            RemoveIfExists("Battery");
            RemoveIfExists("FrontBumper");
            RemoveIfExists("UltrasonicL");
            RemoveIfExists("UltrasonicR");
            RemoveIfExists("WheelFL");
            RemoveIfExists("WheelFR");
            RemoveIfExists("WheelRL");
            RemoveIfExists("WheelRR");
            RemoveIfExists("CameraMast");
            RemoveIfExists("CameraHead");
            // Legacy part names
            RemoveIfExists("Hood");
            RemoveIfExists("Cabin");
            RemoveIfExists("RearDeck");
            RemoveIfExists("Windshield");
            RemoveIfExists("RearWindow");
            RemoveIfExists("CameraPod");
            RemoveIfExists("Body");
        }

        private void RemoveIfExists(string objectName)
        {
            var child = transform.Find(objectName);
            if (child == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }

        private void ApplyVisualPalette()
        {
            var chassisBlue = new Color(0.15f, 0.20f, 0.35f);
            var pcbGreen = new Color(0.10f, 0.45f, 0.15f);
            var batteryGray = new Color(0.20f, 0.20f, 0.20f);
            var wheelBlack = new Color(0.10f, 0.10f, 0.10f);
            var sensorSilver = new Color(0.70f, 0.70f, 0.70f);
            var mastGray = new Color(0.40f, 0.40f, 0.40f);
            var cameraBlack = new Color(0.05f, 0.05f, 0.05f);

            ApplyColor("Chassis", chassisBlue, 0.2f);
            ApplyColor("TopPlatform", pcbGreen, 0.15f);
            ApplyColor("Battery", batteryGray, 0.1f);
            ApplyColor("FrontBumper", chassisBlue, 0.2f);
            ApplyColor("UltrasonicL", sensorSilver, 0.4f);
            ApplyColor("UltrasonicR", sensorSilver, 0.4f);
            ApplyColor("WheelFL", wheelBlack, 0.3f);
            ApplyColor("WheelFR", wheelBlack, 0.3f);
            ApplyColor("WheelRL", wheelBlack, 0.3f);
            ApplyColor("WheelRR", wheelBlack, 0.3f);
            ApplyColor("CameraMast", mastGray, 0.2f);
            ApplyColor("CameraHead", cameraBlack, 0.1f);
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
            => RuntimeMaterialCompatibility.ResolveCompatibleLitShader();

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
                "top_down" or "top" or "bird" or "bird_eye" => "top_down",
                _ => "driver",
            };
        }

        private static ConfigKeyValue KV(string key, float value) => new ConfigKeyValue
        {
            key = key,
            value = value.ToString("0.###", CultureInfo.InvariantCulture),
        };
    }
}
