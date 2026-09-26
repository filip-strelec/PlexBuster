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
        public string machineIdentifier;   // /identity
        public long playQueueID;           // POST /playQueues
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
        public string guid;
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
        public long viewOffset;            // ms into it where it was left off
        public int index;                  // episode number
        public int parentIndex;            // season number
        public PlexOnDeck OnDeck;          // shows, with includeOnDeck=1: the episode to continue with

        // Single-item details (/library/metadata/{id}). Arrays whose names differ only in case from a field
        // above (Guid/guid, Rating/rating) need their own exact-case field, or Newtonsoft maps the array onto
        // the scalar and fails.
        public List<PlexGuidRef> Guid;     // external ids: imdb://…, tmdb://…, tvdb://…
        public List<PlexRating> Rating;    // every rating source (IMDb, Rotten Tomatoes, TMDB)
        public string ratingImage;         // e.g. rottentomatoes://image.rating.ripe, for `rating`
        public string audienceRatingImage; // for `audienceRating`
        public string originalTitle;
        public string originallyAvailableAt; // "1999-03-31"
        public int viewCount;
        public long lastViewedAt;
        public string art;                 // backdrop
        public string subtype;             // extras: trailer, featurette, behindTheScenes…
        public string extraType;
        public string primaryExtraKey;     // the main trailer's metadata key
        public string librarySectionTitle;
        public List<PlexTag> Writer;
        public List<PlexTag> Producer;
        public List<PlexTag> Country;
        public List<PlexTag> Collection;
        public List<PlexMedia> Media;
        public PlexExtras Extras;          // with includeExtras=1
    }

    internal class PlexGuidRef
    {
        public string id;
    }

    internal class PlexRating
    {
        public string image;               // imdb://image.rating, rottentomatoes://image.rating.ripe, themoviedb://image.rating
        public float value;
        public string type;                // critic or audience
    }

    /// <summary>One version (file set) of a title.</summary>
    internal class PlexMedia
    {
        public long duration;
        public int bitrate;                // kbps
        public int width;
        public int height;
        public string videoResolution;     // 4k, 1080, 720, sd
        public string videoCodec;
        public string videoProfile;
        public string audioCodec;
        public int audioChannels;
        public string container;
        public List<PlexPart> Part;
    }

    internal class PlexPart
    {
        public long id;
        public string key;                 // stream path: /library/parts/… (needs the token) or /services/iva/… (redirects to a CDN)
        public long size;
        public List<PlexStream> Stream;    // single-item details only
    }

    /// <summary>One track of a file: video, audio or subtitles.</summary>
    internal class PlexStream
    {
        public long id;
        public int streamType;             // 1 video, 2 audio, 3 subtitles
        public string codec;               // subtitles: srt, ass, ssa, mov_text, vobsub, pgs…
        public int bitDepth;
        public bool selected;              // the audio/subtitle track the user picked (or the server's default)
        public bool forced;
        public string key;                 // external subtitle files: /library/streams/{id}
        public string languageCode;        // eng, hrv… (often missing)
        public string title;
        public string displayTitle;
    }

    internal class PlexExtras
    {
        public int size;
        public List<PlexMetadata> Metadata;
    }

    internal class PlexOnDeck
    {
        public PlexMetadata Metadata;
    }

    /// <summary>A server or player on the account, from plex.tv's api/v2/resources.</summary>
    internal class PlexResource
    {
        public string clientIdentifier;
        public List<PlexConnection> connections;
    }

    internal class PlexConnection
    {
        public string uri;
        public bool local;
        public bool relay;
        public bool IPv6;
    }

    internal class PlexTag
    {
        public string tag;
        public string role;                // cast: the character played
        public string thumb;               // cast: photo URL
    }
}
