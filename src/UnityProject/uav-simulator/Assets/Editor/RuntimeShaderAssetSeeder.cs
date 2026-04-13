using System.IO;
using UnityEditor;
using UnityEngine;

namespace UavSimulator.EditorTools
{
    public static class RuntimeShaderAssetSeeder
    {
        private const string ResourceFolder = "Assets/Resources/UavSimulator/RuntimeShaders";

        public static void EnsureRuntimeShaderAssets()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/UavSimulator");
            EnsureFolder(ResourceFolder);

            EnsureMaterialAsset("RuntimeUrpLit.mat", "Universal Render Pipeline/Lit");
            EnsureMaterialAsset("RuntimeUrpUnlit.mat", "Universal Render Pipeline/Unlit");
            EnsureMaterialAsset("RuntimeUnlitColor.mat", "Unlit/Color");
            EnsureMaterialAsset("RuntimeUnlitTexture.mat", "Unlit/Texture");
            EnsureMaterialAsset("RuntimeStandard.mat", "Standard");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureMaterialAsset(string fileName, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[RuntimeShaderAssetSeeder] Shader not found: {shaderName}");
                return;
            }

            var assetPath = Path.Combine(ResourceFolder, fileName).Replace('\\', '/');
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = Path.GetFileNameWithoutExtension(fileName),
                };
                AssetDatabase.CreateAsset(material, assetPath);
                return;
            }

            if (material.shader == shader)
            {
                return;
            }

            material.shader = shader;
            EditorUtility.SetDirty(material);
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            var name = Path.GetFileName(assetPath);
            if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            if (string.IsNullOrWhiteSpace(parent))
            {
                return;
            }

            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
