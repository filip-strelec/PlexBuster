using PlexBuster.Data;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace PlexBuster.Store
{
    /// <summary>A rental case showing one library item's poster on its front, and its details on the back while held.</summary>
    [RequireComponent(typeof(TapeGrabInteractable))]
    public class VhsTape : MonoBehaviour
    {
        [SerializeField] Renderer coverRenderer;
        [SerializeField, Tooltip("Template for cover materials; each poster gets its own copy with the poster as base map.")]
        Material coverMaterial;
        [SerializeField, Tooltip("Shown while the poster loads, or when there is none.")]
        Material loadingMaterial;
        [SerializeField] Material backCoverMaterial;

        PosterCache posters;
        BackCover backCover;

        public LibraryItem Item { get; private set; }
        public TapeGrabInteractable Grab { get; private set; }
        public Material CoverTemplate => coverMaterial;
        public Material LoadingMaterial => loadingMaterial;

        /// <summary>The loaded poster, or null while it loads (or if there is none).</summary>
        public Texture PosterTexture =>
            coverRenderer.sharedMaterial != loadingMaterial ? coverRenderer.sharedMaterial.GetTexture(BaseMapId) : null;

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        /// <summary>Hides a shelved tape that doesn't match the search; tapes in hand or on the floor stay.</summary>
        public void SetFilteredOut(bool filteredOut)
        {
            if (filteredOut && (Grab.isSelected || !Grab.IsOnShelf)) return;
            gameObject.SetActive(!filteredOut);
        }

        void Awake()
        {
            Grab = GetComponent<TapeGrabInteractable>();
            Grab.selectEntered.AddListener(OnGrabbed);
            Grab.selectExited.AddListener(OnReleased);
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

            var cover = await cache.AcquireCoverAsync(item, coverMaterial);
            // The tape may have been destroyed or rebound while the poster loaded.
            if (this != null && Item == item) ShowCover(cover);
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

        void ShowCover(Material cover) => coverRenderer.sharedMaterial = cover != null ? cover : loadingMaterial;
    }
}
