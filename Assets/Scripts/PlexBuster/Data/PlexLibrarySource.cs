using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PlexBuster.Data
{
    /// <summary>
    /// Reads the catalogue from every movie and TV section on a Plex server. Filtering happens on the
    /// server (each filter value has its own listing URL); results from several sections are merged.
    /// </summary>
    public class PlexLibrarySource : ILibrarySource
    {
        static readonly Dictionary<FilterType, string> FilterEndpoints = new()
        {
            [FilterType.Genre] = "genre",
            [FilterType.Actor] = "actor",
            [FilterType.Director] = "director",
            [FilterType.Decade] = "decade",
            [FilterType.Year] = "year",
            [FilterType.Collection] = "collection",
            [FilterType.Studio] = "studio",
            [FilterType.ContentRating] = "contentRating",
        };

        readonly PlexClient client;
        readonly int posterWidth;
        readonly int posterHeight;
        readonly List<(string Id, MediaKind Kind, string Title)> sections = new();
        readonly Dictionary<FilterType, IReadOnlyList<FilterValue>> filterCache = new();

        public PlexLibrarySource(PlexClient client, int posterWidth = 256, int posterHeight = 384)
        {
            this.client = client;
            this.posterWidth = posterWidth;
            this.posterHeight = posterHeight;
        }

        public string Name => "Plex";

        public async Task InitializeAsync(CancellationToken ct)
        {
            sections.Clear();
            var container = await client.GetAsync("/library/sections", ct);
            foreach (var d in container.Directory ?? new List<PlexDirectory>())
            {
                if (d.type == "movie") sections.Add((d.key, MediaKind.Movie, d.title));
                else if (d.type == "show") sections.Add((d.key, MediaKind.Show, d.title));
            }
            Debug.Log($"[Plex] Sections: {string.Join(", ", sections.Select(s => $"{s.Title} ({s.Kind})"))}");
        }

        public async Task<IReadOnlyList<FilterValue>> GetFilterValuesAsync(FilterType type, CancellationToken ct)
        {
            if (filterCache.TryGetValue(type, out var cached)) return cached;
            if (!FilterEndpoints.TryGetValue(type, out var endpoint)) return Array.Empty<FilterValue>();

            var merged = new Dictionary<string, FilterValue>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in sections)
            {
                var container = await client.GetAsync($"/library/sections/{section.Id}/{endpoint}", ct);
                foreach (var d in container.Directory ?? new List<PlexDirectory>())
                {
                    if (string.IsNullOrEmpty(d.title)) continue;
                    if (!merged.TryGetValue(d.title, out var value))
                        merged[d.title] = value = new FilterValue { Type = type, Title = d.title };
                    // fastKey is the ready-made listing URL for this value in this section.
                    value.KeysBySection[section.Id] = !string.IsNullOrEmpty(d.fastKey)
                        ? d.fastKey
                        : $"/library/sections/{section.Id}/all?{endpoint}={Uri.EscapeDataString(d.key)}";
                }
            }

            var values = merged.Values.OrderBy(v => v.Title, StringComparer.OrdinalIgnoreCase).ToList();
            filterCache[type] = values;
            return values;
        }

        public async Task<IReadOnlyList<LibraryItem>> QueryAsync(LibraryQuery query, CancellationToken ct)
        {
            var items = new List<LibraryItem>();
            foreach (var section in sections)
            {
                if (section.Kind == MediaKind.Movie ? !query.IncludeMovies : !query.IncludeShows) continue;

                string path;
                switch (query.Filter)
                {
                    case FilterType.All:
                        path = $"/library/sections/{section.Id}/all";
                        break;
                    case FilterType.RecentlyAdded:
                        path = $"/library/sections/{section.Id}/all?sort=addedAt:desc";
                        break;
                    default:
                        if (query.Value == null || !query.Value.KeysBySection.TryGetValue(section.Id, out path)) continue;
                        break;
                }

                // Per-section limits are only safe when the server already sorts the way we will.
                var sectionLimit = query.Filter == FilterType.RecentlyAdded ? query.Limit : 0;
                foreach (var m in await client.GetAllItemsAsync(path, ct, sectionLimit))
                    items.Add(ToItem(m, section.Kind));
            }

            LibrarySorting.Apply(items, query.Sort);
            if (query.Limit > 0 && items.Count > query.Limit) items.RemoveRange(query.Limit, items.Count - query.Limit);
            return items;
        }

        public Task<Texture2D> LoadPosterAsync(LibraryItem item, CancellationToken ct) =>
            client.GetPosterAsync(item.PosterPath, posterWidth, posterHeight, ct);

        static LibraryItem ToItem(PlexMetadata m, MediaKind kind) => new()
        {
            Id = m.ratingKey,
            Kind = kind,
            Title = m.title,
            SortTitle = m.titleSort,
            Year = m.year,
            Summary = m.summary,
            Tagline = m.tagline,
            ContentRating = m.contentRating,
            Studio = m.studio,
            Rating = m.rating,
            AudienceRating = m.audienceRating,
            DurationMs = m.duration,
            AddedAt = m.addedAt,
            SeasonCount = kind == MediaKind.Show ? m.childCount : 0,
            EpisodeCount = kind == MediaKind.Show ? m.leafCount : 0,
            Genres = Tags(m.Genre),
            Directors = Tags(m.Director),
            // Listings only carry the top few cast members; enough for the back of the box.
            Actors = Tags(m.Role),
            PosterPath = m.thumb,
        };

        static List<string> Tags(List<PlexTag> tags) =>
            tags?.Select(t => t.tag).Where(t => !string.IsNullOrEmpty(t)).ToList() ?? new List<string>();
    }
}
