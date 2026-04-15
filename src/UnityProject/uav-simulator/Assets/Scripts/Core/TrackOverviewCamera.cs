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

            cam.orthographic = true;
            float maxExtent = Mathf.Max(TrackExtents.x, TrackExtents.y);
            cam.orthographicSize = maxExtent * 0.5f * 1.2f;

            transform.position = new Vector3(TrackCenter.x, 2.0f, TrackCenter.z);
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 10f;
        }
    }
}
