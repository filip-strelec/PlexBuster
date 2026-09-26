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
    /// Thin Plex HTTP client. The token goes in a header (never in URLs or logs), JSON responses are
    /// cached on disk and served stale when the server is unreachable, and posters are fetched
    /// through Plex's photo transcoder at a small size and cached on disk.
    /// </summary>
    public class PlexClient
    {
        const int MaxConcurrentRequests = 6;
        const string ClientIdPref = "PlexBuster.ClientId";

        static readonly TimeSpan CacheTtl = TimeSpan.FromHours(12);

        readonly string serverUrl;
        readonly string token;
        readonly string clientId;
        readonly string responseCacheDir;
        readonly string posterCacheDir;
        readonly SemaphoreSlim throttle = new(MaxConcurrentRequests);

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

        internal async Task<PlexContainer> GetAsync(string pathAndQuery, CancellationToken ct)
        {
            var cacheFile = Path.Combine(responseCacheDir, Hash(pathAndQuery) + ".json");
            if (File.Exists(cacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) < CacheTtl)
                return Parse(await File.ReadAllTextAsync(cacheFile, ct));

            string json;
            try
            {
                using var request = UnityWebRequest.Get(serverUrl + pathAndQuery);
                request.SetRequestHeader("Accept", "application/json");
                await SendAsync(request, pathAndQuery, ct);
                json = request.downloadHandler.text;
            }
            catch (Exception e) when (e is not OperationCanceledException && File.Exists(cacheFile))
            {
                Debug.LogWarning($"[Plex] {e.Message} - using cached copy from {File.GetLastWriteTime(cacheFile)}");
                return Parse(await File.ReadAllTextAsync(cacheFile, ct));
            }

            await File.WriteAllTextAsync(cacheFile, json, ct);
            return Parse(json);
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

        /// <summary>Downloads a poster resized by the server, or loads it from the disk cache.</summary>
        public async Task<Texture2D> GetPosterAsync(string thumbPath, int width, int height, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(thumbPath)) return null;

            var cacheFile = Path.Combine(posterCacheDir, $"{Hash(thumbPath)}_{width}x{height}.jpg");
            var cached = File.Exists(cacheFile);
            var url = cached
                ? new Uri(cacheFile).AbsoluteUri
                : $"{serverUrl}/photo/:/transcode?width={width}&height={height}&minSize=1&upscale=1&url={Uri.EscapeDataString(thumbPath)}";

            var textureParams = DownloadedTextureParams.Default;
            // Readable so it can be block-compressed below; the CPU copy is dropped afterwards.
            textureParams.flags = DownloadedTextureFlags.MipmapChain | DownloadedTextureFlags.Readable;
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET, new DownloadHandlerTexture(textureParams), null);
            await SendAsync(request, cached ? cacheFile : thumbPath, ct, authenticate: !cached);

            if (!cached) await File.WriteAllBytesAsync(cacheFile, request.downloadHandler.data, ct);

            var texture = DownloadHandlerTexture.GetContent(request);
            texture.name = thumbPath;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 4;
            PosterTextures.Finish(texture);
            return texture;
        }

        async Task SendAsync(UnityWebRequest request, string label, CancellationToken ct, bool authenticate = true)
        {
            if (authenticate)
            {
                request.SetRequestHeader("X-Plex-Token", token);
                request.SetRequestHeader("X-Plex-Client-Identifier", clientId);
                request.SetRequestHeader("X-Plex-Product", "PlexBuster VR");
            }

            await throttle.WaitAsync(ct);
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
                throttle.Release();
            }
        }

        static PlexContainer Parse(string json) =>
            JsonConvert.DeserializeObject<PlexResponse>(json)?.MediaContainer ?? new PlexContainer();

        static string Hash(string value) => Hash128.Compute(value).ToString();
    }
}
