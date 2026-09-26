using System.Collections.Generic;

namespace PlexBuster.Data
{
    // Shapes of Plex's JSON responses (Accept: application/json). Only the fields we use.

    internal class PlexResponse
    {
        public PlexContainer MediaContainer;
    }

    internal class PlexContainer
    {
        public int size;
        public int totalSize;
        public int offset;
        public List<PlexMetadata> Metadata;
        public List<PlexDirectory> Directory;
    }

    /// <summary>A library section, or one value of a filter (genre, actor...).</summary>
    internal class PlexDirectory
    {
        public string key;
        public string title;
        public string type;
        public string fastKey;
    }

    internal class PlexMetadata
    {
        public string ratingKey;
        public string type;
        public string title;
        public string titleSort;
        public string summary;
        public string tagline;
        public string contentRating;
        public string studio;
        public int year;
        public float rating;
        public float audienceRating;
        public long duration;
        public long addedAt;
        public int childCount;
        public int leafCount;
        public string thumb;
        public List<PlexTag> Genre;
        public List<PlexTag> Director;
        public List<PlexTag> Role;
    }

    internal class PlexTag
    {
        public string tag;
    }
}
