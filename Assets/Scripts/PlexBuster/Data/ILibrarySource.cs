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

        Task InitializeAsync(CancellationToken ct);

        /// <summary>All values of a filter (every genre, actor, decade...), sorted by title.</summary>
        Task<IReadOnlyList<FilterValue>> GetFilterValuesAsync(FilterType type, CancellationToken ct);

        Task<IReadOnlyList<LibraryItem>> QueryAsync(LibraryQuery query, CancellationToken ct);

        /// <summary>Loads a new poster texture. Callers own it; use <see cref="PosterCache"/> rather than calling this directly.</summary>
        Task<Texture2D> LoadPosterAsync(LibraryItem item, CancellationToken ct);
    }
}
