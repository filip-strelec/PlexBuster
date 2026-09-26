using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>Small runtime-made materials for props built from code (terminal plastic, frames, screen text).</summary>
    public static class StoreMaterials
    {
        static readonly Dictionary<string, Material> Cache = new();

        public static Material Lit(string name, Color color, float smoothness, Color? emission = null)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            return Cache[name] = material;
        }

        /// <summary>A glowing, single-sided copy of the default TextMeshPro material (phosphor screens, neon).</summary>
        public static Material GlowText(string name, Color hdrColor)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null) return cached;
            var material = new Material(TMP_Settings.defaultFontAsset.material) { name = name };
            material.SetColor("_FaceColor", hdrColor);
            material.SetFloat("_CullMode", 2);
            return Cache[name] = material;
        }
    }
}
