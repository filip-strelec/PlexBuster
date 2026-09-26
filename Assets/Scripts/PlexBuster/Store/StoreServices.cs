using System;
using System.Threading;
using System.Threading.Tasks;
using PlexBuster.Data;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>
    /// Owns the catalogue source, the poster cache and TV playback for the store. Auto mode uses Plex when
    /// PLEX_URL and PLEX_TOKEN are set (environment or .env) and falls back to fake data otherwise (or if Plex fails).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class StoreServices : MonoBehaviour
    {
        public enum SourceMode { Auto, Plex, Mock }

        [SerializeField] SourceMode sourceMode = SourceMode.Auto;
        [SerializeField] Vector2Int posterSize = new(256, 384);

        readonly TaskCompletionSource<bool> ready = new();
        readonly CancellationTokenSource lifetime = new();

        public static StoreServices Instance { get; private set; }
        public ILibrarySource Library { get; private set; }
        public PosterCache Posters { get; private set; }
        public IRemotePlayback Playback { get; private set; }

        /// <summary>Completes once the library is initialised and usable.</summary>
        /// <remarks>
        /// A method, not a property: editor tooling that reads every property would otherwise block on this
        /// task, which never completes in edit mode, and freeze the editor.
        /// </remarks>
        public Task WhenReady() => ready.Task;
        public CancellationToken LifetimeToken => lifetime.Token;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError($"[Store] More than one {nameof(StoreServices)} in the scene; disabling {name}.", this);
                enabled = false;
                return;
            }
            Instance = this;
            _ = InitializeAsync();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            lifetime.Cancel();
            Posters?.Clear();
            Instance = null;
        }

        async Task InitializeAsync()
        {
            var config = PlexConfig.Load();
            var usePlex = sourceMode == SourceMode.Plex || (sourceMode == SourceMode.Auto && config.IsUsable);

            if (usePlex)
            {
                try
                {
                    var client = new PlexClient(config);
                    await Use(new PlexLibrarySource(client, posterSize.x, posterSize.y),
                        new PlexRemotePlayback(client, config.PreferredPlayer));
                    return;
                }
                catch (Exception e) when (e is not OperationCanceledException && sourceMode == SourceMode.Auto)
                {
                    Debug.LogError($"[Store] Plex unavailable ({e.Message}); falling back to mock data.");
                }
            }
            else if (sourceMode == SourceMode.Auto)
            {
                Debug.Log($"[Store] {PlexConfig.UrlVariable}/{PlexConfig.TokenVariable} not set; using mock data. " +
                          $"Set them in the environment or a {PlexConfig.EnvFileName} file in: " +
                          string.Join(", ", PlexConfig.CandidatePaths()));
            }

            await Use(new MockLibrarySource(), new MockRemotePlayback());
        }

        async Task Use(ILibrarySource source, IRemotePlayback playback)
        {
            await source.InitializeAsync(lifetime.Token);
            Library = source;
            Posters = new PosterCache(source);
            Playback = playback;
            Debug.Log($"[Store] Library source: {source.Name}");
            ready.TrySetResult(true);
        }
    }
}
