using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace LeaguePatchCollection
{
    public partial class XMPPProxy
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private static readonly object FlagsLock = new();
        private static readonly List<Stream> _activeClientStreams = [];
        private static readonly Dictionary<Stream, bool> CanSendPresence = [];

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
            catch { }
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
                await sslStream.AuthenticateAsClientAsync(new() { TargetHost = chatHost }, token);

                var clientToServerTask = ClientToServerAsync(networkStream, sslStream, token);
                var serverToClientTask = ServerToClientAsync(sslStream, networkStream, token);

                await Task.WhenAny(clientToServerTask, serverToClientTask);
            }
            catch { }
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

        private static async Task ClientToServerAsync(Stream clientStream, Stream serverStream, CancellationToken token)
        {
            var buffer = new byte[8192];
            int bytesRead;

            try
            {
                while ((bytesRead = await clientStream.ReadAsync(buffer, token)) > 0)
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
                            message = ClearGames().Replace(message, string.Empty);
                        }
                    }

                    if (FilterFakePlayer().IsMatch(message)) continue;

                    bool shouldSend = false;
                    lock (FlagsLock)
                    {
                        if (CanSendPresence.TryGetValue(clientStream, out bool ready) && ready)
                        {
                            CanSendPresence[clientStream] = false;
                            shouldSend = true;
                        }
                    }
                    if (shouldSend)
                    {
                        await SendChatStatus(clientStream);
                    }

                    var modifiedBufferFinal = Encoding.UTF8.GetBytes(message);
                    await serverStream.WriteAsync(modifiedBufferFinal, token);
                    await serverStream.FlushAsync(token);
                }
            }
            catch { }
        }

        private static async Task ServerToClientAsync(Stream serverStream, Stream clientStream, CancellationToken token)
        {
            var buffer = new byte[8192];
            int bytesRead;

            try
            {
                while ((bytesRead = await serverStream.ReadAsync(buffer, token)) > 0)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    if (message.Contains("<query xmlns='jabber:iq:riotgames:roster'>"))
                    {
                        string fakePlayer =
                            "<item jid='00000000-0000-0000-0000-000000000000@na1.pvp.net' name='League Patch Collection' subscription='both' puuid='00000000-0000-0000-0000-000000000000'>" +
                            "<note>This is an automated service by League Patch Collection - The fan-favorite League Client mod menu and Vanguard disabler.</note>" +
                            "<group priority='9999'>Third Party</group>" +
                            "<state>online</state>" +
                            "<id name='League Patch Collection' tagline='Free'/>" +
                            "<platforms><riot name='League Patch Collection' tagline='Free'/></platforms>" +
                            "</item>";

                        message = message.Insert(message.IndexOf("<query xmlns='jabber:iq:riotgames:roster'>") + "<query xmlns='jabber:iq:riotgames:roster'>".Length, fakePlayer);

                        var modifiedBufferFinal = Encoding.UTF8.GetBytes(message);
                        await clientStream.WriteAsync(modifiedBufferFinal, token);
                        await clientStream.FlushAsync(token);

                        lock (FlagsLock)
                        {
                            CanSendPresence[clientStream] = true;
                        }
                    }
                    else
                    {
                        var modifiedBufferFinal = Encoding.UTF8.GetBytes(message);
                        await clientStream.WriteAsync(modifiedBufferFinal, token);
                        await clientStream.FlushAsync(token);
                    }
                }
            }
            catch { }
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
                $"<message from='00000000-0000-0000-0000-000000000000@na1.pvp.net/RC-LeaguePatchCollection' stamp='{stamp}' id='fake-{stamp}' type='chat'><body>If there is an issue with the app, open a new issue on the Github page.</body></message>";

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

        [GeneratedRegex("(?<=<games>).*?(?=</games>)")]
        private static partial Regex ClearGames();
        [GeneratedRegex(@"<show>.*?</show>")]
        private static partial Regex ShowMitm();
        [GeneratedRegex(@"<st>.*?</st>")]
        private static partial Regex StMitm();
        [GeneratedRegex(@"\b00000000-0000-0000-0000-000000000000@na1.pvp.net\b")]
        private static partial Regex FilterFakePlayer();
    }
}
