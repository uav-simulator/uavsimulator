using Unity.Sentis;
using UnityEngine;

namespace UavSimulator.CityDemo
{
    /// <summary>
    /// ONNX-classifier-based traffic-light awareness. Same
    /// <see cref="IMovementGate"/> contract as the ground-truth
    /// <see cref="UavSimulator.Vehicles.TrafficLightAwareController"/>, but
    /// the decision is produced by running an ONNX model on a downsampled
    /// camera frame instead of a raycast.
    ///
    /// Loads the ModelAsset via <see cref="Resources.Load"/> — drop the
    /// classifier ONNX at Assets/Resources/Models/tl-classifier.onnx and the
    /// Sentis importer auto-converts it to a ModelAsset Unity can load.
    ///
    /// When no model is available the gate falls back to a permanently-Green
    /// verdict (no braking), keeping the component safe in development scenes.
    /// </summary>
    public sealed class OnnxTrafficLightAwareController : MonoBehaviour, IMovementGate
    {
        [SerializeField] private string modelResourcesPath = "Models/tl-classifier";
        [SerializeField] private Camera cameraSource;
        [SerializeField] private float predictHz = 4f;

        private OnnxClassifierService svc;
        private RenderTexture renderTex;
        private Texture2D readback;
        private string lastState = "Green";
        private float lastPredictAt = -1f;

        /// <summary>Latest classifier verdict ("Red" / "Yellow" / "Green").</summary>
        public string LastVerdict => lastState;

        private void Awake()
        {
            var modelAsset = Resources.Load<ModelAsset>(modelResourcesPath);
            if (modelAsset != null)
            {
                svc = new OnnxClassifierService(modelAsset);
            }
            else
            {
                Debug.LogWarning(
                    $"[OnnxTrafficLightAwareController] ModelAsset not found at " +
                    $"Resources/{modelResourcesPath}; gate will default to Green.");
            }
            renderTex = new RenderTexture(84, 84, 0, RenderTextureFormat.ARGB32);
            readback = new Texture2D(84, 84, TextureFormat.RGBA32, false);
        }

        private void OnDestroy()
        {
            svc?.Dispose();
            if (renderTex != null) renderTex.Release();
        }

        private void Update()
        {
            if (svc == null || cameraSource == null) return;
            if (Time.time - lastPredictAt < 1f / predictHz) return;
            lastPredictAt = Time.time;

            var prev = cameraSource.targetTexture;
            cameraSource.targetTexture = renderTex;
            cameraSource.Render();
            RenderTexture.active = renderTex;
            readback.ReadPixels(new Rect(0, 0, 84, 84), 0, 0);
            readback.Apply();
            RenderTexture.active = null;
            cameraSource.targetTexture = prev;

            var pixels = readback.GetPixels32();
            var probs = svc.Predict(pixels, 84, 84);
            int argmax = 0;
            for (int i = 1; i < probs.Length; i++)
            {
                if (probs[i] > probs[argmax]) argmax = i;
            }
            lastState = argmax switch
            {
                0 => "Red",
                1 => "Yellow",
                _ => "Green",
            };
        }

        public bool ShouldBrake(out float brakeIntensity)
        {
            switch (lastState)
            {
                case "Red":
                    brakeIntensity = 1.0f;
                    return true;
                case "Yellow":
                    brakeIntensity = 0.5f;
                    return true;
                default:
                    brakeIntensity = 0f;
                    return false;
            }
        }
    }
}
