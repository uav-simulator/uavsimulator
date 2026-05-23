using System;
using Unity.Sentis;
using UnityEngine;

namespace UavSimulator.CityDemo
{
    /// <summary>
    /// Thin wrapper over Unity Sentis to run a 3-class image classifier
    /// (Red / Yellow / Green) on an 84x84 RGB input.
    /// Used by <see cref="OnnxTrafficLightAwareController"/> (Plan B.3).
    ///
    /// Layout:
    ///   Input  shape NCHW = (1, 3, 84, 84), values in [0, 1] float32.
    ///   Output shape (1, 3) — softmax probabilities [P(Red), P(Yellow), P(Green)].
    ///
    /// If the trained model does not include a final softmax layer the values
    /// returned here are raw logits; callers should apply softmax themselves.
    /// The trainer in Plan B.6 is expected to export with softmax baked in.
    /// </summary>
    public sealed class OnnxClassifierService : IDisposable
    {
        private readonly Model _model;
        private readonly Worker _worker;
        private bool _disposed;

        public OnnxClassifierService(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                throw new ArgumentException("modelPath must not be empty", nameof(modelPath));
            }

            // NOTE: Sentis 2.x ModelLoader.Load(string) expects a *.sentis archive,
            // not a raw PyTorch-exported .onnx file. The standard workflow is to
            // drop the .onnx into Assets/, let Sentis's editor importer convert
            // it to a ModelAsset, and load via the ModelAsset ctor below.
            // String-path ctor still works for already-converted .sentis files
            // on disk (e.g. StreamingAssets/something.sentis).
            _model = ModelLoader.Load(modelPath);
            _worker = new Worker(_model, BackendType.CPU);
        }

        /// <summary>
        /// Editor-friendly ctor accepting a Sentis ModelAsset — produced when
        /// you drag a .onnx into the Unity project and Sentis's importer
        /// converts it to a ModelAsset. Prefer this in production setups.
        /// </summary>
        public OnnxClassifierService(ModelAsset asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }
            _model = ModelLoader.Load(asset);
            _worker = new Worker(_model, BackendType.CPU);
        }

        /// <summary>
        /// Run inference on a flat <see cref="Color32"/> pixel buffer in
        /// row-major order (origin top-left). Returns three floats in the
        /// order [Red, Yellow, Green].
        /// </summary>
        public float[] Predict(Color32[] pixels, int width, int height)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException(nameof(pixels));
            }
            if (pixels.Length != width * height)
            {
                throw new ArgumentException(
                    $"pixels length {pixels.Length} does not match width*height = {width * height}",
                    nameof(pixels));
            }

            using var input = new Tensor<float>(new TensorShape(1, 3, height, width));
            for (int i = 0; i < pixels.Length; i++)
            {
                int x = i % width;
                int y = i / width;
                var p = pixels[i];
                // Indexer order is [d3, d2, d1, d0] == [batch, channel, height, width].
                input[0, 0, y, x] = p.r / 255f;
                input[0, 1, y, x] = p.g / 255f;
                input[0, 2, y, x] = p.b / 255f;
            }

            _worker.Schedule(input);
            using var output = _worker.PeekOutput().ReadbackAndClone() as Tensor<float>;
            if (output == null)
            {
                throw new InvalidOperationException(
                    "Model output tensor was not a Tensor<float>; check the exported model.");
            }
            return output.DownloadToArray();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _worker?.Dispose();
            _disposed = true;
        }
    }
}
