using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PlexBuster.Data
{
    /// <summary>
    /// A deterministic fake catalogue with generated posters, for building the store without a Plex server.
    /// </summary>
    public class MockLibrarySource : ILibrarySource
    {
        static readonly LibrarySection[] MockSections =
        {
            new() { Id = "mock-movies", Title = "Movies", Kind = MediaKind.Movie },
            new() { Id = "mock-kids", Title = "Kids", Kind = MediaKind.Movie },
            new() { Id = "mock-tv", Title = "TV Shows", Kind = MediaKind.Show },
        };

        static readonly string[] GenreNames =
        {
            "Action", "Adventure", "Animation", "Comedy", "Crime", "Documentary", "Drama", "Family",
            "Fantasy", "Horror", "Mystery", "Romance", "Science Fiction", "Thriller", "War", "Western",
        };

        static readonly string[] FirstNames =
        {
            "Jack", "Linda", "Marco", "Nina", "Otto", "Paula", "Rex", "Sandra", "Tom", "Vera",
            "Walt", "Yvonne", "Bruno", "Clara", "Dean", "Elsa", "Frank", "Greta", "Hank", "Iris",
        };

        static readonly string[] LastNames =
        {
            "Holloway", "Marsh", "Kovac", "Reyes", "Stone", "Fairbanks", "Lindqvist", "Duval",
            "Ashford", "Brennan", "Castellano", "Novak", "Whitaker", "Okafor", "Petrov",
        };

        static readonly string[] Adjectives =
        {
            "Last", "Silent", "Crimson", "Midnight", "Broken", "Golden", "Hidden", "Electric", "Savage",
            "Frozen", "Lost", "Final", "Burning", "Neon", "Wild", "Dark", "Distant", "Iron", "Secret",
        };

        static readonly string[] Nouns =
        {
            "Horizon", "Protocol", "Harvest", "Signal", "Kingdom", "Witness", "Frontier", "Machine",
            "Shadow", "Highway", "Island", "Empire", "Echo", "Summer", "Circuit", "River", "Payback",
        };

        readonly int movieCount;
        readonly int showCount;
        List<LibraryItem> items;
        List<string> actors;
        List<string> directors;

        public MockLibrarySource(int movieCount = 600, int showCount = 40)
        {
            this.movieCount = movieCount;
            this.showCount = showCount;
        }

        public string Name => "Mock";
        public IReadOnlyList<LibrarySection> Sections => MockSections;

        public Task InitializeAsync(CancellationToken ct)
        {
            var rng = new System.Random(1985);
            actors = Enumerable.Range(0, 60).Select(_ => PersonName(rng)).Distinct().ToList();
            directors = Enumerable.Range(0, 20).Select(_ => PersonName(rng)).Distinct().ToList();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            items = new List<LibraryItem>();
            for (var i = 0; i < movieCount + showCount; i++)
            {
                var isShow = i >= movieCount;
                var title = Title(rng);
                var year = rng.Next(1972, 2025);
                // Most movies go to "Movies", the last fifth to "Kids"; shows to "TV Shows".
                var section = isShow ? MockSections[2] : i < movieCount * 4 / 5 ? MockSections[0] : MockSections[1];
                items.Add(new LibraryItem
                {
                    Id = $"mock-{i}",
                    Guid = i % 13 == 0 ? $"mock://watchlist/{i}" : $"mock://{i}",
                    SectionId = section.Id,
                    Kind = section.Kind,
                    Title = title,
                    SortTitle = title.StartsWith("The ") ? title.Substring(4) : title,
                    Year = year,
                    Summary = $"When {Pick(rng, actors)} discovers a {Pick(rng, Nouns).ToLower()} nobody was meant to find, " +
                              "one long night turns into the adventure of a lifetime.",
                    Tagline = $"{Pick(rng, Adjectives)}. {Pick(rng, Adjectives)}. {Pick(rng, Adjectives)}.",
                    ContentRating = Pick(rng, new[] { "G", "PG", "PG-13", "R" }),
                    Studio = Pick(rng, new[] { "Carolco", "Orion", "New Line", "Cannon", "Touchstone" }),
                    Rating = (float)Math.Round(3 + rng.NextDouble() * 6.5, 1),
                    AudienceRating = (float)Math.Round(3 + rng.NextDouble() * 6.5, 1),
                    DurationMs = isShow ? 0 : rng.Next(80, 170) * 60_000L,
                    AddedAt = now - rng.Next(0, 3 * 365 * 24 * 3600),
                    SeasonCount = isShow ? rng.Next(1, 9) : 0,
                    EpisodeCount = isShow ? rng.Next(6, 120) : 0,
                    Genres = GenreNames.OrderBy(_ => rng.Next()).Take(rng.Next(1, 4)).ToList(),
                    Directors = new List<string> { Pick(rng, directors) },
                    Actors = actors.OrderBy(_ => rng.Next()).Take(4).ToList(),
                    PosterPath = $"mock-{i}",
                });
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FilterValue>> GetFilterValuesAsync(FilterType type, string sectionId, CancellationToken ct)
        {
            // Like Plex: a value exists in a section when at least one of its items has it.
            var merged = new SortedDictionary<string, FilterValue>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var item in items)
            {
                if (sectionId != null && item.SectionId != sectionId) continue;
                foreach (var title in ValuesOf(item, type))
                {
                    if (!merged.TryGetValue(title, out var value))
                        merged[title] = value = new FilterValue { Type = type, Title = title };
                    value.KeysBySection[item.SectionId] = title;
                }
            }
            return Task.FromResult<IReadOnlyList<FilterValue>>(merged.Values.ToList());
        }

        static IEnumerable<string> ValuesOf(LibraryItem item, FilterType type) => type switch
        {
            FilterType.Genre => item.Genres,
            FilterType.Actor => item.Actors,
            FilterType.Director => item.Directors,
            FilterType.Decade => new[] { $"{item.Year / 10 * 10}s" },
            FilterType.Year => new[] { item.Year.ToString() },
            FilterType.ContentRating => new[] { item.ContentRating },
            FilterType.Studio => new[] { item.Studio },
            _ => Array.Empty<string>(),
        };

        public Task<IReadOnlyList<LibraryItem>> QueryAsync(LibraryQuery query, CancellationToken ct)
        {
            var title = query.Value?.Title;
            var result = items.Where(i =>
                (query.SectionId == null || i.SectionId == query.SectionId) &&
                (i.Kind == MediaKind.Movie ? query.IncludeMovies : query.IncludeShows) &&
                (query.Filter is FilterType.All or FilterType.RecentlyAdded
                 || (query.Filter == FilterType.Watchlist && i.Guid.StartsWith("mock://watchlist/"))
                 || ValuesOf(i, query.Filter).Contains(title))).ToList();

            LibrarySorting.Apply(result, query.Filter == FilterType.RecentlyAdded ? SortOrder.RecentlyAdded : query.Sort);
            if (query.Limit > 0 && result.Count > query.Limit) result.RemoveRange(query.Limit, result.Count - query.Limit);
            return Task.FromResult<IReadOnlyList<LibraryItem>>(result);
        }

        public async Task<Texture2D> LoadPosterAsync(LibraryItem item, CancellationToken ct)
        {
            await Task.Yield(); // behave like a real async load
            ct.ThrowIfCancellationRequested();
            return GeneratePoster(item);
        }

        /// <summary>Two-tone gradient with a title band, tinted per item so tapes are distinguishable.</summary>
        static Texture2D GeneratePoster(LibraryItem item)
        {
            const int w = 128, h = 192;
            var seed = item.Id.GetHashCode();
            var top = Color.HSVToRGB(Mathf.Abs(seed % 360) / 360f, 0.75f, 0.9f);
            var bottom = Color.HSVToRGB(Mathf.Abs(seed / 360 % 360) / 360f, 0.8f, 0.25f);
            var band = Color.Lerp(top, Color.white, 0.6f);

            var pixels = new Color32[w * h];
            for (var y = 0; y < h; y++)
            {
                var row = Color.Lerp(bottom, top, y / (float)h);
                var inBand = y > h * 0.78f && y < h * 0.9f;
                for (var x = 0; x < w; x++)
                    pixels[y * w + x] = inBand && x > 10 && x < w - 10 ? band : row;
            }

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = item.Title, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            PosterTextures.Finish(texture);
            return texture;
        }

        static string PersonName(System.Random rng) => $"{Pick(rng, FirstNames)} {Pick(rng, LastNames)}";

        static string Title(System.Random rng) => rng.Next(3) switch
        {
            0 => $"The {Pick(rng, Adjectives)} {Pick(rng, Nouns)}",
            1 => $"{Pick(rng, Nouns)} of the {Pick(rng, Adjectives)} {Pick(rng, Nouns)}",
            _ => $"{Pick(rng, Adjectives)} {Pick(rng, Nouns)} {rng.Next(2, 4)}",
        };

        static T Pick<T>(System.Random rng, IReadOnlyList<T> list) => list[rng.Next(list.Count)];
    }
}
