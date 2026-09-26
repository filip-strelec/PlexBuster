using PlexBuster.Store;
using UnityEditor;
using UnityEngine;

namespace PlexBuster.Editor
{
    /// <summary>
    /// PlexBuster > Set Up TV Room and Cinema: creates the screen materials (the PlexBuster/Video Screen shader,
    /// with the CRT look for the TV and a clean picture for the cinema) and assigns them, and the beam material,
    /// to the store theme. Safe to run again: existing materials are reused.
    /// </summary>
    public static class ScreeningMenu
    {
        const string Folder = "Assets/Materials/Store";
        const string ShaderName = "PlexBuster/Video Screen";

        [MenuItem("PlexBuster/Set Up TV Room and Cinema")]
        static void SetUp()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[Screening] Shader {ShaderName} not found.");
                return;
            }
            var guids = AssetDatabase.FindAssets("t:" + nameof(StoreTheme));
            if (guids.Length == 0)
            {
                Debug.LogError("[Screening] No StoreTheme asset found.");
                return;
            }
            var theme = AssetDatabase.LoadAssetAtPath<StoreTheme>(AssetDatabase.GUIDToAssetPath(guids[0]));

            theme.crtScreenMaterial = Material(shader, "CrtScreen", m =>
            {
                m.SetFloat("_Brightness", 1.35f);
                m.SetFloat("_Curvature", 0.18f);
                m.SetFloat("_Scanlines", 0.45f);
                m.SetFloat("_ScanlineCount", 360f);
                m.SetFloat("_Mask", 0.3f);
                m.SetFloat("_Vignette", 0.9f);
            });
            theme.cinemaScreenMaterial = Material(shader, "CinemaScreen", m =>
            {
                m.SetFloat("_Brightness", 1.15f);
                m.SetFloat("_Vignette", 0.15f);
            });
            if (theme.beamMaterial == null)
                theme.beamMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{Folder}/HologramBeam.mat");

            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Screening] Screen materials assigned to {theme.name}.", theme);
        }

        static Material Material(Shader shader, string name, System.Action<Material> configure)
        {
            var path = $"{Folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(shader) { name = name };
            configure(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
