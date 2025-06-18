using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace LeaguePatchCollection
{
    public class RTMPProxy
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;

        public async Task RunAsync(CancellationToken token)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _listener = new TcpListener(IPAddress.Loopback, LeagueProxy.RtmpPort);
            _listener.Start();
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    _ = HandleClient(client, token);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[RTMP] Stopping proxy...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RTMP] Error: {ex.Message}");
            }
            finally
            {
                Stop();
                Console.WriteLine("[RTMP] Proxy successfully stopped.");
            }
        }

        private static async Task HandleClient(TcpClient client, CancellationToken cancellationToken)
        {
            NetworkStream? networkStream = null;
            try
            {
                networkStream = client.GetStream();

                var RtmpHost = SystemYamlLive.RtmpServer;
                if (string.IsNullOrEmpty(RtmpHost))
                    throw new Exception("RTMP host is not ready yet.");

                using var tcpClient = new TcpClient(RtmpHost, 2099);
                Stream serverStream = tcpClient.GetStream();

                using var sslStream = new SslStream(serverStream, false, (sender, certificate, chain, sslPolicyErrors) => true);
                var sslOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = RtmpHost,
                    EnabledSslProtocols = SslProtocols.Tls12
                };
                await sslStream.AuthenticateAsClientAsync(sslOptions, cancellationToken);
                serverStream = sslStream;
                Console.WriteLine("[RTMP] Connection to server established.");

                var clientToServerTask = ClientToServerAsync(networkStream, serverStream, cancellationToken);
                var serverToClientTask = ServerToClientAsync(serverStream, networkStream, cancellationToken);

                await Task.WhenAny(clientToServerTask, serverToClientTask);
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
            {
                Console.WriteLine($"[RTMP] Client disconnected or connection error: {ex.Message}");
            }
            finally
            {
                client?.Close();
                networkStream?.Dispose();
            }
        }

        private static async Task ClientToServerAsync(Stream clientStream, Stream serverStream, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            try
            {
                int bytesRead;
                while ((bytesRead = await clientStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await serverStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    await serverStream.FlushAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RTMP] Error forwarding Client -> Server: {ex.Message}");
            }
        }

        private static async Task ServerToClientAsync(Stream serverStream, Stream clientStream, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            try
            {
                int bytesRead;
                while ((bytesRead = await serverStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    // decode packets here, mitm if needed then re encode and send back to client. idk this is cancer to do
                    await clientStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    await clientStream.FlushAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RTMP] Error forwarding Server -> Client: {ex.Message}");
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;
        }
    }
}
