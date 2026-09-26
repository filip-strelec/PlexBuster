using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>A song file for the jukebox, with artist and title read from its name ("Artist - Title.mp3").</summary>
    public class MusicTrack
    {
        // A leading track number ("01 - Fugees - Killing Me Softly", "01. …"), but only when an "Artist - Title"
        // follows, so bands like "112 - Cupid", "4 Non Blondes" and "98 Degrees" keep their names.
        static readonly Regex TrackNumber = new(@"^\d{1,3}\s*[.)-]\s*(?=.+ - )");

        public readonly string Path;
        public readonly AudioType Type;
        public readonly string Artist;
        public readonly string Title;

        public MusicTrack(string path, AudioType type)
        {
            Path = path;
            Type = type;
            var name = TrackNumber.Replace(System.IO.Path.GetFileNameWithoutExtension(path).Replace('_', ' '), "").Trim();
            var dash = name.IndexOf(" - ", StringComparison.Ordinal);
            Artist = dash > 0 ? name.Substring(0, dash).Trim() : "";
            Title = dash > 0 ? name.Substring(dash + 3).Trim() : name;
        }
    }

    /// <summary>
    /// Where the jukebox's songs live: a <c>Music</c> folder next to the project folder (editor) or the .exe
    /// (build), or the folder in the <c>JUKEBOX_DIR</c> environment variable. Subfolders count too.
    /// </summary>
    public static class MusicFolder
    {
        public const string DirVariable = "JUKEBOX_DIR";

        static readonly Dictionary<string, AudioType> Formats = new(StringComparer.OrdinalIgnoreCase)
        {
            [".mp3"] = AudioType.MPEG,
            [".ogg"] = AudioType.OGGVORBIS,
            [".wav"] = AudioType.WAV,
            [".aif"] = AudioType.AIFF,
            [".aiff"] = AudioType.AIFF,
        };

        public static string Location
        {
            get
            {
                var custom = Environment.GetEnvironmentVariable(DirVariable);
                if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim();
                // Application.dataPath is <project>/Assets in the editor and <game>/<name>_Data in a build.
                return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Music");
            }
        }

        /// <summary>Every playable file in the folder, creating the folder if it's missing. Safe off the main thread.</summary>
        public static List<MusicTrack> Scan(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(file => Formats.ContainsKey(Path.GetExtension(file)))
                    .Select(file => new MusicTrack(file, Formats[Path.GetExtension(file)]))
                    .ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Jukebox] Can't read {folder}: {e.Message}");
                return new List<MusicTrack>();
            }
        }

        /// <summary>
        /// A file:// URL with every path segment escaped, so names with spaces, '#' or accents load. Handles
        /// drive paths (C:\…) and network shares (\\nas\music\…).
        /// </summary>
        public static string FileUrl(string path)
        {
            var full = Path.GetFullPath(path).Replace('\\', '/');
            var share = full.StartsWith("//");
            var segments = full.TrimStart('/').Split('/')
                .Select((s, i) => i == 0 && !share ? s : Uri.EscapeDataString(s));
            return (share ? "file://" : "file:///") + string.Join("/", segments);
        }
    }
}
