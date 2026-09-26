using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PlexBuster.Data
{
    /// <summary>
    /// A tiny HTTP relay on 127.0.0.1 that lets Unity's VideoPlayer stream from the Plex server without the token
    /// in the URL. VideoPlayer can't send headers, and it logs the URLs it plays (warnings and errors quote them),
    /// so it's handed a local URL with a random id instead, and the relay makes the real request with the token in
    /// a header. Range requests pass through, so seeking in a file works as if it were served directly.
    /// </summary>
    public sealed class LocalStreamProxy : IDisposable
    {
        const int MaxHeaderBytes = 16 * 1024;
        static readonly HttpClient Upstream = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
        static LocalStreamProxy instance;

        readonly TcpListener listener;
        readonly ConcurrentDictionary<string, Route> routes = new();
        readonly CancellationTokenSource lifetime = new();

        class Route
        {
            public string Url;
            public Dictionary<string, string> Headers;
            public bool FixMatroska;
        }

        public static LocalStreamProxy Instance => instance ??= new LocalStreamProxy();

        public int Port { get; }

        LocalStreamProxy()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = AcceptLoop();
            Application.quitting += Dispose;
        }

        /// <summary>
        /// A local URL that streams <paramref name="upstreamUrl"/> fetched with <paramref name="headers"/>.
        /// <paramref name="extension"/> (".mkv", ".ts", ".mp4") helps the video decoder pick a demuxer.
        /// </summary>
        public string Register(string upstreamUrl, IReadOnlyDictionary<string, string> headers, string extension)
        {
            var id = Guid.NewGuid().ToString("N");
            routes[id] = new Route
            {
                Url = upstreamUrl,
                Headers = headers.ToDictionary(h => h.Key, h => h.Value),
                FixMatroska = extension == ".mkv",
            };
            return $"http://127.0.0.1:{Port}/{id}{extension}";
        }

        public void Unregister(string localUrl)
        {
            if (string.IsNullOrEmpty(localUrl)) return;
            routes.TryRemove(IdOf(new Uri(localUrl).AbsolutePath), out _);
        }

        public void Dispose()
        {
            if (lifetime.IsCancellationRequested) return;
            lifetime.Cancel();
            listener.Stop();
            routes.Clear();
            if (instance == this) instance = null;
        }

        async Task AcceptLoop()
        {
            while (!lifetime.IsCancellationRequested)
            {
                TcpClient connection;
                try
                {
                    connection = await listener.AcceptTcpClientAsync();
                }
                catch (Exception) when (lifetime.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Stream] Relay stopped accepting: {e.Message}");
                    return;
                }
                _ = Task.Run(() => Serve(connection));
            }
        }

        async Task Serve(TcpClient connection)
        {
            using (connection)
            {
                connection.NoDelay = true;
                var stream = connection.GetStream();
                try
                {
                    var (method, path, requestHeaders) = await ReadRequest(stream);
                    if (method == null) return;
                    if (!routes.TryGetValue(IdOf(path), out var route))
                    {
                        await WriteStatus(stream, 404, "Not Found");
                        return;
                    }
                    await Relay(stream, method, route, requestHeaders);
                }
                catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
                {
                    // The player closed the connection (it does when seeking or stopping).
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Stream] Relay error: {e.Message}");
                }
            }
        }

        async Task Relay(NetworkStream client, string method, Route route, Dictionary<string, string> requestHeaders)
        {
            using var request = new HttpRequestMessage(method == "HEAD" ? HttpMethod.Head : HttpMethod.Get, route.Url);
            foreach (var header in route.Headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (requestHeaders.TryGetValue("range", out var range)) request.Headers.TryAddWithoutValidation("Range", range);

            using var response = await Upstream.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, lifetime.Token);
            var head = new StringBuilder();
            head.Append($"HTTP/1.1 {(int)response.StatusCode} {response.ReasonPhrase}\r\n");
            var content = response.Content.Headers;
            if (content.ContentType != null) head.Append($"Content-Type: {content.ContentType}\r\n");
            if (content.ContentLength.HasValue) head.Append($"Content-Length: {content.ContentLength.Value}\r\n");
            if (content.ContentRange != null) head.Append($"Content-Range: {content.ContentRange}\r\n");
            head.Append(response.Headers.AcceptRanges.Count > 0 ? $"Accept-Ranges: {string.Join(",", response.Headers.AcceptRanges)}\r\n" : "");
            // A live transcode has no length: the body simply runs until the connection closes.
            head.Append("Connection: close\r\n\r\n");
            var bytes = Encoding.ASCII.GetBytes(head.ToString());
            await client.WriteAsync(bytes, 0, bytes.Length, lifetime.Token);
            if (method == "HEAD") return;

            using var body = await response.Content.ReadAsStreamAsync();
            if (route.FixMatroska && response.StatusCode == HttpStatusCode.OK)
            {
                // The header (with the track list) comes first, well inside the first few kilobytes.
                var header = new byte[64 * 1024];
                var length = 0;
                while (length < header.Length)
                {
                    var read = await body.ReadAsync(header, length, header.Length - length, lifetime.Token);
                    if (read == 0) break;
                    length += read;
                    if (length >= 8 * 1024) break;
                }
                MatroskaHeaderFix.Apply(header, length);
                await client.WriteAsync(header, 0, length, lifetime.Token);
            }
            await body.CopyToAsync(client, 256 * 1024, lifetime.Token);
        }

        static async Task<(string Method, string Path, Dictionary<string, string> Headers)> ReadRequest(NetworkStream stream)
        {
            var buffer = new byte[MaxHeaderBytes];
            var length = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer, length, buffer.Length - length);
                if (read == 0) return (null, null, null);
                length += read;
                var text = Encoding.ASCII.GetString(buffer, 0, length);
                var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (end < 0)
                {
                    if (length == buffer.Length) return (null, null, null);
                    continue;
                }

                var lines = text.Substring(0, end).Split(new[] { "\r\n" }, StringSplitOptions.None);
                var parts = lines[0].Split(' ');
                if (parts.Length < 2) return (null, null, null);
                var headers = new Dictionary<string, string>();
                foreach (var line in lines.Skip(1))
                {
                    var colon = line.IndexOf(':');
                    if (colon > 0) headers[line.Substring(0, colon).Trim().ToLowerInvariant()] = line.Substring(colon + 1).Trim();
                }
                return (parts[0].ToUpperInvariant(), parts[1], headers);
            }
        }

        static async Task WriteStatus(NetworkStream stream, int code, string reason)
        {
            var bytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }

        /// <summary>The route id in "/{id}.ext".</summary>
        static string IdOf(string path)
        {
            var name = path.TrimStart('/');
            var dot = name.IndexOf('.');
            return dot < 0 ? name : name.Substring(0, dot);
        }
    }
}
