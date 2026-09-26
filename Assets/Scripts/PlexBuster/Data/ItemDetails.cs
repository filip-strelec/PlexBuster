using System;
using System.Collections.Generic;

namespace PlexBuster.Data
{
    /// <summary>Everything known about one title, for the info hologram. Listings only carry a summary of this.</summary>
    public class ItemDetails
    {
        public LibraryItem Item;
        public string OriginalTitle;
        public DateTime? Released;
        public DateTime? Added;
        public DateTime? LastViewed;
        public int ViewCount;
        public readonly List<ItemRating> Ratings = new();
        public readonly List<string> Genres = new();
        public readonly List<string> Countries = new();
        public readonly List<string> Directors = new();
        public readonly List<string> Writers = new();
        public readonly List<string> Producers = new();
        public readonly List<string> Collections = new();
        public readonly List<CastMember> Cast = new();
        public readonly List<ExtraVideo> Videos = new();   // trailers first, then other extras
        public readonly List<string> Seasons = new();       // shows: "Season 1 · 10 episodes"
        public string SectionTitle;
        public string MediaSummary;                           // e.g. "1080p · H.264 · AAC 5.1 · MKV · 8.2 Mbps"
        public string ArtPath;                                // backdrop image for LoadImageAsync, or null
    }

    public readonly struct ItemRating
    {
        public readonly string Source;   // "IMDb", "Rotten Tomatoes", "TMDB"…
        public readonly string Kind;     // "critic" or "audience"
        public readonly string Display;  // "8.4" or "93%"

        public ItemRating(string source, string kind, string display)
        {
            Source = source;
            Kind = kind;
            Display = display;
        }
    }

    public readonly struct CastMember
    {
        public readonly string Name;
        public readonly string Role;
        public readonly string PhotoPath; // for ILibrarySource.LoadImageAsync, or null

        public CastMember(string name, string role, string photoPath)
        {
            Name = name;
            Role = role;
            PhotoPath = photoPath;
        }
    }

    public class ExtraVideo
    {
        public string Title;
        public string Kind;        // "trailer", "featurette", "behindTheScenes"…
        public long DurationMs;
        public string SourcePath;  // source-specific; resolve with ILibrarySource.ResolveVideoUrlAsync
    }
}
