using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>
    /// Tiles a scaled box's texture by real-world size instead of stretching it once across every face.
    /// The box's thinnest axis is taken as its normal; the other two set the tiling.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(Renderer))]
    public class TiledSurface : MonoBehaviour
    {
        static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");

        [SerializeField, Min(0.01f)] float metersPerTile = 1f;

        public static void Apply(GameObject box, float metersPerTile)
        {
            var surface = box.AddComponent<TiledSurface>();
            surface.metersPerTile = metersPerTile;
            surface.Refresh();
        }

        void OnEnable() => Refresh();
        void OnValidate() => Refresh();

        void Refresh()
        {
            var s = transform.lossyScale;
            Vector2 size;
            if (s.y <= s.x && s.y <= s.z) size = new Vector2(s.x, s.z);      // floor / ceiling
            else if (s.z <= s.x) size = new Vector2(s.x, s.y);                // wall along X
            else size = new Vector2(s.z, s.y);                                // wall along Z

            var renderer = GetComponent<Renderer>();
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetVector(BaseMapSt, new Vector4(size.x / metersPerTile, size.y / metersPerTile, 0, 0));
            renderer.SetPropertyBlock(block);
        }
    }
}
