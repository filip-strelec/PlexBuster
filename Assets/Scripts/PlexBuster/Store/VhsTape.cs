using PlexBuster.Data;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace PlexBuster.Store
{
    /// <summary>A rental case showing one library item's poster on its front, and its details on the back while held.</summary>
    [RequireComponent(typeof(TapeGrabInteractable))]
    public class VhsTape : MonoBehaviour
    {
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] Renderer coverRenderer;
        [SerializeField] Material backCoverMaterial;
        [SerializeField] Color loadingColor = new(0.12f, 0.12f, 0.14f);

        MaterialPropertyBlock block;
        PosterCache posters;
        BackCover backCover;

        public LibraryItem Item { get; private set; }
        public TapeGrabInteractable Grab { get; private set; }

        void Awake()
        {
            Grab = GetComponent<TapeGrabInteractable>();
            Grab.selectEntered.AddListener(OnGrabbed);
            Grab.selectExited.AddListener(OnReleased);
            block = new MaterialPropertyBlock();
            ShowCover(null);
        }

        // Only release here: when a whole shelf is destroyed the cover renderer may already be gone,
        // and a held back cover is destroyed along with the tape.
        void OnDestroy() => ReleasePoster();

        public async void Bind(LibraryItem item, PosterCache cache)
        {
            Unbind();
            Item = item;
            posters = cache;
            name = $"Tape: {item.Title} ({item.Year})";

            var texture = await cache.AcquireAsync(item);
            // The tape may have been destroyed or rebound while the poster loaded.
            if (this != null && Item == item) ShowCover(texture);
        }

        public void Unbind()
        {
            ReleasePoster();
            ShowCover(null);
        }

        void ReleasePoster()
        {
            if (Item != null) posters.Release(Item);
            Item = null;
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            if (backCover == null && Item != null) backCover = BackCover.Attach(this, backCoverMaterial);
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (Grab.isSelected || backCover == null) return;
            backCover.Detach();
            backCover = null;
        }

        void ShowCover(Texture texture)
        {
            coverRenderer.GetPropertyBlock(block);
            block.SetTexture(BaseMapId, texture != null ? texture : Texture2D.whiteTexture);
            block.SetColor(BaseColorId, texture != null ? Color.white : loadingColor);
            coverRenderer.SetPropertyBlock(block);
        }
    }
}
