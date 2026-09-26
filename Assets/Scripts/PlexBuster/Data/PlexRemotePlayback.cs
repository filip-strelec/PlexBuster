using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace PlexBuster.Data
{
    /// <summary>
    /// Plays library items on Plex apps that accept remote control (a TV's Plex app with "Advertise as player"
    /// on), with Plex's remote-control ("companion") API: create a play queue on the server, then tell the player
    /// to play it.
    /// </summary>
    /// <remarks>
    /// Players are found through the account's device list: every non-server device's address is asked
    /// whether it is a player. Plex's LAN discovery (GDM) doesn't help here: the Plex app on an Android TV on
    /// Ethernet doesn't answer it, not even the server's own searches, so the server can't relay commands to the
    /// TV either; they go to the player directly. Player endpoints are plain HTTP, which UnityWebRequest refuses
    /// under this project's settings, hence HttpClient for them.
    /// </remarks>
    public class PlexRemotePlayback : IRemotePlayback
    {
        const string DevicesUrl = "https://plex.tv/devices.xml";
        const string ResourcesUrl = "https://plex.tv/api/v2/resources?includeHttps=1";
        static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
        static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
        // A player takes a few seconds to start (paused, buffering, then playing).
        static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

        static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

        readonly PlexClient client;
        readonly string preferred;
        (string MachineId, Uri Address)? server;
        int commandId;

        /// <param name="preferredPlayer">Name, model, id or address (or part of one) of the player to put first.</param>
        public PlexRemotePlayback(PlexClient client, string preferredPlayer)
        {
            this.client = client;
            preferred = preferredPlayer?.Trim() ?? "";
        }

        public async Task<IReadOnlyList<RemotePlayer>> FindPlayersAsync(CancellationToken ct)
        {
            var devices = XDocument.Parse(await client.GetAccountTextAsync(DevicesUrl, "application/xml", ct)).Root;
            // "provides" is only updated when a device checks in, so a TV that was just switched to advertise
            // as a player may still say "controller": ask every device with an address, except servers.
            var candidates = devices?.Elements("Device")
                .Where(d => !((string)d.Attribute("provides") ?? "").Contains("server"))
                .SelectMany(d => d.Elements("Connection").Select(c => (Device: d, Address: ((string)c.Attribute("uri"))?.TrimEnd('/'))))
                .Where(c => !string.IsNullOrEmpty(c.Address))
                .GroupBy(c => c.Address).Select(g => g.First())
                .ToList() ?? new();

            var found = await Task.WhenAll(candidates.Select(c => ProbeAsync(c.Address, c.Device, ct)));
            return found.Where(p => p != null)
                .GroupBy(p => p.Id).Select(g => g.First())
                .OrderByDescending(p => p.Preferred).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>The player at <paramref name="address"/>, or null if it's offline, elsewhere, or not a player.</summary>
        async Task<RemotePlayer> ProbeAsync(string address, XElement device, CancellationToken ct)
        {
            try
            {
                var response = await SendToPlayerAsync($"{address}/resources", null, ProbeTimeout, $"{address}/resources", ct);
                var player = XDocument.Parse(response).Root?.Element("Player");
                if (player == null || !((string)player.Attribute("protocolCapabilities") ?? "").Contains("playback")) return null;

                var name = (string)player.Attribute("title") ?? (string)device.Attribute("name");
                var model = string.Join(" ", new[] { (string)device.Attribute("vendor"), (string)device.Attribute("model") }
                    .Where(s => !string.IsNullOrEmpty(s)));
                var result = new RemotePlayer
                {
                    Id = (string)player.Attribute("machineIdentifier"),
                    Name = name,
                    Details = string.Join(" · ", new[] { model, new Uri(address).Host }.Where(s => s.Length > 0)),
                    Address = address,
                };
                var product = (string)player.Attribute("product") ?? "";
                result.Preferred = preferred.Length > 0 && new[] { name, product, model, result.Id, address }
                    .Any(s => s != null && s.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0);
                return result;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return null;
            }
        }

        public async Task<PlaybackPlan> PlanAsync(LibraryItem item, CancellationToken ct)
        {
            // Fresh rather than cached: where a title was left off changes with every viewing.
            if (item.Kind == MediaKind.Movie)
            {
                var movie = (await client.GetFreshAsync($"/library/metadata/{item.Id}", ct)).Metadata?.FirstOrDefault();
                return new PlaybackPlan { Item = item, Key = item.Id, ResumeMs = movie?.viewOffset ?? 0 };
            }

            // A show continues where Plex's On Deck says (the episode in progress, or the next unwatched one);
            // a finished show starts again from its first episode.
            var show = (await client.GetFreshAsync($"/library/metadata/{item.Id}?includeOnDeck=1", ct)).Metadata?.FirstOrDefault();
            var episode = show?.OnDeck?.Metadata;
            if (episode == null)
            {
                var episodes = (await client.GetFreshAsync($"/library/metadata/{item.Id}/allLeaves?X-Plex-Container-Start=0&X-Plex-Container-Size=50", ct)).Metadata;
                // Specials are season 0 and sort first.
                episode = episodes?.FirstOrDefault(e => e.parentIndex > 0) ?? episodes?.FirstOrDefault();
            }
            if (episode == null) throw new InvalidOperationException($"{item.Title} has no episodes");

            return new PlaybackPlan
            {
                Item = item,
                Key = episode.ratingKey,
                EpisodeLabel = $"S{episode.parentIndex} · E{episode.index} · {episode.title}",
                ResumeMs = episode.viewOffset,
            };
        }

        public async Task PlayAsync(RemotePlayer player, PlaybackPlan plan, bool resume, CancellationToken ct)
        {
            var (machineId, address) = await GetServerAsync(ct);

            // A show's queue carries on into the following episodes.
            var continuous = plan.Item.Kind == MediaKind.Show ? 1 : 0;
            var itemUri = $"server://{machineId}/com.plexapp.plugins.library/library/metadata/{plan.Key}";
            var queue = await client.PostAsync(
                $"/playQueues?type=video&uri={Uri.EscapeDataString(itemUri)}&shuffle=0&repeat=0&continuous={continuous}&own=1", ct);
            if (queue.playQueueID == 0) throw new InvalidOperationException("the server didn't create a play queue");

            var key = $"/library/metadata/{plan.Key}";
            var query = string.Join("&",
                $"key={Uri.EscapeDataString(key)}",
                $"offset={(resume ? plan.ResumeMs : 0)}",
                $"machineIdentifier={machineId}",
                $"protocol={address.Scheme}",
                $"address={Uri.EscapeDataString(address.Host)}",
                $"port={address.Port}",
                $"containerKey={Uri.EscapeDataString($"/playQueues/{queue.playQueueID}?own=1&window=200")}",
                "type=video",
                $"commandID={++commandId}",
                // The player streams with it; it's a LAN request and never logged.
                $"token={Uri.EscapeDataString(client.Token)}");
            await SendToPlayerAsync($"{player.Address}/player/playback/playMedia?{query}", player.Id, CommandTimeout,
                $"{player.Name}: play", ct);

            await WaitUntilPlayingAsync(player, key, ct);
            Debug.Log($"[Playback] {player.Name} is playing {plan.Item.Title}" + (plan.EpisodeLabel != null ? $" {plan.EpisodeLabel}" : ""));
        }

        async Task WaitUntilPlayingAsync(RemotePlayer player, string key, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + StartTimeout;
            while (true)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) break;
                string response;
                try
                {
                    // Long-poll: answers as soon as the player's state changes.
                    response = await SendToPlayerAsync($"{player.Address}/player/timeline/poll?wait=1&commandID={++commandId}",
                        player.Id, remaining, $"{player.Name}: timeline", ct);
                }
                catch (TimeoutException)
                {
                    break;
                }

                var video = XDocument.Parse(response).Root?.Elements("Timeline")
                    .FirstOrDefault(t => (string)t.Attribute("type") == "video");
                if ((string)video?.Attribute("state") == "playing" && (string)video.Attribute("key") == key) return;
            }
            throw new TimeoutException("it took the title but hasn't started playing. Check the TV");
        }

        /// <summary>
        /// The server's id, and the address the player should stream from: its LAN connection (a plex.direct name
        /// with a valid certificate, as Plex's own apps use), falling back to PLEX_URL.
        /// </summary>
        async Task<(string MachineId, Uri Address)> GetServerAsync(CancellationToken ct)
        {
            if (server != null) return server.Value;

            var machineId = (await client.GetAsync("/identity", ct)).machineIdentifier;
            var address = new Uri(client.ServerUrl);
            try
            {
                var resources = JsonConvert.DeserializeObject<List<PlexResource>>(
                    await client.GetAccountTextAsync(ResourcesUrl, "application/json", ct));
                var connection = resources?.FirstOrDefault(r => r.clientIdentifier == machineId)?.connections?
                    .Where(c => !c.relay && !string.IsNullOrEmpty(c.uri))
                    .OrderByDescending(c => c.local).ThenBy(c => c.IPv6)
                    .FirstOrDefault();
                if (connection != null) address = new Uri(connection.uri);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Debug.LogWarning($"[Playback] Couldn't look up the server's LAN address ({e.Message}); players will use {address.Host}");
            }

            server = (machineId, address);
            return server.Value;
        }

        /// <param name="label">What to call the request in errors: the URL can hold the token.</param>
        async Task<string> SendToPlayerAsync(string url, string targetId, TimeSpan timeout, string label, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Plex-Client-Identifier", client.ClientId);
            request.Headers.Add("X-Plex-Product", "PlexBuster VR");
            request.Headers.Add("X-Plex-Device-Name", "PlexBuster VR");
            if (targetId != null) request.Headers.Add("X-Plex-Target-Client-Identifier", targetId);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutSource.CancelAfter(timeout);
            try
            {
                using var response = await Http.SendAsync(request, timeoutSource.Token);
                // Only the status counts: Plex for Android answers a play command that worked with "Failure: 200 OK".
                if (!response.IsSuccessStatusCode)
                    throw new PlexRequestException(label, (long)response.StatusCode, response.ReasonPhrase);
                return await response.Content.ReadAsStringAsync();
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"{label}: no answer");
            }
        }
    }
}
