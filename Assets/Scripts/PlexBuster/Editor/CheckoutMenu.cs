using PlexBuster.Store;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PlexBuster.Editor
{
    /// <summary>
    /// PlexBuster > Add Checkout To Counter: turns the lobby counter (the selected object, or the one named
    /// "Counter") into the checkout where tapes are played on the TV, or selects the checkout already there.
    /// </summary>
    public static class CheckoutMenu
    {
        [MenuItem("PlexBuster/Add Checkout To Counter")]
        static void Add()
        {
            var existing = Object.FindAnyObjectByType<CheckoutCounter>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            var selected = Selection.activeGameObject;
            var counter = selected != null && selected.GetComponent<BoxCollider>() != null ? selected : GameObject.Find("Counter");
            if (counter == null || counter.GetComponent<BoxCollider>() == null)
            {
                Debug.LogWarning("[Checkout] No counter found: select the counter (it needs a BoxCollider) and run this again.");
                return;
            }

            Undo.AddComponent<CheckoutCounter>(counter);
            EditorSceneManager.MarkSceneDirty(counter.scene);
            Selection.activeGameObject = counter;
            Debug.Log($"[Checkout] Added the checkout to {counter.name}; save the scene to keep it.", counter);
        }
    }
}
