using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UavSimulator.Core
{
    public static class RuntimeMaterialCompatibility
    {
        public static bool IsUrpActive()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
            {
                return false;
            }

            var typeName = pipeline.GetType().Name;
            return typeName.Contains("UniversalRenderPipeline", StringComparison.Ordinal) ||
                   typeName.Contains("URP", StringComparison.Ordinal);
        }

        public static Shader ResolveCompatibleLitShader()
        {
            if (IsUrpActive())
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader != null && shader.isSupported)
                {
                    return shader;
                }

                shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (shader != null && shader.isSupported)
                {
                    return shader;
                }
            }

            var standard = Shader.Find("Standard");
            if (standard != null && standard.isSupported)
            {
                return standard;
            }

            var unlitTexture = Shader.Find("Unlit/Texture");
            if (unlitTexture != null && unlitTexture.isSupported)
            {
                return unlitTexture;
            }

            var unlitColor = Shader.Find("Unlit/Color");
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

            return !IsShaderCompatibleForCurrentPipeline(shader);
        }

        public static bool IsShaderCompatibleForCurrentPipeline(Shader shader)
        {
            if (shader == null)
            {
                return false;
            }

            var name = shader.name ?? string.Empty;
            if (IsUrpActive())
            {
                return name.StartsWith("Universal Render Pipeline/", StringComparison.OrdinalIgnoreCase);
            }

            return name.StartsWith("Standard", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Legacy Shaders/", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Unlit/", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Mobile/", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Particles/", StringComparison.OrdinalIgnoreCase);
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

            return material;
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

            return source.color;
        }
    }
}
