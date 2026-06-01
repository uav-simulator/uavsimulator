using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UavSimulator.Core
{
    public static class RuntimeMaterialCompatibility
    {
        private const string UrpLitResourcePath = "UavSimulator/RuntimeShaders/RuntimeUrpLit";
        private const string UrpUnlitResourcePath = "UavSimulator/RuntimeShaders/RuntimeUrpUnlit";
        private const string UnlitColorResourcePath = "UavSimulator/RuntimeShaders/RuntimeUnlitColor";
        private const string UnlitTextureResourcePath = "UavSimulator/RuntimeShaders/RuntimeUnlitTexture";
        private const string StandardResourcePath = "UavSimulator/RuntimeShaders/RuntimeStandard";

        private enum RenderPipelineKind
        {
            Unknown,
            Builtin,
            Universal,
            Other,
        }

        public static bool IsUrpActive()
        {
            return ResolvePipelineKind() == RenderPipelineKind.Universal;
        }

        public static Shader ResolveCompatibleLitShader()
        {
            var seededUrpLit = LoadShaderFromMaterialResource(UrpLitResourcePath);
            if (seededUrpLit != null)
            {
                return seededUrpLit;
            }

            var configuredShader = ResolveConfiguredDefaultLitShader();
            if (configuredShader != null)
            {
                return configuredShader;
            }

            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit != null)
            {
                return urpLit;
            }

            var urpSimpleLit = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (urpSimpleLit != null)
            {
                return urpSimpleLit;
            }

            var standard = Shader.Find("Standard");
            if (standard != null && standard.isSupported)
            {
                return standard;
            }

            var seededStandard = LoadShaderFromMaterialResource(StandardResourcePath);
            if (seededStandard != null)
            {
                return seededStandard;
            }

            var unlitTexture = ResolveCompatibleUnlitTextureShader();
            if (unlitTexture != null && unlitTexture.isSupported)
            {
                return unlitTexture;
            }

            var unlitColor = ResolveCompatibleUnlitColorShader();
            if (unlitColor != null && unlitColor.isSupported)
            {
                return unlitColor;
            }

            var legacy = Shader.Find("Legacy Shaders/Diffuse");
            if (legacy != null)
            {
                return legacy;
            }

            throw new MissingReferenceException("Unable to resolve a compatible runtime shader for the active render pipeline.");
        }

        public static Shader ResolveCompatibleUnlitShader()
        {
            var seededUrpUnlit = LoadShaderFromMaterialResource(UrpUnlitResourcePath);
            if (seededUrpUnlit != null)
            {
                return seededUrpUnlit;
            }

            var urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (urpUnlit != null)
            {
                return urpUnlit;
            }

            return ResolveCompatibleUnlitColorShader() ?? ResolveCompatibleLitShader();
        }

        public static Shader ResolveCompatibleUnlitColorShader()
        {
            var seededUnlitColor = LoadShaderFromMaterialResource(UnlitColorResourcePath);
            if (seededUnlitColor != null)
            {
                return seededUnlitColor;
            }

            var unlitColor = Shader.Find("Unlit/Color");
            if (unlitColor != null)
            {
                return unlitColor;
            }

            return null;
        }

        public static Shader ResolveCompatibleUnlitTextureShader()
        {
            var seededUnlitTexture = LoadShaderFromMaterialResource(UnlitTextureResourcePath);
            if (seededUnlitTexture != null)
            {
                return seededUnlitTexture;
            }

            var unlitTexture = Shader.Find("Unlit/Texture");
            if (unlitTexture != null)
            {
                return unlitTexture;
            }

            return null;
        }

        public static bool NeedsReplacement(Material source)
        {
            if (source == null)
            {
                return false;
            }

            var shader = source.shader;
            if (shader == null || !shader.isSupported)
            {
                return true;
            }

            if (ResolvePipelineKind() == RenderPipelineKind.Unknown)
            {
                return false;
            }

            return !IsShaderCompatibleForCurrentPipeline(shader);
        }

        public static bool IsShaderCompatibleForCurrentPipeline(Shader shader)
        {
            if (shader == null)
            {
                return false;
            }

            var name = shader.name ?? string.Empty;
            switch (ResolvePipelineKind())
            {
                case RenderPipelineKind.Universal:
                    return name.StartsWith("Universal Render Pipeline/", StringComparison.OrdinalIgnoreCase);
                case RenderPipelineKind.Builtin:
                    return name.StartsWith("Standard", StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith("Legacy Shaders/", StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith("Unlit/", StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith("Mobile/", StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith("Particles/", StringComparison.OrdinalIgnoreCase);
                case RenderPipelineKind.Unknown:
                    return true;
                default:
                    return true;
            }
        }

        public static Material CreateReplacementMaterial(Material source, float defaultSmoothness = 0.2f, bool copyTextures = true)
        {
            var material = new Material(ResolveCompatibleLitShader())
            {
                color = ReadSourceColor(source),
            };

            if (copyTextures)
            {
                var texture = ReadSourceTexture(source);
                if (texture != null)
                {
                    if (material.HasProperty("_MainTex"))
                    {
                        material.SetTexture("_MainTex", texture);
                    }

                    if (material.HasProperty("_BaseMap"))
                    {
                        material.SetTexture("_BaseMap", texture);
                    }
                }
            }

            var smoothness = defaultSmoothness;
            if (source != null)
            {
                if (source.HasProperty("_Smoothness"))
                {
                    smoothness = source.GetFloat("_Smoothness");
                }
                else if (source.HasProperty("_Glossiness"))
                {
                    smoothness = source.GetFloat("_Glossiness");
                }
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }

            CopyEmission(source, material);

            return material;
        }

        private static void CopyEmission(Material source, Material target)
        {
            if (source == null || target == null || !source.HasProperty("_EmissionColor") || !target.HasProperty("_EmissionColor"))
            {
                return;
            }

            var emission = source.GetColor("_EmissionColor");
            target.SetColor("_EmissionColor", emission);
            if (emission.maxColorComponent > 0.001f)
            {
                target.EnableKeyword("_EMISSION");
                target.globalIlluminationFlags = source.globalIlluminationFlags;
            }
        }

        public static Texture ReadSourceTexture(Material source)
        {
            if (source == null)
            {
                return null;
            }

            if (source.mainTexture != null)
            {
                return source.mainTexture;
            }

            if (source.HasProperty("_BaseMap"))
            {
                return source.GetTexture("_BaseMap");
            }

            if (source.HasProperty("_MainTex"))
            {
                return source.GetTexture("_MainTex");
            }

            return null;
        }

        public static Color ReadSourceColor(Material source)
        {
            if (source == null)
            {
                return Color.white;
            }

            if (source.HasProperty("_BaseColor"))
            {
                return source.GetColor("_BaseColor");
            }

            if (source.HasProperty("_Color"))
            {
                return source.GetColor("_Color");
            }

            return Color.white;
        }

        private static RenderPipelineKind ResolvePipelineKind()
        {
            var pipeline = ResolveConfiguredPipelineAsset();

            if (pipeline == null)
            {
                return RenderPipelineKind.Unknown;
            }

            var typeName = pipeline.GetType().Name ?? string.Empty;
            if (typeName.Contains("UniversalRenderPipeline", StringComparison.Ordinal) ||
                typeName.Contains("URP", StringComparison.Ordinal))
            {
                return RenderPipelineKind.Universal;
            }

            return typeName.Length == 0 ? RenderPipelineKind.Unknown : RenderPipelineKind.Other;
        }

        private static RenderPipelineAsset ResolveConfiguredPipelineAsset()
        {
            return GraphicsSettings.currentRenderPipeline
                ?? QualitySettings.renderPipeline
                ?? GraphicsSettings.defaultRenderPipeline;
        }

        private static Shader ResolveConfiguredDefaultLitShader()
        {
            var pipeline = ResolveConfiguredPipelineAsset();
            if (pipeline == null)
            {
                return null;
            }

            var defaultMaterial = pipeline.defaultMaterial;
            return defaultMaterial != null ? defaultMaterial.shader : null;
        }

        private static Shader LoadShaderFromMaterialResource(string resourcePath)
        {
            var material = Resources.Load<Material>(resourcePath);
            return material != null ? material.shader : null;
        }
    }
}
