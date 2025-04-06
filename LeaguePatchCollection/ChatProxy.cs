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
        private bool _WelcomeMessageSent = false;

        public async Task RunAsync(CancellationToken token)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _listener = new TcpListener(IPAddress.Any, LeagueProxy.ChatPort);
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
                Trace.WriteLine($"[ERROR] Error in XMPP listener: {ex.Message}");
            }
            finally
            {
                Stop();
            }
        }
        private async Task HandleClient(TcpClient client, CancellationToken token)
        {
            NetworkStream? networkStream = null;
            try
            {
                networkStream = client.GetStream();
                var chatHost = ConfigProxy.ChatHost;
                if (string.IsNullOrEmpty(chatHost))
                    throw new Exception("Chat host is not ready yet.");

                using var tcpClient = new TcpClient(chatHost, 5223);
                using var sslStream = new SslStream(tcpClient.GetStream(), false, (sender, certificate, chain, sslPolicyErrors) => true);

                var sslOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = chatHost,
                    EnabledSslProtocols = SslProtocols.Tls12
                };
                await sslStream.AuthenticateAsClientAsync(sslOptions, token);

                var clientToServerTask = ForwardClientToServerAsync(networkStream, sslStream, token);
                var serverToClientTask = ForwardServerToClientAsync(sslStream, networkStream, token);

                await Task.WhenAny(clientToServerTask, serverToClientTask);
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
            {
                Console.WriteLine($"[WARN] XMPP Client disconnected or connection error: {ex.Message}");
            }
            finally
            {
                client?.Close();
                networkStream?.Dispose();
            }
        }

        private static async Task ForwardClientToServerAsync(NetworkStream source, SslStream destination, CancellationToken token)
        {
            var buffer = new byte[8192];
            int bytesRead;

            try
            {
                while ((bytesRead = await source.ReadAsync(buffer, token)) > 0)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    string status = string.Empty;
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

        private async Task ForwardServerToClientAsync(SslStream source, NetworkStream destination, CancellationToken token)
        {
            var buffer = new byte[8192];
            int bytesRead;

            try
            {
                while ((bytesRead = await source.ReadAsync(buffer, token)) > 0)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    const string rosterTag = "<query xmlns='jabber:iq:riotgames:roster'>";
                    if (message.Contains(rosterTag))
                    {
                        string fakePlayer =
                            "<item jid='00000000-0000-0000-0000-000000000000@na1.pvp.net' name='League Patch Collection' subscription='both' puuid='00000000-0000-0000-0000-000000000000'>" +
                            "<note>This is an automated service by League Patch Collection - The fan-favorite League Client mod menu and Vanguard disabler.</note>" +
                            "<group priority='9999'>Third Party</group>" +
                            "<state>online</state>" +
                            "<id name='League Patch Collection' tagline='Free'/>" +
                            "<platforms><riot name='League Patch Collection' tagline='Free'/></platforms>" +
                            "<lol name='League Patch Collection'/>" +
                            "</item>";

                        message = message.Insert(message.IndexOf(rosterTag, StringComparison.Ordinal) + rosterTag.Length, fakePlayer);
                        _ = Task.Run(() => SendCustomPacket(destination), token);
                    }

                    var modifiedBufferFinal = Encoding.UTF8.GetBytes(message);
                    await destination.WriteAsync(modifiedBufferFinal, token);
                    await destination.FlushAsync(token);
                }
            }
            catch (Exception) { /* Server disconnected or client connection error */ }
        }


        private async Task SendCustomPacket(Stream destination)
        {
            var randomStanzaId = Guid.NewGuid();
            var unixTimeMilliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            var presenceMessage =
                $"<presence from='00000000-0000-0000-0000-000000000000@na1.pvp.net/RC-LeaguePatchCollection' id='presence_{randomStanzaId}'>" +
                "<games>" +
                $"<keystone><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>keystone</s.p></keystone>" +
                $"<league_of_legends><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>league_of_legends</s.p><s.c>live</s.c><m>Active and Working</m>" +
                $"<p>{{\"championId\":\"\",\"gameQueueType\":\"\",\"gameStatus\":\"outOfGame\",\"legendaryMasteryScore\":\"\",\"level\":\"\",\"mapId\":\"\",\"profileIcon\":\"-1\",\"puuid\":\"\",\"rankedLeagueDivision\":\"\",\"rankedLeagueQueue\":\"\",\"rankedLeagueTier\":\"\",\"rankedLosses\":\"\",\"rankedPrevSeasonDivision\":\"\",\"rankedPrevSeasonTier\":\"\",\"rankedSplitRewardLevel\":\"\",\"rankedWins\":\"\",\"regalia\":\"\",\"skinVariant\":\"\",\"skinname\":\"\"}}</p>" +
                "</league_of_legends>" +
                $"<valorant><st>away</st><s.t>{unixTimeMilliseconds}</s.t><s.p>valorant</s.p><s.r>PC</s.r><m>Active and Working</m>" +
                $"<p>eyJpc1ZhbGlkIjp0cnVlLCJwYXJ0eUlkIjoiMDAwMDAwMDAtMDAwMC0wMDAwLTAwMDAtMDAwMDAwMDAwMDAwIiwicGFydHlDbGllbnRWZXJzaW9uIjoidW5rbm93biIsImFjY291bnRMZXZlbCI6OTk5fQ==</p>" +
                "</valorant>" +
                $"<bacon><st>away</st><s.t>{unixTimeMilliseconds}</s.t><s.l>bacon_availability_online</s.l><s.p>bacon</s.p></bacon>" +
                "</games>" +
                "<show>chat</show>" +
                "<platform>riot</platform>" +
                "<status></status>" +
                "</presence>";

            var presenceBytes = Encoding.UTF8.GetBytes(presenceMessage);

            await destination.WriteAsync(presenceBytes);
            if (!_WelcomeMessageSent)
            {
                _WelcomeMessageSent = true;
                _ = Task.Run(() => SendFirstMessage(destination));
            }
        }

        private static async Task SendFirstMessage(Stream destination)
        {
            await Task.Delay(1000);
            var randomStanzaId = Guid.NewGuid();
            var stamp = DateTime.UtcNow.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss.fff");

            var FirstMessage =
                $"<message from='00000000-0000-0000-0000-000000000000@na1.pvp.net/RC-LeaguePatchCollection' stamp='{stamp}' id='fake-{stamp}' type='chat'><body>Welcome to League Patch Collection, created by Cat Bot. This tool is free and open-source at https://github.com/Cat1Bot/league-patch-collection - IF YOU PAID FOR THIS, YOU GOT SCAMMED.</body></message>";

            var messageBytes = Encoding.UTF8.GetBytes(FirstMessage);

            await destination.WriteAsync(messageBytes);
            _ = Task.Run(() => SendSecondMessage(destination));
        }
        private static async Task SendSecondMessage(Stream destination)
        {
            await Task.Delay(1000);
            var stamp = DateTime.UtcNow.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss.fff");

            var SecondMessage =
                $"<message from='00000000-0000-0000-0000-000000000000@na1.pvp.net/RC-LeaguePatchCollection' stamp='{stamp}' id='fake-{stamp}' type='chat'><body>Contact || Discord: c4t_bot , Reddit: u/Cat_Bot4 || Donate || Venmo: @Cat_Bot</body></message>";

            var SecondmessageBytes = Encoding.UTF8.GetBytes(SecondMessage);

            await destination.WriteAsync(SecondmessageBytes);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
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
