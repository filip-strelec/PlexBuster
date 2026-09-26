using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlexBuster.Data
{
    /// <summary>
    /// Ranks a filter's values in a section by how many of the section's titles carry them. Library listings
    /// only include the top-billed cast, so for actors this counts leading roles, which is what you browse by.
    /// </summary>
    public static class FilterRanking
    {
        public readonly struct Ranked
        {
            public readonly FilterValue Value;
            public readonly int Count;

            public Ranked(FilterValue value, int count)
            {
                Value = value;
                Count = count;
            }
        }

        static readonly Dictionary<(ILibrarySource, FilterType, string), IReadOnlyList<Ranked>> Cache = new();

        public static async Task<IReadOnlyList<Ranked>> RankAsync(ILibrarySource source, FilterType type, string sectionId, CancellationToken ct)
        {
            if (Cache.TryGetValue((source, type, sectionId), out var cached)) return cached;

            var values = await source.GetFilterValuesAsync(type, sectionId, ct);
            var items = await source.QueryAsync(LibraryQuery.AllOf(sectionId), ct);

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            foreach (var tag in TagsOf(item, type))
                counts[tag] = counts.TryGetValue(tag, out var n) ? n + 1 : 1;

            IReadOnlyList<Ranked> ranked = values
                .Select(v => new Ranked(v, counts.TryGetValue(v.Title, out var n) ? n : 0))
                .Where(r => r.Count > 0)
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.Value.Title, StringComparer.InvariantCultureIgnoreCase)
                .ToList();
            Cache[(source, type, sectionId)] = ranked;
            return ranked;
        }

        static IEnumerable<string> TagsOf(LibraryItem item, FilterType type) => type switch
        {
            FilterType.Actor => item.Actors,
            FilterType.Director => item.Directors,
            FilterType.Genre => item.Genres,
            FilterType.Studio => string.IsNullOrEmpty(item.Studio) ? Array.Empty<string>() : new[] { item.Studio },
            _ => Array.Empty<string>(),
        };
    }
}
