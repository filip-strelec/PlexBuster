using System;
using System.Collections.Generic;

namespace PlexBuster.Data
{
    public enum MediaKind { Movie, Show }

    public enum FilterType { All, Genre, Actor, Director, Decade, Year, Collection, Studio, ContentRating, RecentlyAdded, Watchlist }

    public enum SortOrder { Title, YearNewest, YearOldest, Rating, RecentlyAdded }

    /// <summary>A library section (a Plex library such as "Movies" or "TV Shows"); each is a department of the store.</summary>
    public class LibrarySection
    {
        public string Id;
        public string Title;
        public MediaKind Kind;

        public override string ToString() => $"{Title} ({Kind})";
    }

    /// <summary>A movie or TV show, independent of which source it came from.</summary>
    [Serializable]
    public class LibraryItem
    {
        public string Id;
        public string Guid;           // source-wide identity (Plex: plex://movie/…), used to match the watchlist
        public string SectionId;
        public MediaKind Kind;
        public string Title;
        public string SortTitle;
        public int Year;
        public string Summary;
        public string Tagline;
        public string ContentRating;
        public string Studio;
        public float Rating;          // critic rating 0-10, 0 = unknown
        public float AudienceRating;  // 0-10, 0 = unknown
        public long DurationMs;
        public long AddedAt;          // unix seconds
        public int SeasonCount;       // shows only
        public int EpisodeCount;      // shows only
        public List<string> Genres = new();
        public List<string> Directors = new();
        public List<string> Actors = new();
        public string PosterPath;     // source-specific poster reference

        public string DisplaySortTitle => string.IsNullOrEmpty(SortTitle) ? Title : SortTitle;
        public float BestRating => Rating > 0 ? Rating : AudienceRating;
    }

    /// <summary>
    /// One value of a filter (e.g. genre "Horror"). Sources with several library sections
    /// merge values by title, so one value can map to a different key in each section.
    /// </summary>
    public class FilterValue
    {
        public FilterType Type;
        public string Title;
        public readonly Dictionary<string, string> KeysBySection = new();

        public override string ToString() => $"{Type}: {Title}";
    }

    public class LibraryQuery
    {
        public FilterType Filter = FilterType.All;
        public FilterValue Value;   // required for every filter except All and RecentlyAdded
        public string SectionId;    // null = every section
        public bool IncludeMovies = true;
        public bool IncludeShows = true;
        public SortOrder Sort = SortOrder.Title;
        public int Limit;           // 0 = no limit

        public static LibraryQuery For(FilterValue value, string sectionId = null, SortOrder sort = SortOrder.Title) =>
            new() { Filter = value.Type, Value = value, SectionId = sectionId, Sort = sort };

        public static LibraryQuery AllOf(string sectionId, SortOrder sort = SortOrder.Title) =>
            new() { Filter = FilterType.All, SectionId = sectionId, Sort = sort };

        public override string ToString()
        {
            var what = Value != null ? Value.ToString() : Filter.ToString();
            return SectionId != null ? $"{what} in section {SectionId} ({Sort})" : $"{what} ({Sort})";
        }
    }

    public static class LibrarySorting
    {
        public static void Apply(List<LibraryItem> items, SortOrder order)
        {
            Comparison<LibraryItem> byTitle = (a, b) =>
                string.Compare(a.DisplaySortTitle, b.DisplaySortTitle, StringComparison.InvariantCultureIgnoreCase);

            items.Sort(order switch
            {
                SortOrder.YearNewest => (a, b) => b.Year != a.Year ? b.Year.CompareTo(a.Year) : byTitle(a, b),
                SortOrder.YearOldest => (a, b) => a.Year != b.Year ? a.Year.CompareTo(b.Year) : byTitle(a, b),
                SortOrder.Rating => (a, b) => b.BestRating != a.BestRating ? b.BestRating.CompareTo(a.BestRating) : byTitle(a, b),
                SortOrder.RecentlyAdded => (a, b) => b.AddedAt.CompareTo(a.AddedAt),
                _ => byTitle,
            });
        }
    }
}
