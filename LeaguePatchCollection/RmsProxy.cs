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
            _listener = new TcpListener(IPAddress.Any, LeagueProxy.RmsPort);
            _listener.Start();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    _ = HandleClient(client, token);
                }
            }
            catch (ObjectDisposedException) { /* Listener was stopped, we can ignore this exception */ }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ERROR] Error in RMS listener: {ex.Message}");
            }
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
            Trace.WriteLine($"[INFO] RMS handshake response: {responseLine}");
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
                Trace.WriteLine("[INFO] RMS Connection to server established.");

                await HandleWebSocketHandshakeAsync(client.GetStream(), serverStream);

                var clientToServerTask = ForwardClientToServerAsync(client.GetStream(), serverStream, token);
                var serverToClientTask = ForwardServerToClientAsync(serverStream, client.GetStream(), token);

                await Task.WhenAny(clientToServerTask, serverToClientTask);
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
            {
                Trace.WriteLine($"[WARN] RMS Client disconnected or connection error: {ex.Message}");
            }
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

        private static async Task ForwardServerToClientAsync(Stream source, Stream destination, CancellationToken token)
        {
            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await source.ReadAsync(buffer, token)) > 0)
            {
                string decodedMessage;

                using (var memoryStream = new MemoryStream(buffer, 0, bytesRead))
                using (var outputStream = new MemoryStream())
                {
                    while (memoryStream.Position < memoryStream.Length)
                    {
                        if (IsGzipHeader(memoryStream))
                        {
                            using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress, leaveOpen: true);
                            gzipStream.CopyTo(outputStream);
                        }
                        else
                        {
                            int currentByte = memoryStream.ReadByte();
                            if (currentByte == -1) break;
                            outputStream.WriteByte((byte)currentByte);
                        }
                    }

                    decodedMessage = Encoding.UTF8.GetString(outputStream.ToArray());
                }

                if (RankedRestriction().IsMatch(decodedMessage))
                {
                    continue;
                }
                else if (NoClientClose().IsMatch(decodedMessage))
                {
                    continue;
                }
                //else if (BlockVanguardSessionCheck().IsMatch(decodedMessage))
                //{
                //    Trace.WriteLine("[INFO] ATTEMPING TO BYPASS GAMEFLOW KICK/BLOCK: BLOCKING MESSAING " + decodedMessage);
                //    continue; // doesnt work - causes client to hang is champ select forever since this is server sided
                //} 
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), token);
            }
        }

        private static bool IsGzipHeader(Stream stream)
        {
            if (stream.Length - stream.Position < 2) return false;

            long originalPosition = stream.Position;

            try
            {
                int firstByte = stream.ReadByte();
                int secondByte = stream.ReadByte();

                stream.Position = originalPosition;

                return firstByte == 0x1F && secondByte == 0x8B;
            }
            catch
            {
                stream.Position = originalPosition;
                return false;
            }
        }
        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
        }

        [GeneratedRegex(@"RANKED_RESTRICTED")]
        private static partial Regex RankedRestriction();
        [GeneratedRegex(@"GAMEFLOW_EVENT.PLAYER_KICKED.VANGUARD")]
        private static partial Regex NoClientClose();
        //[GeneratedRegex(@"PLAYER_LACKS_VANGUARD_SESSION")]
        //private static partial Regex BlockVanguardSessionCheck();
    }
}