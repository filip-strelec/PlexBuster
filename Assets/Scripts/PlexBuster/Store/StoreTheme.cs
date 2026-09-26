using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>Everything generated parts of the store are built from, so the art pass can swap it in one place.</summary>
    [CreateAssetMenu(menuName = "PlexBuster/Store Theme", fileName = "StoreTheme")]
    public class StoreTheme : ScriptableObject
    {
        [Header("Prefabs")]
        public VhsTape tapePrefab;

        [Header("Materials")]
        public Material shelfMaterial;
        public Material floorMaterial;
        public Material wallMaterial;
        public Material ceilingMaterial;
        public Material lightPanelMaterial;
        public Material doorFrameMaterial;
        public Material doorwayMaterial;
        public Material fadeMaterial;

        [Header("Signs")]
        public Color signColor = new(1f, 0.8f, 0.1f);
        public Color labelColor = new(1f, 0.85f, 0.3f);
        public Color exitColor = new(0.9f, 0.15f, 0.1f);

        [Header("Rooms")]
        public float wallHeight = 3f;
        public Color lightColor = new(1f, 0.97f, 0.9f);
        public float lightIntensity = 1.4f;
        public float lightRange = 7f;
        public float lightSpacing = 4f;
        [Tooltip("How far below the ceiling the point lights hang; too close and they blow out the ceiling.")]
        public float lightDrop = 0.8f;
        [Min(1)] public int tapesPerFrame = 24;
    }
}
