using UavSimulator.Tracks;
using UnityEngine;

namespace UavSimulator.Core
{
    /// <summary>
    /// Dynamic track overview camera — frames the entire active track from above
    /// with a slight isometric angle. Recalculates bounds on every reset so it
    /// works with procedural maze tracks of any size.
    /// </summary>
    public sealed class TrackOverviewCamera : MonoBehaviour
    {
        [SerializeField] private float isoPitchDeg = 75f;      // closer to top-down (90=straight down)
        [SerializeField] private float isoYawDeg = 10f;         // slight rotation for depth
        [SerializeField] private float paddingRatio = 0.5f;     // 50% extra margin to always see full track
        [SerializeField] private float minHeight = 1.5f;
        [SerializeField] private float maxHeight = 50f;

        // Fallback bounds when no track geometry found (L-corridor defaults)
        private static readonly Vector3 FallbackCenter = new Vector3(0.30f, 0f, -0.40f);
        private static readonly Vector2 FallbackExtents = new Vector2(1.20f, 1.40f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoAttach()
        {
            var mainCam = Camera.main;
            if (mainCam == null) return;

            var oldFollow = mainCam.GetComponent<PresentationFollowCamera>();
            if (oldFollow != null) Destroy(oldFollow);

            if (mainCam.GetComponent<TrackOverviewCamera>() == null)
            {
                mainCam.gameObject.AddComponent<TrackOverviewCamera>();
            }
        }

        private void Start()
        {
            FrameTrack();
        }

        private void Update()
        {
            // Re-frame periodically so new/regenerated tracks are captured.
            // Uses time.frameCount modulo to avoid doing work every frame.
            if (Time.frameCount % 30 == 0)
            {
                FrameTrack();
            }
        }

        private void FrameTrack()
        {
            var cam = GetComponent<Camera>();
            if (cam == null) return;

            cam.orthographic = false;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;

            if (!TryComputeTrackBounds(out var center, out var extents))
            {
                center = FallbackCenter;
                extents = FallbackExtents;
            }

            // Apply padding
            extents *= (1f + paddingRatio);
            float maxExtent = Mathf.Max(extents.x, extents.y);

            // Distance required to frame maxExtent at the camera's FOV
            float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
            float requiredDistance = (maxExtent * 0.5f) / Mathf.Tan(fovRad * 0.5f);
            // Use vertical axis as limiting factor — also account for isometric angle
            requiredDistance /= Mathf.Cos(isoPitchDeg * Mathf.Deg2Rad * 0.5f);

            float height = Mathf.Clamp(
                requiredDistance * Mathf.Sin(isoPitchDeg * Mathf.Deg2Rad),
                minHeight, maxHeight);
            float horizontalPull = requiredDistance * Mathf.Cos(isoPitchDeg * Mathf.Deg2Rad);

            // Position camera behind and above the track center, looking down at pitch+yaw
            var yawRad = isoYawDeg * Mathf.Deg2Rad;
            transform.position = new Vector3(
                center.x - horizontalPull * Mathf.Sin(yawRad),
                height,
                center.z - horizontalPull * Mathf.Cos(yawRad));
            transform.rotation = Quaternion.Euler(isoPitchDeg, isoYawDeg, 0f);
        }

        /// <summary>
        /// Compute bounding box of the active track from its geometry.
        /// Priority 1: track's waypoints (accurate for corridors).
        /// Priority 2: renderers in track children (fallback).
        /// </summary>
        private bool TryComputeTrackBounds(out Vector3 center, out Vector2 extents)
        {
            center = Vector3.zero;
            extents = Vector2.zero;

            var track = FindFirstObjectByType<TrackBase>();
            if (track == null) return false;

            // Use renderer bounds of the track's children — gives the smallest
            // enclosing box of walls and floors, which is what we want to frame.
            var renderers = track.GetComponentsInChildren<Renderer>(includeInactive: false);
            if (renderers == null || renderers.Length == 0) return false;

            bool hasBounds = false;
            Bounds combined = default;
            foreach (var r in renderers)
            {
                // Skip the surrounding gray floor (named "SurroundFloor") — it'd inflate bounds
                if (r.gameObject != null && r.gameObject.name.StartsWith("SurroundFloor")) continue;
                // Skip track light (no renderer usually, but be safe)
                if (r.gameObject != null && r.gameObject.name.StartsWith("TrackLight")) continue;

                if (!hasBounds)
                {
                    combined = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(r.bounds);
                }
            }

            if (!hasBounds) return false;

            center = new Vector3(combined.center.x, 0f, combined.center.z);
            extents = new Vector2(combined.size.x, combined.size.z);
            return true;
        }
    }
}
