using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace PlexBuster.Data
{
    public class PlexRequestException : Exception
    {
        public readonly long StatusCode;

        public PlexRequestException(string path, long statusCode, string error)
            : base($"{path} failed: HTTP {statusCode} {error}") => StatusCode = statusCode;
    }

    /// <summary>
    /// Thin Plex HTTP client. The token goes in a header (never in URLs or logs; the one exception is the play
    /// command a TV needs to stream, sent over the LAN by <see cref="PlexRemotePlayback"/>), JSON responses are
    /// cached on disk and served stale when the server is unreachable, and posters are fetched
    /// through Plex's photo transcoder at a small size and cached on disk.
    /// </summary>
    /// <remarks>
    /// API calls, poster downloads and poster files from the disk cache queue separately: a room of thousands of
    /// posters mustn't hold up pressing Play or opening the next room.
    /// </remarks>
    public class PlexClient
    {
        const string ClientIdPref = "PlexBuster.ClientId";

        static readonly TimeSpan CacheTtl = TimeSpan.FromHours(12);

        readonly string serverUrl;
        readonly string token;
        readonly string clientId;
        readonly string responseCacheDir;
        readonly string posterCacheDir;
        readonly SemaphoreSlim apiThrottle = new(4);
        readonly SemaphoreSlim posterThrottle = new(4);
        readonly SemaphoreSlim diskThrottle = new(8);

        public PlexClient(PlexConfig config)
        {
            serverUrl = config.ServerUrl.Trim().TrimEnd('/');
            token = config.Token.Trim();

            clientId = PlayerPrefs.GetString(ClientIdPref, "");
            if (string.IsNullOrEmpty(clientId))
            {
                clientId = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(ClientIdPref, clientId);
            }

            var cacheRoot = Path.Combine(Application.temporaryCachePath, "plex", Hash(serverUrl));
            responseCacheDir = Path.Combine(cacheRoot, "responses");
            posterCacheDir = Path.Combine(cacheRoot, "posters");
            Directory.CreateDirectory(responseCacheDir);
            Directory.CreateDirectory(posterCacheDir);
        }

        internal string ServerUrl => serverUrl;
        internal string ClientId => clientId;
        /// <summary>Only for handing to a Plex player that must stream from the server (see <see cref="PlexRemotePlayback"/>).</summary>
        internal string Token => token;

        internal Task<PlexContainer> GetAsync(string pathAndQuery, CancellationToken ct) =>
            GetJsonAsync(serverUrl + pathAndQuery, pathAndQuery, CacheTtl, ct);

        /// <summary>Always asks the server (for state that changes, like where a title was left off); the cached copy is only a fallback.</summary>
        internal Task<PlexContainer> GetFreshAsync(string pathAndQuery, CancellationToken ct) =>
            GetJsonAsync(serverUrl + pathAndQuery, pathAndQuery, TimeSpan.Zero, ct);

        internal async Task<PlexContainer> PostAsync(string pathAndQuery, CancellationToken ct)
        {
            using var request = new UnityWebRequest(serverUrl + pathAndQuery, UnityWebRequest.kHttpVerbPOST, new DownloadHandlerBuffer(), null);
            request.SetRequestHeader("Accept", "application/json");
            await SendAsync(request, pathAndQuery, ct);
            return Parse(request.downloadHandler.text);
        }

        /// <summary>An uncached plex.tv response that isn't a MediaContainer in JSON (XML device lists, resource arrays).</summary>
        internal async Task<string> GetAccountTextAsync(string url, string accept, CancellationToken ct)
        {
            using var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Accept", accept);
            await SendAsync(request, url, ct);
            return request.downloadHandler.text;
        }

        /// <summary>
        /// A plex.tv service rather than the server (the watchlist lives on the account). Cached briefly,
        /// since a watchlist changes more often than a library.
        /// </summary>
        internal Task<PlexContainer> GetAccountAsync(string url, CancellationToken ct) =>
            GetJsonAsync(url, url, TimeSpan.FromMinutes(10), ct);

        async Task<PlexContainer> GetJsonAsync(string url, string label, TimeSpan ttl, CancellationToken ct)
        {
            var cacheFile = Path.Combine(responseCacheDir, Hash(label) + ".json");
            if (File.Exists(cacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) < ttl)
                return await ParseAsync(await File.ReadAllTextAsync(cacheFile, ct));

            string json;
            try
            {
                using var request = UnityWebRequest.Get(url);
                request.SetRequestHeader("Accept", "application/json");
                await SendAsync(request, label, ct);
                json = request.downloadHandler.text;
            }
            catch (Exception e) when (e is not OperationCanceledException && File.Exists(cacheFile))
            {
                Debug.LogWarning($"[Plex] {e.Message} - using cached copy from {File.GetLastWriteTime(cacheFile)}");
                return await ParseAsync(await File.ReadAllTextAsync(cacheFile, ct));
            }

            await File.WriteAllTextAsync(cacheFile, json, ct);
            return await ParseAsync(json);
        }

        /// <summary>A page of 500 titles takes tens of milliseconds to parse: off the main thread, so VR doesn't hitch.</summary>
        static Task<PlexContainer> ParseAsync(string json) => Task.Run(() => Parse(json));

        /// <summary>
        /// Where a server path redirects to, without following it: Plex serves online trailers as a redirect to a
        /// signed CDN URL that a video player can stream without the token. Null if the path doesn't redirect.
        /// </summary>
        internal async Task<string> ResolveRedirectAsync(string pathAndQuery, CancellationToken ct)
        {
            using var request = new UnityWebRequest(serverUrl + pathAndQuery, UnityWebRequest.kHttpVerbGET) { redirectLimit = 0 };
            try
            {
                await SendAsync(request, pathAndQuery, ct);
            }
            catch (PlexRequestException)
            {
                // Refusing to follow the redirect counts as a failure; the Location header is still there.
            }
            var location = request.GetResponseHeader("Location");
            return location != null && location.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? location : null;
        }

        /// <summary>Fetches every item behind a listing path, a page at a time.</summary>
        internal async Task<List<PlexMetadata>> GetAllItemsAsync(string path, CancellationToken ct, int limit = 0)
        {
            const int pageSize = 500;
            var items = new List<PlexMetadata>();
            var separator = path.Contains("?") ? "&" : "?";

            while (limit <= 0 || items.Count < limit)
            {
                var size = limit > 0 ? Math.Min(pageSize, limit - items.Count) : pageSize;
                var page = await GetAsync($"{path}{separator}X-Plex-Container-Start={items.Count}&X-Plex-Container-Size={size}", ct);
                var got = page.Metadata?.Count ?? 0;
                if (got > 0) items.AddRange(page.Metadata);
                if (got < size || (page.totalSize > 0 && items.Count >= page.totalSize)) break;
            }
            return items;
        }

        /// <summary>
        /// Downloads a poster resized by the server, or loads it from the disk cache. The cache keeps the JPEG and
        /// the finished texture (compressed, with mipmaps), which loads without decoding or compressing again.
        /// </summary>
        public async Task<Texture2D> GetPosterAsync(string thumbPath, int width, int height, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(thumbPath)) return null;

            var cacheFile = Path.Combine(posterCacheDir, $"{Hash(thumbPath)}_{width}x{height}.jpg");
            var finishedFile = Path.ChangeExtension(cacheFile, ".tex");
            if (File.Exists(finishedFile))
            {
                var finished = await LoadFinishedAsync(finishedFile, thumbPath, ct);
                if (finished != null) return finished;
            }

            var cached = File.Exists(cacheFile);
            var url = cached
                ? new Uri(cacheFile).AbsoluteUri
                : $"{serverUrl}/photo/:/transcode?width={width}&height={height}&minSize=1&upscale=1&url={Uri.EscapeDataString(thumbPath)}";

            var textureParams = DownloadedTextureParams.Default;
            // Readable so it can be block-compressed below; the CPU copy is dropped afterwards.
            textureParams.flags = DownloadedTextureFlags.MipmapChain | DownloadedTextureFlags.Readable;
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET, new DownloadHandlerTexture(textureParams), null);
            await SendAsync(request, cached ? cacheFile : thumbPath, ct, authenticate: !cached, cached ? diskThrottle : posterThrottle);

            if (!cached) await File.WriteAllBytesAsync(cacheFile, request.downloadHandler.data, ct);

            // Making the GPU texture (block compression, upload) is main-thread work: only as much per frame as
            // the loading budget allows, or a room's worth of posters arriving from the disk cache hitches VR.
            await FrameBudget.WaitAsync(ct);
            Texture2D texture;
            byte[] bytes;
            using (FrameBudget.Measure())
            {
                texture = DownloadHandlerTexture.GetContent(request);
                SetUp(texture, thumbPath);
                bytes = PosterTextures.FinishForCache(texture);
            }
            if (bytes != null) _ = SaveFinishedAsync(finishedFile, bytes);
            return texture;
        }

        async Task<Texture2D> LoadFinishedAsync(string file, string name, CancellationToken ct)
        {
            byte[] bytes;
            await diskThrottle.WaitAsync(ct);
            try
            {
                bytes = await File.ReadAllBytesAsync(file, ct);
            }
            catch (IOException)
            {
                return null;
            }
            finally
            {
                diskThrottle.Release();
            }

            await FrameBudget.WaitAsync(ct);
            using (FrameBudget.Measure())
            {
                var texture = PosterTextures.FromCache(bytes);
                if (texture == null)
                {
                    // Unreadable: the caller makes it again from the JPEG.
                    TryDelete(file);
                    return null;
                }
                SetUp(texture, name);
                return texture;
            }
        }

        /// <summary>Written under a temporary name first, so a half-written file is never taken for a finished one.</summary>
        static async Task SaveFinishedAsync(string file, byte[] bytes)
        {
            var temp = $"{file}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temp, bytes);
                File.Move(temp, file);
            }
            catch (Exception e)
            {
                // Most likely another load of the same poster saved it first.
                TryDelete(temp);
                if (e is not IOException) Debug.LogWarning($"[Plex] Couldn't cache a poster: {e.Message}");
            }
        }

        static void TryDelete(string file)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static void SetUp(Texture2D texture, string name)
        {
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 4;
        }

        /// <param name="limit">The queue the request waits in; API calls by default.</param>
        async Task SendAsync(UnityWebRequest request, string label, CancellationToken ct, bool authenticate = true, SemaphoreSlim limit = null)
        {
            if (authenticate)
            {
                request.SetRequestHeader("X-Plex-Token", token);
                request.SetRequestHeader("X-Plex-Client-Identifier", clientId);
                request.SetRequestHeader("X-Plex-Product", "PlexBuster VR");
            }

            limit ??= apiThrottle;
            await limit.WaitAsync(ct);
            try
            {
                using (ct.Register(request.Abort))
                    await request.SendWebRequest();
                ct.ThrowIfCancellationRequested();
                if (request.result != UnityWebRequest.Result.Success)
                    throw new PlexRequestException(label, request.responseCode, request.error);
            }
            finally
            {
                limit.Release();
            }
        }

        static PlexContainer Parse(string json) =>
            JsonConvert.DeserializeObject<PlexResponse>(json)?.MediaContainer ?? new PlexContainer();

        static string Hash(string value) => Hash128.Compute(value).ToString();
    }
}
