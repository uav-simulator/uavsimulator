using UnityEngine;

namespace UavSimulator.Core
{
    /// <summary>
    /// Static top-down camera that frames the entire track.
    /// Auto-attaches to Camera.main on scene load.
    /// Orthographic, no follow, no free-fly — just a bird's-eye view.
    /// </summary>
    public sealed class TrackOverviewCamera : MonoBehaviour
    {
        // L-shape bounding box for cardboard corridor (60cm width):
        // x: −0.30 to +0.90, z: −1.10 to +0.30
        private static readonly Vector3 TrackCenter = new Vector3(0.30f, 0f, -0.40f);
        private static readonly Vector2 TrackExtents = new Vector2(1.20f, 1.40f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoAttach()
        {
            var mainCam = Camera.main;
            if (mainCam == null) return;

            // Remove old follow camera if present
            var oldFollow = mainCam.GetComponent<PresentationFollowCamera>();
            if (oldFollow != null) Destroy(oldFollow);

            if (mainCam.GetComponent<TrackOverviewCamera>() == null)
            {
                mainCam.gameObject.AddComponent<TrackOverviewCamera>();
            }
        }

        private void Start()
        {
            SetupTopDown();
        }

        private void SetupTopDown()
        {
            var cam = GetComponent<Camera>();
            if (cam == null) return;

            cam.orthographic = false;
            cam.fieldOfView = 45f;

            // Isometric-ish angle: 65° down, slightly rotated
            float height = 2.2f;
            float pullBack = 0.6f;
            transform.position = new Vector3(TrackCenter.x - pullBack * 0.3f, height, TrackCenter.z - pullBack);
            transform.rotation = Quaternion.Euler(60f, 15f, 0f);

            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 10f;
        }
    }
}
