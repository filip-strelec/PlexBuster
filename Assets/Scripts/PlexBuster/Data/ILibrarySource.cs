using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PlexBuster.Data
{
    /// <summary>Where the store's catalogue comes from: a Plex server, or fake data for offline work.</summary>
    public interface ILibrarySource
    {
        string Name { get; }

        /// <summary>The movie and TV sections, available after <see cref="InitializeAsync"/>.</summary>
        IReadOnlyList<LibrarySection> Sections { get; }

        Task InitializeAsync(CancellationToken ct);

        /// <summary>
        /// All values of a filter (every genre, actor, decade...) in one section, or merged across
        /// every section when <paramref name="sectionId"/> is null. Sorted by title.
        /// </summary>
        Task<IReadOnlyList<FilterValue>> GetFilterValuesAsync(FilterType type, string sectionId, CancellationToken ct);

        Task<IReadOnlyList<LibraryItem>> QueryAsync(LibraryQuery query, CancellationToken ct);

        /// <summary>Loads a new poster texture. Callers own it; use <see cref="PosterCache"/> rather than calling this directly.</summary>
        Task<Texture2D> LoadPosterAsync(LibraryItem item, CancellationToken ct);

        /// <summary>Full details for one title: ratings, full cast, crew, media info, trailers and extras.</summary>
        Task<ItemDetails> GetDetailsAsync(LibraryItem item, CancellationToken ct);

        /// <summary>
        /// Loads a new texture for an image path from <see cref="ItemDetails"/> (backdrop, cast photo) or a poster
        /// path, resized to fit <paramref name="width"/> x <paramref name="height"/>. Callers own it.
        /// </summary>
        Task<Texture2D> LoadImageAsync(string path, int width, int height, CancellationToken ct);

        /// <summary>A URL a video player can stream directly (no auth headers needed), or null if unavailable.</summary>
        Task<string> ResolveVideoUrlAsync(ExtraVideo video, CancellationToken ct);

        /// <summary>
        /// Opens the movie or episode <paramref name="key"/> for playback from <paramref name="offsetMs"/>, in a form
        /// Unity's VideoPlayer plays. Null when the source has no video (offline data).
        /// </summary>
        Task<VideoStream> OpenStreamAsync(string key, long offsetMs, CancellationToken ct);

        /// <summary>Ends a stream (stops the server's conversion, if any).</summary>
        void CloseStream(VideoStream stream);

        /// <summary>A subtitle file of a stream, parsed.</summary>
        Task<Subtitles> LoadSubtitlesAsync(SubtitleTrack track, CancellationToken ct);

        /// <summary>Every episode of a show in playing order (specials last), for next/previous episode.</summary>
        Task<IReadOnlyList<EpisodeRef>> GetEpisodesAsync(LibraryItem show, CancellationToken ct);

        /// <summary>
        /// Tells the library how far playback got, so it can be resumed anywhere and counts as watched near the end.
        /// <paramref name="state"/> is "playing", "paused" or "stopped".
        /// </summary>
        Task ReportProgressAsync(VideoStream stream, string state, long positionMs, CancellationToken ct);
    }
}
