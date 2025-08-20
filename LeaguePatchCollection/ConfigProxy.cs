using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Diagnostics;

namespace LeaguePatchCollection;

public partial class ConfigProxy
{
    public static string? GeopassUrl { get; private set; }
    public static string? LcuNavUrl { get; private set; }
    public static string? LedgeUrl { get; private set; }
    public static string? RmsHost { get; private set; }
    public static string? ChatHost { get; private set; }
    public static string? MailboxUrl { get; private set; }
    public static string? PlatformUrl { get; private set; }

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private static readonly string[] separator = ["\r\n"];

    public async Task RunAsync(CancellationToken token)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _listener = new TcpListener(IPAddress.Loopback, LeagueProxy.ConfigPort);
        _listener.Start();

        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                _ = HandleClientAsync(client, token);
            }
        }
        catch (Exception) { }
        finally
        {
            Stop();
        }
    }

    private static async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using var clientStream = client.GetStream();
        TcpClient? serverClient = null;
        SslStream? sslStream = null;

        try
        {
            string? targetHost = "clientconfig.rpg.riotgames.com";

            var buffer = new byte[8192];
            var headerTerminator = Encoding.UTF8.GetBytes("\r\n\r\n");

            while (!token.IsCancellationRequested)
            {
                using var requestStream = new MemoryStream();
                int bytesRead, headerEndIndex = -1;
                bool headersComplete = false;

                while (!headersComplete && (bytesRead = await clientStream.ReadAsync(buffer, token)) > 0)
                {
                    requestStream.Write(buffer, 0, bytesRead);
                    headerEndIndex = IndexOf(requestStream, headerTerminator);
                    if (headerEndIndex != -1)
                        headersComplete = true;
                }

                if (!headersComplete)
                    break;

                int headerLength = headerEndIndex + headerTerminator.Length;
                string headersText = Encoding.UTF8.GetString(requestStream.GetBuffer(), 0, headerLength);

                string[] requestLines = headersText.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                string endpoint = requestLines.Length > 0 ? requestLines[0].Split(' ')[1] : string.Empty;

                int contentLength = 0;
                foreach (var line in requestLines)
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        string value = line["Content-Length:".Length..].Trim();
                        if (int.TryParse(value, out int len))
                            contentLength = len;
                    }
                }

                int bodyBytesReceived = (int)(requestStream.Length - headerLength);
                while (bodyBytesReceived < contentLength &&
                       (bytesRead = await clientStream.ReadAsync(buffer, token)) > 0)
                {
                    requestStream.Write(buffer, 0, bytesRead);
                    bodyBytesReceived += bytesRead;
                }

                byte[] fullRequestBytes = requestStream.ToArray();

                if (serverClient == null || !serverClient.Connected)
                {
                    serverClient?.Close();
                    serverClient = new TcpClient();
                    await serverClient.ConnectAsync(targetHost, 443, token);
                    sslStream = new SslStream(serverClient.GetStream(), false, (sender, certificate, chain, sslPolicyErrors) => true);
                    await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = targetHost,
                        EnabledSslProtocols = SslProtocols.Tls12
                    }, token);
                }

                headersText = ReplaceHost().Replace(headersText, targetHost);
                headersText = ReplaceOrigin().Replace(headersText, $"https://{targetHost}");
                byte[] modifiedHeaderBytes = Encoding.UTF8.GetBytes(headersText);

                await sslStream!.WriteAsync(modifiedHeaderBytes, token); //is it even technically possible for ssl stream to be null here??
                if (bodyBytesReceived > 0)
                    await sslStream.WriteAsync(fullRequestBytes.AsMemory(headerLength, bodyBytesReceived), token);
                await sslStream.FlushAsync(token);

                await ForwardServerToClientAsync(sslStream, clientStream, endpoint, token);
            }
        }
        catch (IOException) { }
        finally
        {
            sslStream?.Dispose();
            serverClient?.Close();
            client.Close();
        }
    }
    private static int IndexOf(MemoryStream stream, byte[] pattern)
    {
        int len = (int)stream.Length;
        byte[] buffer = stream.GetBuffer();
        for (int i = 0; i <= len - pattern.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (buffer[i + j] != pattern[j])
                {
                    found = false;
                    break;
                }
            }
            if (found)
                return i;
        }
        return -1;
    }

    private static async Task ForwardServerToClientAsync(Stream serverStream, Stream clientStream, string endpoint, CancellationToken token)
    {
        byte[] headerBytes = await ReadHeadersAsync(serverStream, token);
        if (headerBytes == null || headerBytes.Length == 0)
        {
            return;
        }

        string headerStr = Encoding.UTF8.GetString(headerBytes);
        int headerEndIndex = headerStr.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEndIndex < 0)
        {
            await clientStream.WriteAsync(headerBytes, token);
            await clientStream.FlushAsync(token);
            return;
        }
        string headerSection = headerStr[..(headerEndIndex + 4)];

        bool isNoContent = headerSection.StartsWith("HTTP/1.1 204", StringComparison.OrdinalIgnoreCase) ||
           headerSection.StartsWith("HTTP/2 204", StringComparison.OrdinalIgnoreCase) ||
           headerSection.Contains("Content-Length: 0", StringComparison.OrdinalIgnoreCase);

        if (isNoContent)
        {
            await clientStream.WriteAsync(headerBytes, token);
            await clientStream.FlushAsync(token);
            return;
        }

        int extraBodyBytesCount = headerBytes.Length - (headerEndIndex + 4);
        byte[] extraBodyBytes = new byte[extraBodyBytesCount];
        if (extraBodyBytesCount > 0)
        {
            Array.Copy(headerBytes, headerEndIndex + 4, extraBodyBytes, 0, extraBodyBytesCount);
        }

        int contentLength = 0;
        bool hasContentLength = false;
        bool isChunked = false;

        var headerLines = headerSection.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in headerLines)
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                string value = line["Content-Length:".Length..].Trim();
                if (int.TryParse(value, out int len))
                {
                    contentLength = len;
                    hasContentLength = true;
                }
            }
            if (line.StartsWith("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase))
            {
                isChunked = true;
            }
        }

        if (isChunked)
        {
            headerSection = TransferEncoding().Replace(headerSection, "");
        }

        MemoryStream bodyStream = new();
        if (extraBodyBytesCount > 0)
        {
            bodyStream.Write(extraBodyBytes, 0, extraBodyBytesCount);
        }

        if (isChunked)
        {
            await ReadChunkedBodyAsync(serverStream, bodyStream, token);
        }
        else
        {
            if (hasContentLength)
            {
                while (bodyStream.Length < contentLength)
                {
                    byte[] buffer = new byte[8192];
                    int read = await serverStream.ReadAsync(buffer, token);
                    if (read <= 0)
                        break;
                    bodyStream.Write(buffer, 0, read);
                }
            }
            else
            {
                byte[] buffer = new byte[8192];
                int read;
                while ((read = await serverStream.ReadAsync(buffer, token)) > 0)
                {
                    bodyStream.Write(buffer, 0, read);
                }
            }
        }

        byte[] bodyBytes = bodyStream.ToArray();

        if (headerSection.Contains("Content-Encoding: gzip", StringComparison.OrdinalIgnoreCase))
        {
            byte[] decompressedBytes;
            using (var compressedStream = new MemoryStream(bodyBytes))
            using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
            using (var decompressedStream = new MemoryStream())
            {
                await gzipStream.CopyToAsync(decompressedStream, token);
                decompressedBytes = decompressedStream.ToArray();
            }

            decompressedBytes = ModifyResponsePayload(decompressedBytes, endpoint);

            string modifiedHeader = RemoveContentEncoding().Replace(headerSection, "");
            modifiedHeader = RemoveContentLenth().Replace(modifiedHeader, "");
            modifiedHeader = modifiedHeader.TrimEnd() + "\r\n" + $"Content-Length: {decompressedBytes.Length}" + "\r\n\r\n";

            byte[] finalHeaderBytes = Encoding.UTF8.GetBytes(modifiedHeader);
            await clientStream.WriteAsync(finalHeaderBytes, token);
            await clientStream.WriteAsync(decompressedBytes, token);
        }
        else
        {
            byte[] modifiedBody = ModifyResponsePayload(bodyBytes, endpoint);

            string modifiedHeader = headerSection;
            if (isChunked)
            {
                modifiedHeader = modifiedHeader.TrimEnd() + "\r\n" + $"Content-Length: {modifiedBody.Length}" + "\r\n\r\n";
            }
            else if (hasContentLength)
            {
                modifiedHeader = RemoveContentLenth().Replace(modifiedHeader, "");
                modifiedHeader = modifiedHeader.TrimEnd() + "\r\n" + $"Content-Length: {modifiedBody.Length}" + "\r\n\r\n";
            }

            byte[] headerToSend = Encoding.UTF8.GetBytes(modifiedHeader);
            await clientStream.WriteAsync(headerToSend, token);
            if (modifiedBody.Length > 0)
            {
                await clientStream.WriteAsync(modifiedBody, token);
            }
        }

        await clientStream.FlushAsync(token);
    }
    private static async Task ReadChunkedBodyAsync(Stream serverStream, MemoryStream bodyStream, CancellationToken token)
    {
        while (true)
        {
            string chunkSizeLine = await ReadLineAsync(serverStream, token);
            if (string.IsNullOrWhiteSpace(chunkSizeLine)) continue;

            if (!int.TryParse(chunkSizeLine, System.Globalization.NumberStyles.HexNumber, null, out int chunkSize) || chunkSize == 0)
            {
                await ReadLineAsync(serverStream, token);
                break;
            }

            byte[] buffer = new byte[chunkSize];
            int totalRead = 0;
            while (totalRead < chunkSize)
            {
                int read = await serverStream.ReadAsync(buffer.AsMemory(totalRead, chunkSize - totalRead), token);
                if (read <= 0) throw new EndOfStreamException("Unexpected end of chunked data.");
                totalRead += read;
            }

            await bodyStream.WriteAsync(buffer, token);
            await ReadLineAsync(serverStream, token);
        }
    }
    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken token)
    {
        MemoryStream lineBuffer = new();
        byte[] buffer = new byte[1];

        while (await stream.ReadAsync(buffer, token) > 0)
        {
            if (buffer[0] == '\n') break;
            if (buffer[0] != '\r') lineBuffer.WriteByte(buffer[0]);
        }

        return Encoding.UTF8.GetString(lineBuffer.ToArray());
    }
    private static async Task<byte[]> ReadHeadersAsync(Stream stream, CancellationToken token)
    {
        using MemoryStream ms = new();
        byte[] buffer = new byte[1];
        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, 1), token);
            if (read <= 0)
                break;
            ms.Write(buffer, 0, read);
            if (ms.Length >= 4)
            {
                byte[] arr = ms.ToArray();
                int len = arr.Length;
                if (arr[len - 4] == (byte)'\r' && arr[len - 3] == (byte)'\n' &&
                    arr[len - 2] == (byte)'\r' && arr[len - 1] == (byte)'\n')
                {
                    break;
                }
            }
        }
        return ms.ToArray();
    }
    private static byte[] ModifyResponsePayload(byte[] payload, string endpoint)
    {
        string payloadStr = Encoding.UTF8.GetString(payload);
        var configObject = JsonSerializer.Deserialize<JsonNode>(payload);

        var GeopassUrlNode = configObject?["keystone.player-affinity.playerAffinityServiceURL"];
        if (GeopassUrlNode != null)
        {
            GeopassUrl = GeopassUrlNode.ToString();
        }

        if (configObject?["keystone.mailbox.clusters"] is JsonObject MailboxAffinities)
        {
            if (MailboxAffinities.DeepClone() is JsonObject originalMailboxAffinities)
            {
                StartBackgroundTaskForMailboxAffinities(originalMailboxAffinities);
            }
            var keys = MailboxAffinities.Select(entry => entry.Key).ToArray();
            foreach (var key in keys)
            {
                MailboxAffinities[key] = $"http://127.0.0.1:{LeagueProxy.MailboxPort}";
            }
        }

        var PlatformUrlNode = configObject?["lol.client_settings.player_platform_edge.url"];
        if (PlatformUrlNode != null)
        {
            PlatformUrl = PlatformUrlNode.ToString();
        }

        var NavUrlNode = configObject?["lol.client_settings.client_navigability.base_url"];
        if (NavUrlNode != null)
        {
            LcuNavUrl = NavUrlNode.ToString();
        }

        if (configObject?["rms.affinities"] is JsonObject rmsAffinities)
        {
            var originalRmsAffinities = JsonSerializer.Deserialize<JsonObject>(JsonSerializer.Serialize(rmsAffinities));

            if (originalRmsAffinities != null)
            {
                StartBackgroundTaskForRmsAffinities(originalRmsAffinities);
            }
            var keys = rmsAffinities.Select(entry => entry.Key).ToArray();
            foreach (var key in keys)
            {
                rmsAffinities[key] = "ws://127.0.0.1";
            }
        }

        if (configObject?["chat.affinities"] is JsonObject chatAffinities)
        {
            var originalChatAffinities = JsonSerializer.Deserialize<JsonObject>(JsonSerializer.Serialize(chatAffinities));

            if (originalChatAffinities != null)
            {
                StartBackgroundTaskForChatAffinities(originalChatAffinities);
            }
            var keys = chatAffinities.Select(entry => entry.Key).ToArray();
            foreach (var key in keys)
            {
                chatAffinities[key] = "127.0.0.1";
            }
        }

        var leagueEdgeUrlNode = configObject?["lol.client_settings.league_edge.url"];
        if (leagueEdgeUrlNode != null)
        {
            LedgeUrl = leagueEdgeUrlNode.ToString();
        }

        if (configObject?["keystone.loyalty.config"] is JsonObject loyaltyConfig)
        {
            foreach (var region in loyaltyConfig)
            {
                if (region.Value is JsonObject regionConfig && regionConfig.ContainsKey("enabled"))
                {
                    regionConfig["enabled"] = false;
                }
            }
        }

        if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Novgk)
        {
            SetKey(configObject, "anticheat.vanguard.backgroundInstall", false);
            SetKey(configObject, "anticheat.vanguard.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.restart_required.disabled", true);
            SetKey(configObject, "keystone.client.feature_flags.vanguardLaunch.disabled", true);
            SetKey(configObject, "keystone.client.feature_flags.vanguard_attestation.enabled", false);
            SetKey(configObject, "lol.client_settings.clash.vanguard_check_enabled", false);
            SetKey(configObject, "lol.client_settings.vanguard.enabled", false);
            SetKey(configObject, "lol.client_settings.vanguard.enabled_embedded", false);
            SetKey(configObject, "lol.client_settings.vanguard.url", "");
            SetKey(configObject, "lion.vanguard.required", false);
            SetKey(configObject, "lion.vanguard.netrequired", false);
            RemoveVanguardDependencies(configObject, "keystone.products.lion.patchlines.live");
            RemoveVanguardDependencies(configObject, "keystone.products.league_of_legends.patchlines.live");
            RemoveVanguardDependencies(configObject, "keystone.products.league_of_legends.patchlines.pbe");
            RemoveVanguardDependencies(configObject, "keystone.products.valorant.patchlines.live");
        }

        if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Legacyhonor)
        {
            SetNestedKeys(configObject, "lol.client_settings.honor", "CeremonyV3Enabled", false);
            SetNestedKeys(configObject, "lol.client_settings.honor", "Enabled", true);
            SetNestedKeys(configObject, "lol.client_settings.honor", "HonorEndpointsV2Enabled", false);
            SetNestedKeys(configObject, "lol.client_settings.honor", "HonorSuggestionsEnabled", true);
            SetNestedKeys(configObject, "lol.client_settings.honor", "HonorVisibilityEnabled", true);
            SetNestedKeys(configObject, "lol.client_settings.honor", "SecondsToVote", 90);
        }

        if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Namebypass)
        {
            SetKey(configObject, "keystone.client.feature_flags.dismissible_name_change_modal.enabled", true);
            SetKey(configObject, "keystone.client.feature_flags.flaggedNameModal.disabled", true);
            SetKey(configObject, "keystone.client.feature_flags.riot_id_required_modal.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.username_required_modal.enabled", false);
        }

        if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Nobloatware)
        {
            if (configObject?["lol.client_settings.nacho.active_banners"] is JsonObject activeBannersConfig &&
                activeBannersConfig.ContainsKey("enabled"))
            {
                activeBannersConfig["enabled"] = false;
            }

            SetKey(configObject, "lol.client_settings.loot.enable_mythic_shoppefront", false);
            SetKey(configObject, "lol.client_settings.champ_select.enable_ability_previews", false);
            SetKey(configObject, "chat.disable_chat_restriction_muted_system_message", true);
            SetKey(configObject, "keystone.client.feature_flags.home_page_route.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.campaign-hub.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.playerReporting.enabled", false);
            SetKey(configObject, "lol.client_settings.loot.standalone_mythic_shop", false);
            SetKey(configObject, "keystone.client.feature_flags.playerReportingMailboxIntegration.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.playerReportingPasIntegration.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.playerReportingReporterFeedback.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.arcane_event.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.arcane_event_live.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.arcane_event_prelaunch.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.arcane_event_premier.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.arcane_theme.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.mfa_notification.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.autoPatch.disabled", true);
            SetKey(configObject, "keystone.client.feature_flags.pending_consent_modal.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.pending_forget_modal.enabled", false);
            SetKey(configObject, "games_library.special_events.enabled", false);
            SetKey(configObject, "riot.eula.agreementBaseURI", "");
            SetKey(configObject, "keystone.client.feature_flags.background_mode_patching.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.product_update_scanner.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.privacyPolicy.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.regionlessLoginInfoTooltip.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.keystone_login_splash_video.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.qrcode_modal.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.riot_mobile_special_event.enabled", false);
            SetKey(configObject, "lol.client_settings.store.hidePurchaseModalQuantityControl", true);
            SetKey(configObject, "lol.client_settings.startup.should_show_progress_bar_text", false);
            SetKey(configObject, "lol.client_settings.paw.enableRPTopUp", false);
            SetKey(configObject, "lol.client_settings.clash.eosCelebrationEnabled", false);
            SetKey(configObject, "lol.client_settings.missions.upsell_opens_event_hub", false);
            SetKey(configObject, "lol.client_settings.client_navigability.info_hub_disabled", true);
            SetKey(configObject, "lol.client_settings.remedy.is_verbal_abuse_remedy_modal_enabled", false);
            SetKey(configObject, "keystone.rso-mobile-ui.accountCreationTosAgreement", false);
            SetKey(configObject, "lol.client_settings.display_legacy_patch_numbers", true);
            SetNestedKeys(configObject, "lol.client_settings.league_edge.enabled_services", "Missions", false);
            SetNestedKeys(configObject, "lol.client_settings.deepLinks", "launchLorEnabled", false);
        }

        if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Nobehavior)
        {
            SetKey(configObject, "keystone.client.feature_flags.penaltyNotifications.enabled", false);
            SetKey(configObject, "lol.client_settings.reputation_based_honor_enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.gaWarning.enabled", false);
            SetKey(configObject, "chat.require_pbtoken_for_muc.enabled", false);
            SetKey(configObject, "chat.send_restrictions_messages_mid_chat.enabled", false);
            SetKey(configObject, "chat.send_restrictions_messages_on_muc_entry.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.playerBehaviorToken.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.playerReporting.enabled", false);
            SetKey(configObject, "keystone.client.feature_flags.restriction.enabled", false);
        }

        SetKey(configObject, "rms.port", LeagueProxy.RmsPort);
        SetKey(configObject, "chat.port", LeagueProxy.ChatPort);
        SetKey(configObject, "keystone.player-affinity.playerAffinityServiceURL", $"http://127.0.0.1:{LeagueProxy.GeopassPort}");
        SetKey(configObject, "lol.client_settings.league_edge.url", $"http://127.0.0.1:{LeagueProxy.LedgePort}");
        SetKey(configObject, "lol.client_settings.client_navigability.base_url", $"http://127.0.0.1:{LeagueProxy.LcuNavPort}");
        SetKey(configObject, "lol.client_settings.player_platform_edge.url", $"http://127.0.0.1:{LeagueProxy.PlatformPort}");

        SetKey(configObject, "lol.client_settings.remedy.obfuscated_player_name", false);
        SetKey(configObject, "lion.analytics.enable", false);
        SetKey(configObject, "chat.allow_bad_cert.enabled", true);
        SetKey(configObject, "chat.host", "127.0.0.1");
        SetKey(configObject, "chat.use_tls.enabled", false);
        SetKey(configObject, "chat.force_filter.enabled", false);
        SetKey(configObject, "keystone.client.feature_flags.chrome_devtools.enabled", true);
        SetKey(configObject, "keystone.riotgamesapi.telemetry.endpoint.send_deprecated", false);
        SetKey(configObject, "keystone.riotgamesapi.telemetry.endpoint.send_failure", false);
        SetKey(configObject, "keystone.riotgamesapi.telemetry.endpoint.send_success", false);
        SetKey(configObject, "keystone.telemetry.metrics_enabled", false);
        SetKey(configObject, "keystone.telemetry.newrelic_events_v2_enabled", false);
        SetKey(configObject, "keystone.telemetry.newrelic_metrics_v1_enabled", false);
        SetKey(configObject, "keystone.telemetry.newrelic_schemaless_events_v2_enabled", false);
        SetKey(configObject, "lol.client_settings.metrics.enabled", false);
        SetKey(configObject, "lol.client_settings.player_behavior.display_v1_ban_notifications", true);
        SetKey(configObject, "lol.game_client_settings.logging.enable_http_public_logs", false);
        SetKey(configObject, "lol.game_client_settings.logging.enable_rms_public_logs", false);
        SetKey(configObject, "keystone.riotgamesapi.telemetry.enable_redaction", false);
        SetKey(configObject, "keystone.age_restriction.enabled", false);
        SetKey(configObject, "keystone.client.feature_flags.lifecycle.backgroundRunning.enabled", false);
        SetKey(configObject, "keystone.client.feature_flags.cpu_memory_warning_report.enabled", false);
        SetKey(configObject, "keystone.client.feature_flags.launch_on_computer_start.enabled", false);
        SetKey(configObject, "keystone.client.feature_flags.open_telemetry_sender.enabled", false);
        SetKey(configObject, "keystone.client.feature_flags.quick_actions.enabled", true);
        SetKey(configObject, "keystone.client.feature_flags.self_update_in_background.enabled", false);
        SetKey(configObject, "keystone.client_config.diagnostics_enabled", false);
        SetKey(configObject, "keystone.telemetry.heartbeat_custom_metrics", false);
        SetKey(configObject, "keystone.riotgamesapi.telemetry.heartbeat_products", false);
        SetKey(configObject, "keystone.riotgamesapi.telemetry.heartbeat_voice_chat_metrics", false);
        SetKey(configObject, "keystone.riotgamesapi.telemetry.newrelic_events_v2_enabled", false);
        SetKey(configObject, "keystone.riotgamesapi.telemetry.newrelic_metrics_v1_enabled", false);
        SetKey(configObject, "keystone.riotgamesapi.telemetry.newrelic_schemaless_events_v2_enabled", false);
        SetKey(configObject, "keystone.riotgamesapi.telemetry.opentelemetry_events_enabled", false);
        SetKey(configObject, "keystone.riotgamesapi.telemetry.opentelemetry_uri_events", "");
        SetKey(configObject, "keystone.riotgamesapi.telemetry.singular_v1_enabled", false);
        SetKey(configObject, "keystone.telemetry.heartbeat_products", false);
        SetKey(configObject, "keystone.telemetry.heartbeat_voice_chat_metrics", false);
        SetKey(configObject, "keystone.telemetry.send_error_telemetry_metrics", false);
        SetKey(configObject, "keystone.telemetry.send_product_session_start_metrics", false);
        SetKey(configObject, "keystone.telemetry.singular_v1_enabled", false);
        SetKey(configObject, "lol.client_settings.startup.should_wait_for_home_hubs", false);
        SetKey(configObject, "lol.game_client_settings.app_config.singular_enabled", false);
        SetKey(configObject, "lol.game_client_settings.low_memory_reporting_enabled", false);
        SetKey(configObject, "lol.game_client_settings.missions.enabled", false);
        SetKey(configObject, "lol.game_client_settings.cap_orders_metrics_enabled", false);
        SetKey(configObject, "lol.game_client_settings.platform_stats_enabled", false);
        SetKey(configObject, "lol.game_client_settings.telemetry.standalone.long_frame_cooldown", 999);
        SetKey(configObject, "lol.game_client_settings.telemetry.standalone.long_frame_min_time", 99999);
        SetKey(configObject, "lol.game_client_settings.telemetry.standalone.nr_sample_rate", 0);
        SetKey(configObject, "lol.game_client_settings.telemetry.standalone.sample_rate", 0);
        SetKey(configObject, "rms.host", "ws://127.0.0.1");
        SetKey(configObject, "rms.allow_bad_cert.enabled", true);
        SetNestedKeys(configObject, "lol.client_settings.opmon_relay", "isEnabled", false);
        SetNestedKeys(configObject, "lol.client_settings.opmon_relay", "sessionSamplePercent", 0);
        SetNestedKeys(configObject, "lol.client_settings.errormonitor", "isEnabled", false);
        SetNestedKeys(configObject, "lol.client_settings.errormonitor", "isEnabledFoundation", false);
        SetNestedKeys(configObject, "lol.client_settings.errormonitor", "isEnabledUX", false);
        SetNestedKeys(configObject, "lol.client_settings.datadog_rum_config", "applicationID", "");
        SetNestedKeys(configObject, "lol.client_settings.datadog_rum_config", "clientToken", "");
        SetNestedKeys(configObject, "lol.client_settings.datadog_rum_config", "isEnabled", false);
        SetNestedKeys(configObject, "lol.client_settings.datadog_rum_config", "service", "");
        SetNestedKeys(configObject, "lol.client_settings.datadog_rum_config", "sessionReplaySampleRate", 0);
        SetNestedKeys(configObject, "lol.client_settings.datadog_rum_config", "sessionSampleRate", 0);
        SetNestedKeys(configObject, "lol.client_settings.datadog_rum_config", "site", "");
        SetNestedKeys(configObject, "lol.client_settings.datadog_rum_config", "telemetrySampleRate", 0);
        SetNestedKeys(configObject, "lol.client_settings.datadog_rum_config", "traceSampleRate", 0);
        SetNestedKeys(configObject, "lol.client_settings.datadog_rum_config", "trackLongTasks", false);
        SetNestedKeys(configObject, "lol.client_settings.datadog_rum_config", "trackResources", false);
        SetNestedKeys(configObject, "lol.client_settings.datadog_rum_config", "trackUserInteractions", false);
        SetNestedKeys(configObject, "lol.client_settings.sentry_config", "isEnabled", false);
        SetNestedKeys(configObject, "lol.client_settings.sentry_config", "sampleRate", 0);
        SetNestedKeys(configObject, "lol.client_settings.sentry_config", "dsn", "");
        SetEmptyArrayForConfig(configObject, "chat.xmpp_stanza_response_telemetry_allowed_codes");
        SetEmptyArrayForConfig(configObject, "chat.xmpp_stanza_response_telemetry_allowed_iqids");

        payloadStr = JsonSerializer.Serialize(configObject);
        return Encoding.UTF8.GetBytes(payloadStr);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
    }

    private static void StartBackgroundTaskForRmsAffinities(JsonObject rmsAffinities)
    {
        Task.Run(() =>
        {
            string? userRegionRms = null;
            while (string.IsNullOrEmpty(userRegionRms))
            {
                userRegionRms = GeopassHandlerRms.GetUserRegion();
                Thread.Sleep(100);
            }

            if (!string.IsNullOrEmpty(userRegionRms) && rmsAffinities[userRegionRms] is JsonNode tempRmsHost)
            {
                RmsHost = tempRmsHost.ToString();
            }
        });
    }

    private static void StartBackgroundTaskForChatAffinities(JsonObject chatAffinities)
    {
        Task.Run(() =>
        {
            string? userRegion = null;
            while (string.IsNullOrEmpty(userRegion))
            {
                userRegion = GeopassHandlerChat.GetUserRegion();
                Thread.Sleep(100);
            }
            if (!string.IsNullOrEmpty(userRegion) && chatAffinities[userRegion] is JsonNode tempChatHost)
            {
                ChatHost = tempChatHost.ToString();
            }
        });
    }

    private static void StartBackgroundTaskForMailboxAffinities(JsonObject mailboxAffinities)
    {
        Task.Run(() =>
        {
            string? userRegion = null;
            while (string.IsNullOrEmpty(userRegion))
            {
                userRegion = GeopassHandlerMailbox.GetUserRegion();
                Thread.Sleep(100);
            }
            if (!string.IsNullOrEmpty(userRegion) && mailboxAffinities[userRegion] is JsonNode tempMailboxHost)
            {
                MailboxUrl = tempMailboxHost.ToString();
            }
        });
    }

    private static void SetEmptyArrayForConfig(JsonNode? configObject, string configKey)
    {
        if (configObject?[configKey] is JsonArray)
        {
            configObject[configKey] = new JsonArray();
        }
        else if (configObject?[configKey] is JsonObject jsonObject)
        {
            jsonObject[configKey] = new JsonArray();
        }
    }

    private static void SetKey(JsonNode? configObject, string key, object value)
    {
        if (configObject?[key] != null)
        {
            try
            {
                configObject[key] = value switch
                {
                    bool boolValue => (JsonNode)boolValue,
                    int intValue => (JsonNode)intValue,
                    double doubleValue => (JsonNode)doubleValue,
                    string stringValue => (JsonNode)stringValue,
                    _ => throw new InvalidOperationException($"[ERROR] SetKey Unsupported type: {value.GetType()}"),
                };
            }
            catch (InvalidOperationException ex)
            {
                Trace.WriteLine($"{ex.Message}");
                throw;
            }
        }
    }
    private static void SetNestedKeys(JsonNode? configObject, string parentKey, string childKey, object value)
    {
        if (configObject == null) return;
        if (configObject?[parentKey] is JsonNode parentNode)
        {
            try
            {
                parentNode[childKey] = value switch
                {
                    bool boolValue => (JsonNode)boolValue,
                    string stringValue => (JsonNode)stringValue,
                    double doubleValue => (JsonNode)doubleValue,
                    int intValue => (JsonNode)intValue,
                    _ => throw new InvalidOperationException($"[ERROR] SetNestedKeys Unsupported type: {value.GetType()}"),
                };
            }
            catch (InvalidOperationException ex)
            {
                Trace.WriteLine($"{ex.Message}");
                throw;
            }
        }
    }

    private static string? DecodeAndGetRegion(string jwtToken)
    {
        try
        {
            var pasJwtContent = jwtToken.Split('.')[1];
            var validBase64 = pasJwtContent.PadRight((pasJwtContent.Length / 4 * 4) + (pasJwtContent.Length % 4 == 0 ? 0 : 4), '=');
            var pasJwtString = Encoding.UTF8.GetString(Convert.FromBase64String(validBase64));
            var pasJwtJson = JsonSerializer.Deserialize<JsonNode>(pasJwtString);
            var region = pasJwtJson?["affinity"]?.GetValue<string>();

            if (region == null)
            {
                Trace.WriteLine("[ERROR] JWT payload is malformed or missing 'affinity'.");
            }

            return region ?? throw new Exception("JWT payload is malformed or missing 'affinity'.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return null;
        }
    }

    public static class GeopassHandlerChat
    {
        private static string? _userRegion;

        public static void DecodeAndStoreUserRegion(string content)
        {
            _userRegion = DecodeAndGetRegion(content);
        }

        public static string GetUserRegion()
        {
            return _userRegion ?? "";
        }
    }
    public static class GeopassHandlerRms
    {
        private static string? _userRegion;

        public static void DecodeAndStoreUserRegion(string content)
        {
            _userRegion = DecodeAndGetRegion(content);
        }

        public static string GetUserRegion()
        {
            return _userRegion ?? "";
        }
    }
    public static class GeopassHandlerMailbox
    {
        private static string? _userRegion;

        public static void DecodeAndStoreUserRegion(string content)
        {
            _userRegion = DecodeAndGetRegion(content);
        }

        public static string GetUserRegion()
        {
            return _userRegion ?? "";
        }
    }
    public static class GeopassHandlerPBtoken
    {
        private static string? _userRegion;

        public static void DecodeAndStoreUserRegion(string content)
        {
            _userRegion = DecodeAndGetRegion(content);
        }

        public static string GetUserRegion()
        {
            return _userRegion ?? "";
        }
    }

    private static void ClientVersionOverride(JsonNode configObject, string patchline)
    {
        var productNode = configObject?[patchline];
        if (productNode is not null)
        {
            var configs = productNode["platforms"]?["win"]?["configurations"]?.AsArray();
            if (configs != null)
            {
                foreach (var config in configs)
                {
                    if (config?["patch_url"] is not null)
                    {
                        config["patch_url"] = "";
                    }

                    var patchArtifacts = config?["patch_artifacts"]?.AsArray();
                    if (patchArtifacts != null)
                    {
                        foreach (var artifact in patchArtifacts)
                        {
                            if (artifact?["type"]?.ToString() == "patch_url")
                            {
                                artifact["patch_url"] = "";
                            }
                        }
                    }
                    if (config != null)
                    {
                        config["launchable_on_update_fail"] = true;
                    }
                }
            }
        }
    }

    private static void RemoveVanguardDependencies(JsonNode? configObject, string path)
    {
        if (configObject == null) return;

        var productNode = configObject?[path];
        if (productNode is not null)
        {
            var configs = productNode?["platforms"]?["win"]?["configurations"]?.AsArray();
            if (configs is not null)
            {
                foreach (var config in configs)
                {
                    var dependencies = config?["dependencies"]?.AsArray();
                    var vanguard = dependencies?.FirstOrDefault(x => x!["id"]!.GetValue<string>() == "vanguard");
                    if (vanguard is not null)
                    {
                        dependencies?.Remove(vanguard);
                    }
                }
            }
        }
    }

    [GeneratedRegex(@"(?im)^Transfer-Encoding:\s*chunked\r\n")]
    private static partial Regex TransferEncoding();
    [GeneratedRegex(@"(?im)^Content-Length:\s*\d+\r\n")]
    private static partial Regex RemoveContentLenth();
    [GeneratedRegex(@"(?im)^Content-Encoding:\s*gzip\r\n")]
    private static partial Regex RemoveContentEncoding();
    [GeneratedRegex(@"(?<=\r\nHost: )[^\r\n]+")]
    private static partial Regex ReplaceHost();
    [GeneratedRegex(@"(?<=\r\nOrigin: )[^\r\n]+")]
    private static partial Regex ReplaceOrigin();
}
