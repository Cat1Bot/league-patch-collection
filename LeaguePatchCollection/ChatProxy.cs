using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace LeaguePatchCollection
{
    public partial class XMPPProxy
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private static readonly object FlagsLock = new();
        private static readonly List<Stream> _activeClientStreams = []; //store flags in each connected client stream 
        private static readonly Dictionary<Stream, bool> CanSendPresence = [];
        private static readonly string RosterTag = "<query xmlns='jabber:iq:riotgames:roster'>";

        public async Task RunAsync(CancellationToken token)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _listener = new TcpListener(IPAddress.Loopback, LeagueProxy.ChatPort);
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

        private static async Task HandleClient(TcpClient client, CancellationToken token)
        {
            NetworkStream? networkStream = null;
            try
            {

                networkStream = client.GetStream();
                lock (FlagsLock)
                {
                    _activeClientStreams.Add(networkStream);
                    CanSendPresence[networkStream] = false;
                }
                var chatHost = ConfigProxy.ChatHost;
                if (string.IsNullOrEmpty(chatHost))
                {
                    return;
                }

                using var tcpClient = new TcpClient(chatHost, 5223);
                using var sslStream = new SslStream(tcpClient.GetStream(), false, (sender, certificate, chain, sslPolicyErrors) => true);

                var sslOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = chatHost,
                    EnabledSslProtocols = SslProtocols.Tls12
                };
                await sslStream.AuthenticateAsClientAsync(sslOptions, token);

                var clientToServerTask = ClientToServerAsync(networkStream, sslStream, token);
                var serverToClientTask = ServerToClientAsync(sslStream, networkStream, token);

                await Task.WhenAny(clientToServerTask, serverToClientTask);
            }
            catch (Exception) { }
            finally
            {
                if (networkStream != null)
                {
                    lock (FlagsLock)
                    {
                        _activeClientStreams.Remove(networkStream);
                        CanSendPresence.Remove(networkStream);
                    }
                }
                client?.Close();
                networkStream?.Dispose();
            }
        }

        private static async Task ClientToServerAsync(NetworkStream source, SslStream destination, CancellationToken token)
        {
            var buffer = new byte[8192];
            int bytesRead;

            try
            {
                while ((bytesRead = await source.ReadAsync(buffer, token)) > 0)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    string status = string.Empty;

                    if (message.Contains("<presence"))
                    {
                        if (LeaguePatchCollectionUX.SettingsManager.ChatSettings.EnableOnline)
                            status = "chat";
                        else if (LeaguePatchCollectionUX.SettingsManager.ChatSettings.EnableAway)
                            status = "away";
                        else if (LeaguePatchCollectionUX.SettingsManager.ChatSettings.EnableMobile)
                            status = "mobile";
                        else if (LeaguePatchCollectionUX.SettingsManager.ChatSettings.EnableOffline)
                            status = "offline";

                        message = ShowMitm().Replace(message, $"<show>{status}</show>");
                        message = StMitm().Replace(message, $"<st>{status}</st>");

                        if (LeaguePatchCollectionUX.SettingsManager.ChatSettings.EnableOffline || LeaguePatchCollectionUX.SettingsManager.ChatSettings.EnableMobile)
                        {
                            message = RemoveLeague().Replace(message, string.Empty);
                            message = RemoveVal().Replace(message, string.Empty);
                            message = RemoveBacon().Replace(message, string.Empty);
                        }
                    }

                    if (FilterFakePlayer().IsMatch(message))
                    {
                        continue;
                    }

                    var modifiedBufferFinal = Encoding.UTF8.GetBytes(message);
                    await destination.WriteAsync(modifiedBufferFinal, token);
                    await destination.FlushAsync(token);
                }
            }
            catch (Exception) { /* Client disconnected or server connection error */ }
        }

        private static async Task ServerToClientAsync(SslStream source, NetworkStream destination, CancellationToken token)
        {
            var buffer = new byte[8192];
            int bytesRead;

            try
            {
                while ((bytesRead = await source.ReadAsync(buffer, token)) > 0)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    bool ShouldSendPresence = false;
                    lock (FlagsLock)
                    {
                        if (CanSendPresence.TryGetValue(destination, out bool ready) && ready)
                        {
                            CanSendPresence[destination] = false;
                            ShouldSendPresence = true;
                        }

                        if (message.Contains(RosterTag))
                        {
                            string fakePlayer =
                                "<item jid='00000000-0000-0000-0000-000000000000@na1.pvp.net' name='League Patch Collection' subscription='both' puuid='00000000-0000-0000-0000-000000000000'>" +
                                "<note>This is an automated service by League Patch Collection - The fan-favorite League Client mod menu and Vanguard disabler.</note>" +
                                "<group priority='9999'>Third Party</group>" +
                                "<state>online</state>" +
                                "<id name='League Patch Collection' tagline='Free'/>" +
                                "<platforms><riot name='League Patch Collection' tagline='Free'/></platforms>" +
                                "</item>";

                            message = message.Insert(message.IndexOf(RosterTag, StringComparison.Ordinal) + RosterTag.Length, fakePlayer);

                            CanSendPresence[destination] = true;
                        }
                    }

                    if (ShouldSendPresence)
                        await SendChatStatus(destination);

                    var modifiedBufferFinal = Encoding.UTF8.GetBytes(message);
                    await destination.WriteAsync(modifiedBufferFinal, token);
                    await destination.FlushAsync(token);
                }
            }
            catch (Exception) { /* Server disconnected or client connection error */ }
        }

        private static async Task SendChatStatus(Stream destination)
        {
            var randomStanzaId = Guid.NewGuid();
            var unixTimeMilliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            var presenceMessage =
                $"<presence from='00000000-0000-0000-0000-000000000000@na1.pvp.net/RC-LeaguePatchCollection' id='-b{randomStanzaId}'>" +
                "<games>" +
                $"<keystone><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>keystone</s.p><pty/></keystone>" +
                $"<league_of_legends><st>dnd</st><s.t>{unixTimeMilliseconds}</s.t><s.p>league_of_legends</s.p><s.c>live</s.c><m>Active and Working</m></league_of_legends>" +
                //$"<valorant><st>away</st><s.t>{unixTimeMilliseconds}</s.t><s.p>valorant</s.p><s.r>PC</s.r><m>Active and Working</m>" +
                //$"<p>eyJpc1ZhbGlkIjp0cnVlLCJwYXJ0eUlkIjoiMDAwMDAwMDAtMDAwMC0wMDAwLTAwMDAtMDAwMDAwMDAwMDAwIiwicGFydHlDbGllbnRWZXJzaW9uIjoidW5rbm93biIsImFjY291bnRMZXZlbCI6OTk5fQ==</p><pty/></valorant>" +
                //$"<bacon><st>away</st><s.t>{unixTimeMilliseconds}</s.t><s.l>bacon_availability_online</s.l><s.p>bacon</s.p></bacon>" +
                "</games>" +
                "<show>dnd</show>" +
                "<platform>riot</platform>" +
                "<status/>" +
                "</presence>";

            var presenceBytes = Encoding.UTF8.GetBytes(presenceMessage);

            await destination.WriteAsync(presenceBytes);
            await destination.FlushAsync();

            await SendFirstMessage(destination);
            await SendSecondMessage(destination);
        }

        private static async Task SendFirstMessage(Stream destination)
        {
            await Task.Delay(1000);
            var stamp = DateTime.UtcNow.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss.fff");

            var FirstMessage =
                $"<message from='00000000-0000-0000-0000-000000000000@na1.pvp.net/RC-LeaguePatchCollection' stamp='{stamp}' id='fake-{stamp}' type='chat'><body>Welcome to League Patch Collection, created by Cat Bot. This tool is free and open-source at https://github.com/Cat1Bot/league-patch-collection - IF YOU PAID FOR THIS, YOU GOT SCAMMED.</body></message>";

            var messageBytes = Encoding.UTF8.GetBytes(FirstMessage);

            await destination.WriteAsync(messageBytes);
            await destination.FlushAsync();
        }

        private static async Task SendSecondMessage(Stream destination)
        {
            await Task.Delay(1000);
            var stamp = DateTime.UtcNow.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss.fff");

            var SecondMessage =
                $"<message from='00000000-0000-0000-0000-000000000000@na1.pvp.net/RC-LeaguePatchCollection' stamp='{stamp}' id='fake-{stamp}' type='chat'><body>Contact || Discord: c4t_bot , Reddit: u/Cat_Bot4 || Donate || Venmo: @Cat_Bot</body></message>";

            var SecondmessageBytes = Encoding.UTF8.GetBytes(SecondMessage);

            await destination.WriteAsync(SecondmessageBytes);
            await destination.FlushAsync();
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;
        }

        [GeneratedRegex(@"<show>.*?</show>")]
        private static partial Regex ShowMitm();
        [GeneratedRegex(@"<st>.*?</st>")]
        private static partial Regex StMitm();
        [GeneratedRegex("<league_of_legends>.*?</league_of_legends>")]
        private static partial Regex RemoveLeague();
        [GeneratedRegex("<valorant>.*?</valorant>")]
        private static partial Regex RemoveVal();
        [GeneratedRegex("<bacon>.*?</bacon>")]
        private static partial Regex RemoveBacon();
        [GeneratedRegex(@"\b00000000-0000-0000-0000-000000000000@na1.pvp.net\b")]
        private static partial Regex FilterFakePlayer();
    }
}
