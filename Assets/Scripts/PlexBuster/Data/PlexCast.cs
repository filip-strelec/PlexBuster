using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PlexBuster.Data
{
    /// <summary>What a Cast device's Plex app needs to stream an item from the server.</summary>
    public class PlexCastRequest
    {
        public string Key;              // /library/metadata/<id>
        public long OffsetMs;
        public long PlayQueueId;
        public string MachineId;        // the server's
        public Uri ServerAddress;       // where the TV streams from
        public string ServerVersion;
        public string Token;
    }

    /// <summary>
    /// Starts Plex playback on a Google Cast device (a TV with Chromecast built in), the way Plex's own apps cast:
    /// launch the Plex receiver, then load a play queue into it. On an Android TV with the Plex app installed the
    /// receiver is that app (Cast Connect), so this works with the new Plex TV app, which dropped the companion
    /// remote-control port.
    /// </summary>
    /// <remarks>
    /// Cast v2 is length-prefixed protobuf CastMessages over TLS on port 8009, carrying JSON. The one message type is
    /// encoded by hand here rather than pulling in a protobuf library. Cast devices present a self-signed
    /// certificate, so it isn't checked (Cast senders authenticate the device separately; that is left out, as the
    /// address comes from the account's own device list).
    /// </remarks>
    public static class PlexCast
    {
        public const int Port = 8009;
        const string PlexAppId = "9AC194DC";
        const string Sender = "sender-0";
        const string Receiver = "receiver-0";
        const string ConnectionNs = "urn:x-cast:com.google.cast.tp.connection";
        const string HeartbeatNs = "urn:x-cast:com.google.cast.tp.heartbeat";
        const string ReceiverNs = "urn:x-cast:com.google.cast.receiver";
        const string MediaNs = "urn:x-cast:com.google.cast.media";

        /// <summary>Completes once the device reports the item playing; throws if it refuses or doesn't start in time.</summary>
        public static async Task PlayAsync(string host, PlexCastRequest request, TimeSpan timeout, CancellationToken ct)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutSource.CancelAfter(timeout);
            try
            {
                using var channel = await Channel.OpenAsync(host, Port, timeoutSource.Token);
                await PlayAsync(channel, request, timeoutSource.Token);
            }
            // Cancelling closes the socket, which surfaces as whatever the pending read or write throws.
            catch (Exception e) when (e is not OperationCanceledException && ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch (Exception) when (timeoutSource.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new TimeoutException("the TV didn't start playing. Check the TV");
            }
            catch (SocketException e)
            {
                throw new IOException($"couldn't reach the TV ({e.Message}). Is it on?", e);
            }
        }

        static async Task PlayAsync(Channel channel, PlexCastRequest request, CancellationToken ct)
        {
            var requestId = 0;
            await channel.SendAsync(Receiver, ConnectionNs, new { type = "CONNECT" }, ct);
            var launchId = ++requestId;
            await channel.SendAsync(Receiver, ReceiverNs, new { type = "LAUNCH", appId = PlexAppId, requestId = launchId }, ct);

            // Launching takes several seconds when Plex isn't already in front.
            string transportId = null, sessionId = null;
            while (transportId == null)
            {
                var (_, ns, message) = await channel.ReceiveAsync(ct);
                if (ns != ReceiverNs) continue;
                var type = (string)message["type"];
                if (type == "LAUNCH_ERROR" || (type == "INVALID_REQUEST" && (int?)message["requestId"] == launchId))
                    throw new InvalidOperationException($"the TV couldn't open Plex ({message["reason"]})");
                if (type != "RECEIVER_STATUS") continue;
                var app = message["status"]?["applications"]?.FirstOrDefault(a => (string)a["appId"] == PlexAppId);
                transportId = (string)app?["transportId"];
                sessionId = (string)app?["sessionId"];
            }

            await channel.SendAsync(transportId, ConnectionNs, new { type = "CONNECT" }, ct);
            var loadId = ++requestId;
            await channel.SendAsync(transportId, MediaNs, LoadMessage(request, loadId, sessionId), ct);

            while (true)
            {
                var (source, ns, message) = await channel.ReceiveAsync(ct);
                var type = (string)message["type"];
                if (source == transportId && ns == ConnectionNs && type == "CLOSE")
                    throw new InvalidOperationException("Plex on the TV closed the connection");
                if (ns != MediaNs) continue;
                if (type == "LOAD_FAILED" || type == "LOAD_CANCELLED" || type == "INVALID_REQUEST" || type == "ERROR")
                {
                    if ((int?)message["requestId"] is int id && id != 0 && id != loadId) continue;
                    throw new InvalidOperationException($"Plex on the TV refused it ({type}{(message["reason"] != null ? $": {message["reason"]}" : "")})");
                }
                if (type != "MEDIA_STATUS") continue;
                // Statuses can still describe whatever was playing before; only ours counts.
                var playing = message["status"]?.Any(s => (string)s["playerState"] == "PLAYING" &&
                    ((string)s["media"]?["contentId"] ?? request.Key) == request.Key) ?? false;
                if (!playing) continue;

                // Plex keeps playing after the sender leaves.
                await channel.SendAsync(transportId, ConnectionNs, new { type = "CLOSE" }, ct);
                return;
            }
        }

        /// <summary>The load message Plex's receiver expects: its media.customData says which server and queue to play.</summary>
        static object LoadMessage(PlexCastRequest request, int requestId, string sessionId)
        {
            var server = request.ServerAddress;
            // Cast times are in seconds.
            var offsetSeconds = request.OffsetMs / 1000.0;
            return new
            {
                type = "LOAD",
                requestId,
                sessionId,
                media = new
                {
                    contentId = request.Key,
                    streamType = "BUFFERED",
                    contentType = "video",
                    customData = new
                    {
                        offset = offsetSeconds,
                        directPlay = true,
                        directStream = true,
                        subtitleSize = 100,
                        audioBoost = 100,
                        server = new
                        {
                            machineIdentifier = request.MachineId,
                            transcoderVideo = true,
                            transcoderVideoRemuxOnly = false,
                            transcoderAudio = true,
                            version = request.ServerVersion,
                            myPlexSubscription = true,
                            isVerifiedHostname = true,
                            protocol = server.Scheme,
                            address = server.Host,
                            port = server.Port,
                            accessToken = request.Token,
                        },
                        containerKey = $"/playQueues/{request.PlayQueueId}?own=1&window=200",
                    },
                },
                autoplay = true,
                currentTime = offsetSeconds,
            };
        }

        /// <summary>A TLS connection to a Cast device, sending and receiving CastMessages.</summary>
        sealed class Channel : IDisposable
        {
            readonly TcpClient tcp;
            readonly SslStream stream;
            readonly byte[] header = new byte[4];

            Channel(TcpClient tcp, SslStream stream)
            {
                this.tcp = tcp;
                this.stream = stream;
            }

            public static async Task<Channel> OpenAsync(string host, int port, CancellationToken ct)
            {
                var tcp = new TcpClient();
                // Socket calls don't all take a token; closing the socket is what unblocks them.
                using var _ = ct.Register(() => tcp.Close());
                try
                {
                    await tcp.ConnectAsync(host, port);
                    var stream = new SslStream(tcp.GetStream(), false, (sender, certificate, chain, errors) => true);
                    await stream.AuthenticateAsClientAsync(host);
                    ct.ThrowIfCancellationRequested();
                    return new Channel(tcp, stream);
                }
                catch
                {
                    tcp.Close();
                    throw;
                }
            }

            public async Task SendAsync(string destination, string ns, object payload, CancellationToken ct)
            {
                var body = Encode(Sender, destination, ns, JsonConvert.SerializeObject(payload));
                var frame = new byte[4 + body.Length];
                WriteBigEndian(frame, body.Length);
                Buffer.BlockCopy(body, 0, frame, 4, body.Length);
                using var _ = ct.Register(() => tcp.Close());
                await stream.WriteAsync(frame, 0, frame.Length, ct);
                await stream.FlushAsync(ct);
            }

            /// <summary>The next message for us, answering the device's heartbeat on the way.</summary>
            public async Task<(string Source, string Namespace, JObject Payload)> ReceiveAsync(CancellationToken ct)
            {
                while (true)
                {
                    using (ct.Register(() => tcp.Close()))
                    {
                        await ReadExactlyAsync(header, ct);
                        var length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                        if (length <= 0 || length > 1 << 20) throw new IOException($"bad Cast message length {length}");
                        var body = new byte[length];
                        await ReadExactlyAsync(body, ct);
                        var (source, destination, ns, payload) = Decode(body);
                        if (payload == null || (destination != Sender && destination != "*")) continue;

                        JObject message;
                        try { message = JObject.Parse(payload); }
                        catch (JsonException) { continue; }

                        if (ns == HeartbeatNs)
                        {
                            if ((string)message["type"] == "PING") await SendAsync(source, HeartbeatNs, new { type = "PONG" }, ct);
                            continue;
                        }
                        return (source, ns, message);
                    }
                }
            }

            async Task ReadExactlyAsync(byte[] buffer, CancellationToken ct)
            {
                for (var read = 0; read < buffer.Length;)
                {
                    var n = await stream.ReadAsync(buffer, read, buffer.Length - read, ct);
                    if (n == 0) throw new IOException("the TV closed the Cast connection");
                    read += n;
                }
            }

            public void Dispose()
            {
                stream.Dispose();
                tcp.Close();
            }

            static void WriteBigEndian(byte[] buffer, int value)
            {
                buffer[0] = (byte)(value >> 24);
                buffer[1] = (byte)(value >> 16);
                buffer[2] = (byte)(value >> 8);
                buffer[3] = (byte)value;
            }

            // CastMessage fields: 1 protocol_version (enum, 0 = CASTV2_1_0), 2 source_id, 3 destination_id,
            // 4 namespace, 5 payload_type (enum, 0 = STRING), 6 payload_utf8, 7 payload_binary.

            static byte[] Encode(string source, string destination, string ns, string payload)
            {
                using var output = new MemoryStream();
                WriteVarintField(output, 1, 0);
                WriteStringField(output, 2, source);
                WriteStringField(output, 3, destination);
                WriteStringField(output, 4, ns);
                WriteVarintField(output, 5, 0);
                WriteStringField(output, 6, payload);
                return output.ToArray();
            }

            static (string Source, string Destination, string Namespace, string Payload) Decode(byte[] body)
            {
                string source = null, destination = null, ns = null, payload = null;
                var position = 0;
                while (position < body.Length)
                {
                    var tag = ReadVarint(body, ref position);
                    var field = (int)(tag >> 3);
                    switch ((int)(tag & 7))
                    {
                        case 0:
                            ReadVarint(body, ref position);
                            break;
                        case 1:
                            position += 8;
                            break;
                        case 2:
                            var length = (int)ReadVarint(body, ref position);
                            if (length < 0 || position + length > body.Length) throw new IOException("bad Cast message");
                            var text = field is >= 2 and <= 4 || field == 6 ? Encoding.UTF8.GetString(body, position, length) : null;
                            if (field == 2) source = text;
                            else if (field == 3) destination = text;
                            else if (field == 4) ns = text;
                            else if (field == 6) payload = text;
                            position += length;
                            break;
                        case 5:
                            position += 4;
                            break;
                        default:
                            throw new IOException("bad Cast message");
                    }
                }
                return (source, destination, ns, payload);
            }

            static void WriteVarintField(Stream output, int field, ulong value)
            {
                WriteVarint(output, (ulong)(field << 3));
                WriteVarint(output, value);
            }

            static void WriteStringField(Stream output, int field, string value)
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                WriteVarint(output, (ulong)((field << 3) | 2));
                WriteVarint(output, (ulong)bytes.Length);
                output.Write(bytes, 0, bytes.Length);
            }

            static void WriteVarint(Stream output, ulong value)
            {
                do
                {
                    var b = (byte)(value & 0x7F);
                    value >>= 7;
                    output.WriteByte(value != 0 ? (byte)(b | 0x80) : b);
                } while (value != 0);
            }

            static ulong ReadVarint(byte[] body, ref int position)
            {
                ulong value = 0;
                for (var shift = 0; shift < 64; shift += 7)
                {
                    if (position >= body.Length) throw new IOException("bad Cast message");
                    var b = body[position++];
                    value |= (ulong)(b & 0x7F) << shift;
                    if ((b & 0x80) == 0) return value;
                }
                throw new IOException("bad Cast message");
            }
        }
    }
}
