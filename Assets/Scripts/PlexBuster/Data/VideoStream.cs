using System.Collections.Generic;

namespace PlexBuster.Data
{
    /// <summary>
    /// A movie or episode opened for playback in the store (TV room, cinema): a URL Unity's VideoPlayer can play,
    /// and where in the title it starts. Close it with <see cref="ILibrarySource.CloseStream"/> when done.
    /// </summary>
    public class VideoStream
    {
        /// <summary>Id of what plays (the movie, or the episode), for progress reports.</summary>
        public string Key;
        public string Url;
        /// <summary>Where in the title the stream begins. A converted stream starts at the requested offset.</summary>
        public long StartMs;
        public long DurationMs;
        /// <summary>
        /// True for a file played as it is: seek within the stream. False for a stream Plex converts as it goes:
        /// seeking means opening a new stream at the new offset.
        /// </summary>
        public bool Seekable;
        /// <summary>For the screen: "Direct play · 1080p H.264" or "Converted by Plex · 1080p".</summary>
        public string Description;
        /// <summary>
        /// Text subtitle files the player can show itself, in the library's order. Tracks inside the video file,
        /// and picture subtitles, aren't listed: when one of those is the selected track it is burnt into the picture.
        /// </summary>
        public readonly List<SubtitleTrack> Subtitles = new();
        /// <summary>The track to show at the start (the library's remembered choice), or null for none.</summary>
        public SubtitleTrack DefaultSubtitle;
        /// <summary>True when the library's selected subtitles are burnt into the picture.</summary>
        public bool SubtitlesBurntIn;

        internal string SessionId;
    }

    /// <summary>One episode of a show, in playing order.</summary>
    public class EpisodeRef
    {
        public string Key;
        public string Label;      // "S1 · E3 · Title"
        public long DurationMs;
    }

    public class SubtitleTrack
    {
        public string Id;
        public string Language;   // "hrv", "eng", or null when the file doesn't say
        public string Label;      // "HR", "EN", or a short name for untagged files
        public string Format;     // srt, ass, ssa, vtt, smi
        internal string Path;
    }
}
