using PlexBuster.Store;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlexBuster.Editor
{
    /// <summary>
    /// PlexBuster > Add Info Station and Basket: puts the hologram info desk and the shopping basket (on its stand)
    /// in the open scene's lobby, or selects them if they're already there.
    /// </summary>
    public static class InfoStationMenu
    {
        const string BeamMaterialPath = "Assets/Materials/Store/HologramBeam.mat";

        // Back right of the lobby, between the Departments opening and the counter, turned towards where the player
        // starts, with bare wall behind the hologram.
        static readonly Vector3 StationPosition = new(3.1f, 0f, 2.2f);
        static readonly Quaternion StationRotation = Quaternion.LookRotation(new Vector3(-3.1f, 0, -4.2f));
        // Near the entrance, right of where the player starts, its sign facing them.
        static readonly Vector3 BasketPosition = new(1.3f, 0.3f, -2.2f);
        static readonly Quaternion BasketRotation = Quaternion.Euler(0, -90, 0);

        [MenuItem("PlexBuster/Add Info Station and Basket")]
        static void Add()
        {
            var theme = FindTheme();
            if (theme == null)
            {
                Debug.LogError("[Info] No StoreTheme asset found.");
                return;
            }

            var station = Object.FindAnyObjectByType<InfoStation>();
            if (station == null)
            {
                var go = new GameObject("Info Station");
                go.transform.SetPositionAndRotation(StationPosition, StationRotation);
                station = go.AddComponent<InfoStation>();
                Assign(station, "theme", theme);
                Assign(station, "beamMaterial", BeamMaterial(theme));
                Undo.RegisterCreatedObjectUndo(go, "Add Info Station");
                EditorSceneManager.MarkSceneDirty(go.scene);
                Debug.Log($"[Info] Added the info station at {StationPosition}.", go);
            }

            var basket = Object.FindAnyObjectByType<ShoppingBasket>();
            if (basket == null)
            {
                var go = new GameObject("Shopping Basket");
                go.transform.SetPositionAndRotation(BasketPosition, BasketRotation);
                basket = go.AddComponent<ShoppingBasket>();
                Assign(basket, "theme", theme);
                Undo.RegisterCreatedObjectUndo(go, "Add Shopping Basket");
                EditorSceneManager.MarkSceneDirty(go.scene);
                Debug.Log($"[Info] Added the shopping basket at {BasketPosition}.", go);
            }

            Selection.activeGameObject = station.gameObject;
        }

        static StoreTheme FindTheme()
        {
            var themes = AssetDatabase.FindAssets("t:" + nameof(StoreTheme));
            return themes.Length > 0 ? AssetDatabase.LoadAssetAtPath<StoreTheme>(AssetDatabase.GUIDToAssetPath(themes[0])) : null;
        }

        static void Assign(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A soft cyan, additive, double-sided URP Unlit material (the same shader as the theme's light panels).</summary>
        static Material BeamMaterial(StoreTheme theme)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(BeamMaterialPath);
            if (existing != null) return existing;

            var material = new Material(theme.lightPanelMaterial.shader) { name = "HologramBeam" };
            material.SetFloat("_Surface", 1);      // transparent
            material.SetFloat("_Blend", 2);        // additive
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(0.25f, 0.75f, 1f, 0.18f));
            AssetDatabase.CreateAsset(material, BeamMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }
    }
}
