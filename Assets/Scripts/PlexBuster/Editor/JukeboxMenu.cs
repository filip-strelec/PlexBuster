using PlexBuster.Store;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PlexBuster.Editor
{
    /// <summary>PlexBuster > Add Lobby Jukebox: puts the jukebox in the open scene, or selects the one already there.</summary>
    public static class JukeboxMenu
    {
        // Against the lobby's back wall, left of the Departments opening, facing the entrance.
        static readonly Vector3 Position = new(-3f, 0f, 2.9f);
        static readonly Quaternion Rotation = Quaternion.Euler(0, 180, 0);

        [MenuItem("PlexBuster/Add Lobby Jukebox")]
        static void Add()
        {
            var existing = Object.FindAnyObjectByType<Jukebox>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            var go = new GameObject("Jukebox");
            go.transform.SetPositionAndRotation(Position, Rotation);
            var jukebox = go.AddComponent<Jukebox>();

            var themes = AssetDatabase.FindAssets("t:" + nameof(StoreTheme));
            if (themes.Length > 0)
            {
                var serialized = new SerializedObject(jukebox);
                serialized.FindProperty("theme").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<StoreTheme>(AssetDatabase.GUIDToAssetPath(themes[0]));
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else Debug.LogWarning("[Jukebox] No StoreTheme asset found; assign one on the Jukebox.", jukebox);

            Undo.RegisterCreatedObjectUndo(go, "Add Lobby Jukebox");
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
        }
    }
}
