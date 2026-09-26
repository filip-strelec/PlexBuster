using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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
        readonly List<LibrarySection> sections = new();
        readonly Dictionary<(FilterType, string), IReadOnlyList<FilterValue>> filterCache = new();

        public PlexLibrarySource(PlexClient client, int posterWidth = 256, int posterHeight = 384)
        {
            this.client = client;
            this.posterWidth = posterWidth;
            this.posterHeight = posterHeight;
            Application.quitting += () => quitting = true;
        }

        // Set when play mode ends or the app quits: the last requests (stop the conversion, where playback
        // stopped) must go out before everything is torn down.
        bool quitting;

        public string Name => "Plex";
        public IReadOnlyList<LibrarySection> Sections => sections;

        public async Task InitializeAsync(CancellationToken ct)
        {
            sections.Clear();
            var container = await client.GetAsync("/library/sections", ct);
            foreach (var d in container.Directory ?? new List<PlexDirectory>())
            {
                if (d.type == "movie") sections.Add(new LibrarySection { Id = d.key, Title = d.title, Kind = MediaKind.Movie });
                else if (d.type == "show") sections.Add(new LibrarySection { Id = d.key, Title = d.title, Kind = MediaKind.Show });
            }
            Debug.Log($"[Plex] Sections: {string.Join(", ", sections)}");
        }

        public async Task<IReadOnlyList<FilterValue>> GetFilterValuesAsync(FilterType type, string sectionId, CancellationToken ct)
        {
            if (filterCache.TryGetValue((type, sectionId), out var cached)) return cached;
            if (!FilterEndpoints.TryGetValue(type, out var endpoint)) return Array.Empty<FilterValue>();

            var merged = new Dictionary<string, FilterValue>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in sections)
            {
                if (sectionId != null && section.Id != sectionId) continue;
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

            var values = merged.Values.OrderBy(v => v.Title, StringComparer.InvariantCultureIgnoreCase).ToList();
            filterCache[(type, sectionId)] = values;
            return values;
        }

        public async Task<IReadOnlyList<LibraryItem>> QueryAsync(LibraryQuery query, CancellationToken ct)
        {
            if (query.Filter == FilterType.Watchlist) return await WatchlistAsync(query, ct);

            var items = new List<LibraryItem>();
            foreach (var section in sections)
            {
                if (query.SectionId != null && section.Id != query.SectionId) continue;
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
                    items.Add(ToItem(m, section));
            }

            LibrarySorting.Apply(items, query.Sort);
            if (query.Limit > 0 && items.Count > query.Limit) items.RemoveRange(query.Limit, items.Count - query.Limit);
            return items;
        }

        /// <summary>
        /// The account's watchlist (kept on plex.tv, not the server), narrowed to titles this library has.
        /// Titles are matched by their Plex GUID, so every copy (e.g. original and dubbed) turns up.
        /// </summary>
        async Task<IReadOnlyList<LibraryItem>> WatchlistAsync(LibraryQuery query, CancellationToken ct)
        {
            const string url = "https://discover.provider.plex.tv/library/sections/watchlist/all";
            const int pageSize = 100; // plex.tv rejects larger pages from identified clients (X-Plex-Product)
            var wanted = new HashSet<string>();
            var total = 0;
            while (true)
            {
                var page = await client.GetAccountAsync($"{url}?X-Plex-Container-Start={total}&X-Plex-Container-Size={pageSize}", ct);
                var got = page.Metadata?.Count ?? 0;
                foreach (var m in page.Metadata ?? new List<PlexMetadata>())
                    if (!string.IsNullOrEmpty(m.guid)) wanted.Add(m.guid);
                total += got;
                if (got < pageSize || (page.totalSize > 0 && total >= page.totalSize)) break;
            }

            var all = await QueryAsync(new LibraryQuery
            {
                SectionId = query.SectionId, IncludeMovies = query.IncludeMovies, IncludeShows = query.IncludeShows, Sort = query.Sort,
            }, ct);
            var items = all.Where(i => i.Guid != null && wanted.Contains(i.Guid)).ToList();
            Debug.Log($"[Plex] Watchlist: {total} titles, {items.Select(i => i.Guid).Distinct().Count()} of them in this library");
            if (query.Limit > 0 && items.Count > query.Limit) items.RemoveRange(query.Limit, items.Count - query.Limit);
            return items;
        }

        public Task<Texture2D> LoadPosterAsync(LibraryItem item, CancellationToken ct) =>
            client.GetPosterAsync(item.PosterPath, posterWidth, posterHeight, ct);

        // The server's photo transcoder also fetches and resizes remote images (cast photos live on plex.tv).
        public Task<Texture2D> LoadImageAsync(string path, int width, int height, CancellationToken ct) =>
            client.GetPosterAsync(path, width, height, ct);

        public async Task<ItemDetails> GetDetailsAsync(LibraryItem item, CancellationToken ct)
        {
            // Fresh, so watch counts are current; the cached copy is the fallback when the server is away.
            var container = await client.GetFreshAsync($"/library/metadata/{item.Id}?includeExtras=1", ct);
            var m = container.Metadata?.FirstOrDefault();
            var details = new ItemDetails { Item = item };
            if (m == null) return details;

            var section = sections.FirstOrDefault(s => s.Id == item.SectionId);
            details.SectionTitle = section?.Title ?? m.librarySectionTitle;
            details.OriginalTitle = m.originalTitle != m.title ? m.originalTitle : null;
            details.Released = DateTime.TryParse(m.originallyAvailableAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var released)
                ? released : null;
            details.Added = FromUnix(m.addedAt);
            details.LastViewed = FromUnix(m.lastViewedAt);
            details.ViewCount = m.viewCount;
            details.ArtPath = m.art;
            details.Genres.AddRange(Tags(m.Genre));
            details.Countries.AddRange(Tags(m.Country));
            details.Directors.AddRange(Tags(m.Director));
            details.Writers.AddRange(Tags(m.Writer));
            details.Producers.AddRange(Tags(m.Producer));
            details.Collections.AddRange(Tags(m.Collection));
            foreach (var role in m.Role ?? new List<PlexTag>())
                if (!string.IsNullOrEmpty(role.tag)) details.Cast.Add(new CastMember(role.tag, role.role, role.thumb));

            AddRatings(m, details.Ratings);
            details.MediaSummary = MediaSummary(m.Media);
            AddVideos(m, details.Videos);

            if (item.Kind == MediaKind.Show)
            {
                var seasons = await client.GetAsync($"/library/metadata/{item.Id}/children", ct);
                foreach (var season in seasons.Metadata ?? new List<PlexMetadata>())
                {
                    var episodes = season.leafCount == 1 ? "1 episode" : $"{season.leafCount} episodes";
                    details.Seasons.Add(season.year > 0 ? $"{season.title} · {episodes} · {season.year}" : $"{season.title} · {episodes}");
                }
            }
            return details;
        }

        public async Task<string> ResolveVideoUrlAsync(ExtraVideo video, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(video?.SourcePath)) return null;
            // Online trailers (Plex's trailer service) redirect to a signed CDN URL that needs no token. Extras
            // stored with the library would need the token in the URL, which we don't hand out; they're not listed.
            return await client.ResolveRedirectAsync(video.SourcePath, ct);
        }

        public async Task<VideoStream> OpenStreamAsync(string key, long offsetMs, CancellationToken ct)
        {
            var m = (await client.GetFreshAsync($"/library/metadata/{key}", ct)).Metadata?.FirstOrDefault();
            var media = m?.Media?.FirstOrDefault();
            var part = media?.Part?.FirstOrDefault();
            if (part == null) throw new InvalidOperationException("the server has no file for it");

            var stream = new VideoStream { Key = key, DurationMs = m.duration > 0 ? m.duration : media.duration };
            var headers = StreamHeaders();
            var tracks = part.Stream ?? new List<PlexStream>();

            // Subtitle files next to the video are shown by the player itself. A selected track inside the video
            // file, or a picture-based one, can only be shown by burning it into the picture.
            var files = tracks.Where(t => t.streamType == 3 && !string.IsNullOrEmpty(t.key) && TextSubtitleFormats.Contains(t.codec ?? ""))
                .ToList();
            var selected = tracks.FirstOrDefault(t => t.streamType == 3 && t.selected);
            stream.SubtitlesBurntIn = selected != null && !files.Contains(selected);
            foreach (var file in files)
            {
                var track = ToSubtitleTrack(file, files.Count);
                stream.Subtitles.Add(track);
                if (file == selected) stream.DefaultSubtitle = track;
            }

            // Unity's decoder (Media Foundation) takes 8-bit H.264 with AAC in MP4 as it is. Anything else, or
            // subtitles to burn in, goes through Plex's converter.
            var direct = media.container == "mp4" && media.videoCodec == "h264" && media.audioCodec == "aac" &&
                         !stream.SubtitlesBurntIn && tracks.Where(t => t.streamType == 1).All(t => t.bitDepth <= 8);
            if (direct)
            {
                stream.Url = LocalStreamProxy.Instance.Register(client.ServerUrl + part.key, headers, ".mp4");
                stream.Seekable = true;
                stream.Description = $"Direct play · {Resolution(media)} H.264";
                return stream;
            }

            // Converted as it streams: Matroska over plain HTTP, with the video copied as it is when it's already
            // H.264 and there's nothing to burn in (then only the sound is converted, which costs the server little).
            stream.SessionId = Guid.NewGuid().ToString("N");
            stream.StartMs = Math.Max(0, offsetMs);
            var query = TranscodeQuery(key, stream.StartMs, stream.SessionId, stream.SubtitlesBurntIn);
            await SendAsync($"/video/:/transcode/universal/decision?{query}", "transcode decision", ct);
            stream.Url = LocalStreamProxy.Instance.Register(
                $"{client.ServerUrl}/video/:/transcode/universal/start.mkv?{query}", headers, ".mkv");
            var converted = stream.SubtitlesBurntIn || media.videoCodec != "h264" ? "Converted by Plex" : "Sound converted by Plex";
            stream.Description = $"{converted} · {Resolution(media)} {Codec(media.videoCodec ?? "?")}";
            return stream;
        }

        static readonly HashSet<string> TextSubtitleFormats = new() { "srt", "subrip", "ass", "ssa", "vtt", "webvtt" };

        static readonly Dictionary<string, string> LanguageLabels = new()
        {
            ["hrv"] = "HR", ["scr"] = "HR", ["hr"] = "HR", ["eng"] = "EN", ["en"] = "EN", ["srp"] = "SR", ["scc"] = "SR",
            ["bos"] = "BS", ["slv"] = "SL", ["mkd"] = "MK", ["deu"] = "DE", ["ger"] = "DE", ["fra"] = "FR", ["fre"] = "FR",
            ["ita"] = "IT", ["spa"] = "ES",
        };

        static SubtitleTrack ToSubtitleTrack(PlexStream s, int count)
        {
            var language = string.IsNullOrEmpty(s.languageCode) ? null : s.languageCode.ToLowerInvariant();
            string label;
            if (language != null)
                label = LanguageLabels.TryGetValue(language, out var known) ? known : language.Substring(0, Math.Min(2, language.Length)).ToUpperInvariant();
            else
                label = !string.IsNullOrEmpty(s.title) && s.title.Length <= 8 ? s.title : count == 1 ? "SUBS" : $"SUB {s.id % 100}";
            if (s.forced) label += " (forced)";
            return new SubtitleTrack
            {
                Id = s.id.ToString(CultureInfo.InvariantCulture),
                Language = language,
                Label = label,
                Format = s.codec == "subrip" ? "srt" : s.codec,
                Path = s.key,
            };
        }

        public async Task<Subtitles> LoadSubtitlesAsync(SubtitleTrack track, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, client.ServerUrl + track.Path);
            foreach (var header in StreamHeaders()) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new PlexRequestException($"subtitles {track.Label}", (long)response.StatusCode, response.ReasonPhrase);
            return Subtitles.Parse(await response.Content.ReadAsByteArrayAsync(), track.Format);
        }

        public async Task<IReadOnlyList<EpisodeRef>> GetEpisodesAsync(LibraryItem show, CancellationToken ct)
        {
            var episodes = await client.GetAllItemsAsync($"/library/metadata/{show.Id}/allLeaves", ct);
            return episodes
                .OrderBy(e => e.parentIndex == 0 ? int.MaxValue : e.parentIndex).ThenBy(e => e.index)
                .Select(e => new EpisodeRef
                {
                    Key = e.ratingKey,
                    Label = $"S{e.parentIndex} · E{e.index} · {e.title}",
                    DurationMs = e.duration,
                })
                .ToList();
        }

        public void CloseStream(VideoStream stream)
        {
            if (stream == null) return;
            LocalStreamProxy.Instance.Unregister(stream.Url);
            if (stream.SessionId == null) return;
            var stop = SendAsync($"/video/:/transcode/universal/stop?session={stream.SessionId}", "stop transcode", CancellationToken.None);
            stop.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            WaitIfQuitting(stop);
        }

        public Task ReportProgressAsync(VideoStream stream, string state, long positionMs, CancellationToken ct) =>
            WaitIfQuitting(SendAsync("/:/timeline?" + string.Join("&",
                $"ratingKey={stream.Key}",
                $"key={Uri.EscapeDataString($"/library/metadata/{stream.Key}")}",
                $"state={state}",
                $"time={Math.Max(0, positionMs)}",
                $"duration={stream.DurationMs}",
                stream.SessionId != null ? $"X-Plex-Session-Identifier={stream.SessionId}" : "hasMDE=1"), "timeline", ct));

        Task WaitIfQuitting(Task task)
        {
            if (!quitting) return task;
            try
            {
                task.Wait(TimeSpan.FromSeconds(1.5));
            }
            catch (Exception)
            {
                // Best effort: the server times the session out by itself.
            }
            return task;
        }

        static string TranscodeQuery(string key, long offsetMs, string session, bool burnSubtitles)
        {
            // MPEG-TS and MP4 were tried too: Unity's decoder never finishes preparing a live MPEG-TS stream, and
            // Plex can't stream MP4 while converting.
            var profile = string.Join("+",
                "add-transcode-target(type=videoProfile&context=streaming&protocol=http&container=mkv&videoCodec=h264&audioCodec=aac)",
                "add-limitation(scope=videoCodec&scopeName=h264&type=upperBound&name=video.bitDepth&value=8)",
                "add-limitation(scope=videoAudioCodec&scopeName=aac&type=upperBound&name=audio.channels&value=2)");
            return string.Join("&",
                $"path={Uri.EscapeDataString($"/library/metadata/{key}")}",
                "protocol=http",
                $"offset={(offsetMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)}",
                "directPlay=0", "directStream=1", "directStreamAudio=1",
                "mediaIndex=0", "partIndex=0", "fastSeek=1",
                "videoQuality=100", "maxVideoBitrate=20000", "videoResolution=1920x1080",
                burnSubtitles ? "subtitles=burn" : "subtitles=none",
                "subtitleSize=100", "audioBoost=100", "location=lan",
                $"session={session}", $"X-Plex-Session-Identifier={session}",
                $"X-Plex-Client-Profile-Extra={Uri.EscapeDataString(profile)}");
        }

        /// <summary>Headers for requests made outside <see cref="PlexClient"/> (the stream relay, playback reports).</summary>
        Dictionary<string, string> StreamHeaders() => new()
        {
            ["X-Plex-Token"] = client.Token,
            ["X-Plex-Client-Identifier"] = client.ClientId,
            ["X-Plex-Product"] = "PlexBuster VR",
            ["X-Plex-Platform"] = "Generic",
            ["X-Plex-Device"] = "Windows",
            ["X-Plex-Device-Name"] = "PlexBuster VR",
            ["X-Plex-Version"] = "1.0",
        };

        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

        /// <summary>An uncached request whose answer doesn't matter, only that it worked.</summary>
        async Task SendAsync(string pathAndQuery, string label, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, client.ServerUrl + pathAndQuery);
            foreach (var header in StreamHeaders()) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            // Not resumed on the main thread, so waiting for it while quitting can't deadlock.
            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new PlexRequestException(label, (long)response.StatusCode, response.ReasonPhrase);
        }

        static string Resolution(PlexMedia media) => media.videoResolution switch
        {
            null or "" => "",
            "4k" => "4K",
            "sd" => "SD",
            var r when int.TryParse(r, out _) => r + "p",
            var r => r,
        };

        static void AddRatings(PlexMetadata m, List<ItemRating> ratings)
        {
            if (m.Rating is { Count: > 0 })
            {
                foreach (var r in m.Rating) AddRating(ratings, r.image, r.type, r.value);
            }
            else
            {
                // Older agents only give the two headline ratings.
                AddRating(ratings, m.ratingImage, "critic", m.rating);
                AddRating(ratings, m.audienceRatingImage, "audience", m.audienceRating);
            }
        }

        static void AddRating(List<ItemRating> ratings, string image, string type, float value)
        {
            if (value <= 0 || string.IsNullOrEmpty(image)) return;
            var kind = type == "critic" ? "critic" : "audience";
            if (image.StartsWith("imdb://"))
            {
                ratings.Add(new ItemRating("IMDb", kind, $"{value.ToString("0.0", CultureInfo.InvariantCulture)}/10"));
            }
            else if (image.StartsWith("rottentomatoes://"))
            {
                // ripe/rotten for critics, upright/spilled for audiences.
                var verdict = image.EndsWith(".ripe") ? " fresh" : image.EndsWith(".rotten") ? " rotten" : "";
                ratings.Add(new ItemRating("Rotten Tomatoes", kind, $"{Mathf.RoundToInt(value * 10)}%{verdict}"));
            }
            else if (image.StartsWith("themoviedb://"))
            {
                ratings.Add(new ItemRating("TMDB", kind, $"{Mathf.RoundToInt(value * 10)}%"));
            }
            else
            {
                ratings.Add(new ItemRating(image.Split(':')[0], kind, value.ToString("0.0", CultureInfo.InvariantCulture)));
            }
        }

        static string MediaSummary(List<PlexMedia> media)
        {
            var main = media?.FirstOrDefault();
            if (main == null) return null;

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(main.videoResolution))
                parts.Add(main.videoResolution switch
                {
                    "4k" => "4K",
                    "sd" => "SD",
                    var r when int.TryParse(r, out _) => r + "p",
                    var r => r,
                });
            if (!string.IsNullOrEmpty(main.videoCodec)) parts.Add(Codec(main.videoCodec));
            if (!string.IsNullOrEmpty(main.audioCodec))
                parts.Add(main.audioChannels switch
                {
                    0 => Codec(main.audioCodec),
                    1 => $"{Codec(main.audioCodec)} mono",
                    2 => $"{Codec(main.audioCodec)} stereo",
                    var c => $"{Codec(main.audioCodec)} {c - 1}.1",
                });
            if (!string.IsNullOrEmpty(main.container)) parts.Add(main.container.ToUpperInvariant());
            if (main.bitrate > 0) parts.Add($"{(main.bitrate / 1000f).ToString("0.0", CultureInfo.InvariantCulture)} Mbps");
            var size = main.Part?.Sum(p => p.size) ?? 0;
            if (size > 0) parts.Add($"{(size / 1e9).ToString("0.0", CultureInfo.InvariantCulture)} GB");
            if (media.Count > 1) parts.Add(media.Count == 2 ? "+1 more version" : $"+{media.Count - 1} more versions");
            return string.Join(" · ", parts);
        }

        static string Codec(string codec) => codec.ToLowerInvariant() switch
        {
            "h264" => "H.264",
            "hevc" or "h265" => "HEVC",
            "mpeg4" => "MPEG-4",
            "ac3" => "Dolby Digital",
            "eac3" => "Dolby Digital+",
            "truehd" => "TrueHD",
            "dca" or "dts" => "DTS",
            var c => c.ToUpperInvariant(),
        };

        /// <summary>Online trailers first (the title's main one leading), then the other online extras.</summary>
        static void AddVideos(PlexMetadata m, List<ExtraVideo> videos)
        {
            var extras = m.Extras?.Metadata;
            if (extras == null) return;
            foreach (var extra in extras
                         .OrderBy(e => e.subtype == "trailer" ? 0 : 1)
                         .ThenBy(e => m.primaryExtraKey != null && m.primaryExtraKey.EndsWith("/" + e.ratingKey) ? 0 : 1))
            {
                var path = extra.Media?.FirstOrDefault()?.Part?.FirstOrDefault()?.key;
                // Only online extras can be streamed without the token (see ResolveVideoUrlAsync).
                if (string.IsNullOrEmpty(path) || !path.StartsWith("/services/")) continue;
                videos.Add(new ExtraVideo
                {
                    Title = extra.title,
                    Kind = extra.subtype ?? "extra",
                    DurationMs = extra.duration,
                    SourcePath = path,
                });
            }
        }

        static DateTime? FromUnix(long seconds) =>
            seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime : null;

        static LibraryItem ToItem(PlexMetadata m, LibrarySection section) => new()
        {
            Id = m.ratingKey,
            Guid = m.guid,
            SectionId = section.Id,
            Kind = section.Kind,
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
            SeasonCount = section.Kind == MediaKind.Show ? m.childCount : 0,
            EpisodeCount = section.Kind == MediaKind.Show ? m.leafCount : 0,
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
