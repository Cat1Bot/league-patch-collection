using System.IO.Compression;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Text;
using System.Diagnostics;

namespace LeaguePatchCollection
{
    public partial class RMSProxy
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;

        public async Task RunAsync(CancellationToken token)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _listener = new TcpListener(IPAddress.Loopback, LeagueProxy.RmsPort);
            _listener.Start();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    _ = HandleClient(client, token);
                }
            }
            catch (Exception) { }
            finally
            {
                Stop();
            }
        }

        private static async Task HandleWebSocketHandshakeAsync(Stream clientStream, Stream serverStream)
        {
            var clientReader = new StreamReader(clientStream, Encoding.ASCII);
            var clientWriter = new StreamWriter(clientStream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };
            var serverWriter = new StreamWriter(serverStream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };

            string? requestLine = await clientReader.ReadLineAsync();
            await serverWriter.WriteLineAsync(requestLine);

            while (true)
            {
                string? header = await clientReader.ReadLineAsync();
                if (string.IsNullOrEmpty(header)) break;

                if (header.StartsWith("Host: "))
                {
                    header = $"Host: {ConfigProxy.RmsHost!.Replace("wss://", "")}";
                }
                else if (header.StartsWith("Origin: "))
                {
                    header = $"Origin: https://{ConfigProxy.RmsHost!.Replace("wss://", "")}:443/";
                }

                await serverWriter.WriteLineAsync(header);
            }
            await serverWriter.WriteLineAsync();

            var serverReader = new StreamReader(serverStream, Encoding.ASCII);

            string? responseLine = await serverReader.ReadLineAsync();
            await clientWriter.WriteLineAsync(responseLine ?? string.Empty);

            while (true)
            {
                string? header = await serverReader.ReadLineAsync();
                if (string.IsNullOrEmpty(header)) break;

                await clientWriter.WriteLineAsync(header);
            }
            await clientWriter.WriteLineAsync();
        }

        private static async Task HandleClient(TcpClient client, CancellationToken token)
        {
            try
            {
                var rmsHost = ConfigProxy.RmsHost?.Replace("wss://", "");

                using var tcpClient = new TcpClient(rmsHost!, 443);
                Stream serverStream = tcpClient.GetStream();

                var sslStream = new SslStream(serverStream, false, (sender, certificate, chain, sslPolicyErrors) => true);
                await sslStream.AuthenticateAsClientAsync(rmsHost!);
                serverStream = sslStream;

                await HandleWebSocketHandshakeAsync(client.GetStream(), serverStream);

                var clientToServerTask = ForwardClientToServerAsync(client.GetStream(), serverStream, token);
                var serverToClientTask = ForwardServerToClientAsync(serverStream, client.GetStream(), token);

                await Task.WhenAny(clientToServerTask, serverToClientTask);
            }
            catch (Exception){ }
        }

        private static async Task ForwardClientToServerAsync(Stream source, Stream destination, CancellationToken token)
        {
            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await source.ReadAsync(buffer, token)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), token);
            }
        }

        private static (int headerLen, int payloadLen, bool isMasked, byte[] maskKey)
            ParseWebSocketHeader(byte[] buffer, int bytesRead)
        {
            int offset = 0;
            byte b1 = buffer[offset++];
            byte b2 = buffer[offset++];

            bool isMasked = (b2 & 0b1000_0000) != 0;
            int payloadLen = b2 & 0b0111_1111;

            if (payloadLen == 126)
            {
                payloadLen = (buffer[offset++] << 8) | buffer[offset++];
            }
            else if (payloadLen == 127)
            {
                payloadLen = (int)(
                    ((long)buffer[offset++] << 56) |
                    ((long)buffer[offset++] << 48) |
                    ((long)buffer[offset++] << 40) |
                    ((long)buffer[offset++] << 32) |
                    ((long)buffer[offset++] << 24) |
                    ((long)buffer[offset++] << 16) |
                    ((long)buffer[offset++] << 8) |
                     buffer[offset++]
                );
            }

            byte[] maskKey = [];
            if (isMasked)
            {
                maskKey = [.. buffer.Skip(offset).Take(4)];
                offset += 4;
            }

            return (offset, payloadLen, isMasked, maskKey);
        }

        private static byte[] TryDecompressGzip(byte[] payload)
        {
            using var ms = new MemoryStream(payload);
            using var gzip = new GZipStream(ms, CompressionMode.Decompress);
            using var result = new MemoryStream();
            gzip.CopyTo(result);
            return result.ToArray();
        }

        private static byte[] BuildWebSocketFrame(byte originalB1, byte[] payload)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(originalB1);

            if (payload.Length <= 125)
            {
                ms.WriteByte((byte)payload.Length);
            }
            else if (payload.Length <= ushort.MaxValue)
            {
                ms.WriteByte(126);
                ms.WriteByte((byte)(payload.Length >> 8));
                ms.WriteByte((byte)(payload.Length));
            }
            else
            {
                ms.WriteByte(127);
                for (int i = 7; i >= 0; i--)
                {
                    ms.WriteByte((byte)(payload.Length >> (8 * i)));
                }
            }

            ms.Write(payload, 0, payload.Length);
            return ms.ToArray();
        }

        private static async Task ForwardServerToClientAsync(Stream source, Stream destination, CancellationToken token)
        {
            var buffer = new byte[8192];

            while (true)
            {
                int bytesRead = await source.ReadAsync(buffer, token);
                if (bytesRead == 0) break;

                byte originalB1 = buffer[0];

                var (headerLen, payloadLen, isMasked, maskKey) = ParseWebSocketHeader(buffer, bytesRead);
                int frameLen = headerLen + payloadLen;

                while (bytesRead < frameLen)
                {
                    int extra = await source.ReadAsync(buffer.AsMemory(bytesRead, buffer.Length - bytesRead), token);
                    if (extra == 0) break;
                    bytesRead += extra;
                }

                int suffixLen = Math.Max(0, bytesRead - frameLen);
                var suffix = suffixLen > 0
                           ? buffer.AsMemory(frameLen, suffixLen)
                           : default;

                byte[] payload = [.. buffer.Skip(headerLen).Take(payloadLen)];
                if (isMasked)
                    for (int i = 0; i < payload.Length; i++)
                        payload[i] ^= maskKey[i % 4];

                byte[] newPayload;
                try
                {
                    if (payload.Length >= 2 && payload[0] == 0x1F && payload[1] == 0x8B)
                    {
                        newPayload = TryDecompressGzip(payload);
                        string txt = Encoding.UTF8.GetString(newPayload);

                        if (RankedRestriction().IsMatch(txt))
                        {
                            Trace.WriteLine(" [INFO] Supressed forced rank restriction popup.");
                            continue;
                        }
                        else if (NoClientClose().IsMatch(txt))
                        {
                            Trace.WriteLine(" [INFO] Prevented client forced shutdown due to Vanguard detection while in-game.");
                            continue;
                        }
                        else if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.AutoAccept && AfkCheck().IsMatch(txt))
                        {
                            txt = AutoAccept().Replace(txt, "$1true"
);
                            Trace.WriteLine($" [INFO] auto-accepted queue.");
                        }

                        newPayload = Encoding.UTF8.GetBytes(txt);
                    }
                    else
                    {
                        string orig = Encoding.UTF8.GetString(payload);
                        if (GzipFind().IsMatch(orig))
                        {
                            string replaced = DisableCompression().Replace(orig, "\"enabled\":\"false\""
);
                            newPayload = Encoding.UTF8.GetBytes(replaced);
                        }

                        else
                        {
                            newPayload = payload;
                        }
                    }
                }
                catch
                {
                    newPayload = payload;
                }

                byte[] newFrame = BuildWebSocketFrame(originalB1, newPayload);

                await destination.WriteAsync(newFrame, token);

                if (suffixLen > 0)
                    await destination.WriteAsync(suffix, token);
            }
        }
    
        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;
        }

        [GeneratedRegex(@"RANKED_RESTRICTED")]
        private static partial Regex RankedRestriction();
        [GeneratedRegex(@"GAMEFLOW_EVENT.PLAYER_KICKED.VANGUARD")]
        private static partial Regex NoClientClose();
        [GeneratedRegex(@"AFK_CHECK")]
        private static partial Regex AfkCheck();
        [GeneratedRegex(@"(\\?""autoAccept\\?""\s*:\s*)false")]
        private static partial Regex AutoAccept();
        [GeneratedRegex(@"""subject""\s*:\s*""rms:gzip""")]
        private static partial Regex GzipFind();
        [GeneratedRegex(@"""enabled""\s*:\s*""true""")]
        private static partial Regex DisableCompression();
    }
}