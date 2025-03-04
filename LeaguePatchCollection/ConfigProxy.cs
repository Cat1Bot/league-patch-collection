﻿using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using System.Diagnostics;
using EmbedIO.Utilities;
using System.Text.Json.Nodes;
using System.Text.Json;
using Swan.Logging;
using System.Net;
using System.IO.Compression;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using static System.Net.WebRequestMethods;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.Logging;

namespace LeaguePatchCollection;

class HttpProxy
{
    private static string _geopassUrl = string.Empty;
    private static string _lcuNavigationUrl = string.Empty;
    private static string _leagueEdgeUrl = string.Empty;
    public static string _rmsHost = string.Empty;
    public static string _chatHost = string.Empty;

    internal sealed class ConfigController : WebApiController
    {
        private static readonly HttpClient _Client = new(new HttpClientHandler
        {
            UseCookies = false,
            UseProxy = false,
            Proxy = null,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
            CheckCertificateRevocationList = false
        });
        private const string BASE_URL = "https://clientconfig.rpg.riotgames.com";

        [Route(HttpVerbs.Get, "/", true)]
        public async Task GetConfigPlayer()
        {
            var response = await ClientConfig(HttpContext.Request);
            var content = await response.Content.ReadAsStringAsync();

            if (HttpContext.Request.Url.LocalPath == "/api/v1/config/public")
            {
                var configObject = JsonSerializer.Deserialize<JsonNode>(content);

                var GeopassUrlNode = configObject?["keystone.player-affinity.playerAffinityServiceURL"];
                if (GeopassUrlNode != null)
                {
                    _geopassUrl = GeopassUrlNode.ToString();
                }

                var NavUrlNode = configObject?["lol.client_settings.client_navigability.base_url"];
                if (NavUrlNode != null)
                {
                    _lcuNavigationUrl = NavUrlNode.ToString();
                }

                if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Novgk)
                {
                    SetKey(configObject, "anticheat.vanguard.backgroundInstall", false);
                    SetKey(configObject, "anticheat.vanguard.enabled", false);
                    SetKey(configObject, "keystone.client.feature_flags.restart_required.disabled", true);
                    SetKey(configObject, "keystone.client.feature_flags.vanguardLaunch.disabled", true);
                    SetKey(configObject, "lol.client_settings.vanguard.enabled", false);
                    SetKey(configObject, "lol.client_settings.vanguard.enabled_embedded", false);
                    SetKey(configObject, "lol.client_settings.vanguard.url", "");
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
                    SetKey(configObject, "keystone.client.feature_flags.eula.use_patch_downloader.enabled", false);
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
                }

                if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Nobehavior)
                {
                    SetKey(configObject, "keystone.client.feature_flags.penaltyNotifications.enabled", false);
                    SetKey(configObject, "lol.client_settings.reputation_based_honor_enabled", false);
                }

                if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.NoStore)
                {
                    SetKey(configObject, "lol.client_settings.navigation.enableRewardsProgram", false);
                    SetKey(configObject, "lol.client_settings.store.lcu.enableCodesPage", false);
                    SetKey(configObject, "lol.client_settings.store.lcu.enableFetchOffers", false);
                    SetKey(configObject, "lol.client_settings.store.lcu.enableGifting", false);
                    SetKey(configObject, "lol.client_settings.store.enableGiftingMessages", false);
                    SetKey(configObject, "lol.client_settings.store.lcu.enableHextechItems", false);
                    SetKey(configObject, "lol.client_settings.store.lcu.enableRPPurchase", false);
                    SetKey(configObject, "lol.client_settings.store.lcu.enableTransfers", false);
                    SetKey(configObject, "lol.client_settings.store.lcu.enabled", false);
                    SetKey(configObject, "lol.client_settings.store.lcu.playerGiftingNotificationsEnabled", false);
                    SetKey(configObject, "lol.client_settings.store.lcu.useRMS", false);
                    SetKey(configObject, "lol.client_settings.store.customPageFiltersMap", false);
                    SetKey(configObject, "lol.client_settings.store.use_local_storefront", false);
                    SetKey(configObject, "lol.game_client_settings.starshards_purchase_enabled", false);
                    SetKey(configObject, "lol.game_client_settings.starshards_services_enabled", false);
                    SetKey(configObject, "lol.game_client_settings.store_enabled", false);
                    SetNestedKeys(configObject, "lol.client_settings.store.essenceEmporium", "Enabled", false);
                    SetEmptyArrayForConfig(configObject, "lol.client_settings.store.navTabs");
                    SetEmptyArrayForConfig(configObject, "lol.client_settings.store.allowedPurchaseWidgetTypes");
                    SetEmptyArrayForConfig(configObject, "lol.client_settings.store.bundlesUsingPaw");
                    SetEmptyArrayForConfig(configObject, "lol.client_settings.store.customPageFiltersMap");
                }

                SetKey(configObject, "keystone.age_restriction.enabled", false);
                SetKey(configObject, "keystone.client.feature_flags.lifecycle.backgroundRunning.enabled", false);
                SetKey(configObject, "keystone.client.feature_flags.cpu_memory_warning_report.enabled", false);
                SetKey(configObject, "keystone.client.feature_flags.launch_on_computer_start.enabled", false);
                SetKey(configObject, "keystone.client.feature_flags.open_telemetry_sender.enabled", false);
                SetKey(configObject, "keystone.client.feature_flags.pcbang_vanguard_restart_bypass.disabled", true);
                SetKey(configObject, "keystone.client.feature_flags.quick_actions.enabled", true);
                SetKey(configObject, "keystone.client.feature_flags.self_update_in_background.enabled", false);
                SetKey(configObject, "keystone.client_config.diagnostics_enabled", false);
                SetKey(configObject, "keystone.player-affinity.playerAffinityServiceURL", "http://127.0.0.1:29151");
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
                SetKey(configObject, "lol.client_settings.client_navigability.base_url", "http://127.0.0.1:29159");
                SetKey(configObject, "lol.client_settings.startup.should_wait_for_home_hubs", false);
                SetKey(configObject, "lol.game_client_settings.app_config.singular_enabled", false);
                SetKey(configObject, "lol.game_client_settings.low_memory_reporting_enabled", false);
                SetKey(configObject, "lol.game_client_settings.missions.enabled", false);
                SetKey(configObject, "patcher.scd.service_enabled", false);
                SetKey(configObject, "lol.game_client_settings.cap_orders_metrics_enabled", false);
                SetKey(configObject, "lol.game_client_settings.platform_stats_enabled", false);
                SetKey(configObject, "lol.game_client_settings.telemetry.standalone.long_frame_cooldown", 999);
                SetKey(configObject, "lol.game_client_settings.telemetry.standalone.long_frame_min_time", 99999);
                SetKey(configObject, "lol.game_client_settings.telemetry.standalone.nr_sample_rate", 0);
                SetKey(configObject, "lol.game_client_settings.telemetry.standalone.sample_rate", 0);
                SetKey(configObject, "rms.host", "ws://127.0.0.1");
                SetKey(configObject, "rms.port", 29155);
                SetKey(configObject, "rms.allow_bad_cert.enabled", true);
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
                //ClientVersionOverride(configObject, "keystone.products.league_of_legends.patchlines.live");
                //AppendLauncherArguments(configObject, "keystone.products.league_of_legends.patchlines.live");

                content = JsonSerializer.Serialize(configObject);
            }
            if (HttpContext.Request.Url.LocalPath == "/api/v1/config/player")
            {
                var configObject = JsonSerializer.Deserialize<JsonNode>(content);

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
                    _leagueEdgeUrl = leagueEdgeUrlNode.ToString();
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

                if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.NoStore)
                {
                    SetNestedKeys(configObject, "lol.client_settings.event_hub.activation", "hubEnabled", false);
                    SetNestedKeys(configObject, "lol.client_settings.yourshop", "Active", false);
                }

                if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Nobehavior)
                {
                    SetKey(configObject, "keystone.client.feature_flags.gaWarning.enabled", false);
                    SetKey(configObject, "chat.require_pbtoken_for_muc.enabled", false);
                    SetKey(configObject, "chat.send_restrictions_messages_mid_chat.enabled", false);
                    SetKey(configObject, "chat.send_restrictions_messages_on_muc_entry.enabled", false);
                    SetKey(configObject, "keystone.client.feature_flags.playerBehaviorToken.enabled", false);
                    SetKey(configObject, "keystone.client.feature_flags.playerReporting.enabled", false);
                    SetKey(configObject, "keystone.client.feature_flags.restriction.enabled", false);
                }

                if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Nobloatware)
                {
                    SetNestedKeys(configObject, "lol.client_settings.league_edge.enabled_services", "Missions", false);
                    SetNestedKeys(configObject, "lol.client_settings.deepLinks", "launchLorEnabled", false);
                    SetKey(configObject, "chat.disable_chat_restriction_muted_system_message", true);
                    SetKey(configObject, "keystone.client.feature_flags.home_page_route.enabled", false);
                    SetKey(configObject, "keystone.client.feature_flags.campaign-hub.enabled", false);
                    SetKey(configObject, "keystone.client.feature_flags.playerReporting.enabled", false);

                    if (configObject?["lol.client_settings.nacho.active_banners"] is JsonObject activeBannersConfig &&
                        activeBannersConfig.ContainsKey("enabled"))
                    {
                        activeBannersConfig["enabled"] = false;
                    }
                }

                SetKey(configObject, "chat.allow_bad_cert.enabled", true);
                SetKey(configObject, "chat.host", "127.0.0.1");
                SetKey(configObject, "chat.port", 29153);
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
                SetKey(configObject, "lol.client_settings.league_edge.url", "http://127.0.0.1:29152");
                SetKey(configObject, "lol.client_settings.metrics.enabled", false);
                SetKey(configObject, "lol.client_settings.player_behavior.display_v1_ban_notifications", true);
                SetKey(configObject, "lol.game_client_settings.logging.enable_http_public_logs", false);
                SetKey(configObject, "lol.game_client_settings.logging.enable_rms_public_logs", false);
                SetEmptyArrayForConfig(configObject, "chat.xmpp_stanza_response_telemetry_allowed_codes");
                SetEmptyArrayForConfig(configObject, "chat.xmpp_stanza_response_telemetry_allowed_iqids");

                content = JsonSerializer.Serialize(configObject);
            }

            await SendResponse(response, content);
        }
        private static async Task<HttpResponseMessage> ClientConfig(IHttpRequest request)
        {
            var url = BASE_URL + request.RawUrl;

            using var message = new HttpRequestMessage(HttpMethod.Get, url);

            if (request.Headers["accept-encoding"] is not null)
                message.Headers.TryAddWithoutValidation("Accept-Encoding", request.Headers["accept-encoding"]);

            message.Headers.TryAddWithoutValidation("user-agent", request.Headers["user-agent"]);

            if (request.Headers["x-riot-entitlements-jwt"] is not null)
                message.Headers.TryAddWithoutValidation("X-Riot-Entitlements-JWT", request.Headers["x-riot-entitlements-jwt"]);

            if (request.Headers["authorization"] is not null)
                message.Headers.TryAddWithoutValidation("Authorization", request.Headers["authorization"]);

            if (request.Headers["x-riot-rso-identity-jwt"] is not null)
                message.Headers.TryAddWithoutValidation("X-Riot-RSO-Identity-JWT", request.Headers["x-riot-rso-identity-jwt"]);

            if (request.Headers["baggage"] is not null)
                message.Headers.TryAddWithoutValidation("baggage", request.Headers["baggage"]);

            if (request.Headers["traceparent"] is not null)
                message.Headers.TryAddWithoutValidation("traceparent", request.Headers["traceparent"]);

            message.Headers.TryAddWithoutValidation("Accept", "application/json");

            var response = await _Client.SendAsync(message);

            return response;
        }

        private async Task SendResponse(HttpResponseMessage response, string content)
        {
            var responseBuffer = Encoding.UTF8.GetBytes(content);

            HttpContext.Response.SendChunked = false;
            HttpContext.Response.ContentType = "application/json";
            HttpContext.Response.ContentLength64 = responseBuffer.Length;
            HttpContext.Response.StatusCode = (int)response.StatusCode;

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Trace.WriteLine($"[ERROR] Request to {HttpContext.Request.Url.LocalPath} returned {response.StatusCode}");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {HttpContext.Request.Url.LocalPath} returned 403 Forbidden; possibly blocked by Cloudflare.");
                Console.ResetColor();
            }

            await HttpContext.Response.OutputStream.WriteAsync(responseBuffer);
            HttpContext.Response.OutputStream.Close();
        }
    }
    internal sealed class LedgeController : WebApiController
    {
        private readonly static HttpClient _Client = new(new HttpClientHandler
        {
            UseCookies = false,
            UseProxy = false,
            Proxy = null,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
            CheckCertificateRevocationList = false
        });
        private static string LEDGE_URL => _leagueEdgeUrl;

        [Route(HttpVerbs.Get, "/", true)]
        public async Task GetLedge()
        {
            if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Nobloatware)
            {
                if (HttpContext.Request.Url.LocalPath == "/leagues-ledge/v2/notifications" ||
                HttpContext.Request.Url.LocalPath.StartsWith("/catalog/v1/products/"))
                {
                    return;
                }
            }

            string requestBody;
            using (var reader = new StreamReader(HttpContext.OpenRequestStream()))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            var response = await GetLedge(HttpContext.Request);
            var content = await response.Content.ReadAsStringAsync();

            if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Nobehavior)
            {
                if (HttpContext.Request.Url.LocalPath == "/honor-edge/v2/retrieveProfileInfo/")
                {
                    var configObject = JsonSerializer.Deserialize<JsonNode>(content);

                    SetKey(configObject, "honorLevel", 5);
                    SetKey(configObject, "checkpoint", 0);
                    SetKey(configObject, "rewardsLocked", false);
                    SetEmptyArrayForConfig(configObject, "redemptions");

                    content = JsonSerializer.Serialize(configObject);
                }
                if (HttpContext.Request.Url.LocalPath == "/leaverbuster-ledge/restrictionInfo")
                {
                    var configObject = JsonSerializer.Deserialize<JsonNode>(content);

                    SetNestedKeys(configObject, "rankedRestrictionEntryDto", "rankedRestrictionAckNeeded", false);
                    SetNestedKeys(configObject, "leaverBusterEntryDto", "preLockoutAckNeeded", false);
                    SetNestedKeys(configObject, "leaverBusterEntryDto", "onLockoutAckNeeded", false);
                    content = JsonSerializer.Serialize(configObject);
                }
            }

            await SendResponse(response, content);
        }

        [Route(HttpVerbs.Options, "/", true)]
        public async Task OptionsLedge()
        {
            var response = await OptionsLedge(HttpContext.Request);
            var content = await response.Content.ReadAsStringAsync();


            await SendResponse(response, content);
        }

        [Route(HttpVerbs.Post, "/", true)]
        public async Task PostLedge()
        {
            string requestBody;
            using (var reader = new StreamReader(HttpContext.OpenRequestStream()))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            var response = await PostLedge(HttpContext.Request, requestBody);
            var content = await response.Content.ReadAsStringAsync();


            await SendResponse(response, content);
        }
        [Route(HttpVerbs.Put, "/", true)]
        public async Task PutLedge()
        {
            string requestBody;
            using (var reader = new StreamReader(HttpContext.OpenRequestStream()))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            var response = await PutLedge(HttpContext.Request, requestBody);
            var content = await response.Content.ReadAsStringAsync();


            await SendResponse(response, content);
        }
        private static async Task<HttpResponseMessage> PutLedge(IHttpRequest request, string body)
        {
            var url = LEDGE_URL + request.RawUrl;

            using var message = new HttpRequestMessage(HttpMethod.Put, url);

            if (request.Headers["accept-encoding"] is not null)
                message.Headers.TryAddWithoutValidation("Accept-Encoding", request.Headers["accept-encoding"]);

            message.Headers.TryAddWithoutValidation("user-agent", request.Headers["user-agent"]);

            if (request.Headers["authorization"] is not null)
                message.Headers.TryAddWithoutValidation("Authorization", request.Headers["authorization"]);

            message.Headers.TryAddWithoutValidation("Content-type", "application/json");

            message.Headers.TryAddWithoutValidation("Accept", "application/json");

            if (!string.IsNullOrEmpty(body))
                message.Content = new StringContent(body, Encoding.UTF8, "application/json");

            if (request.Headers["content-length"] is not null)
            {
                if (long.TryParse(request.Headers["content-length"], out var contentLength))
                    message.Content.Headers.ContentLength = contentLength;
            }

            var response = await _Client.SendAsync(message);

            return response;
        }
        private static async Task<HttpResponseMessage> PostLedge(IHttpRequest request, string body)
        {
            var url = LEDGE_URL + request.RawUrl;

            using var message = new HttpRequestMessage(HttpMethod.Post, url);

            if (request.Headers["accept-encoding"] is not null)
                message.Headers.TryAddWithoutValidation("Accept-Encoding", request.Headers["accept-encoding"]);

            message.Headers.TryAddWithoutValidation("user-agent", request.Headers["user-agent"]);

            if (request.Headers["authorization"] is not null)
                message.Headers.TryAddWithoutValidation("Authorization", request.Headers["authorization"]);

            if (request.Headers["content-type"] is not null)
            {
                message.Content = new StringContent(body, null, request.Headers["content-type"]);
            }

            message.Headers.TryAddWithoutValidation("Accept", "application/json");

            if (request.Headers["content-length"] is not null)
            {
                if (long.TryParse(request.Headers["content-length"], out var contentLength))
                    message.Content.Headers.ContentLength = contentLength;
            }

            var response = await _Client.SendAsync(message);

            return response;
        }
        private static async Task<HttpResponseMessage> OptionsLedge(IHttpRequest request)
        {
            var url = LEDGE_URL + request.RawUrl;

            using var message = new HttpRequestMessage(HttpMethod.Options, url);

            if (request.Headers["connection"] is not null)
                message.Headers.TryAddWithoutValidation("Connection", request.Headers["connection"]);

            if (request.Headers["accept"] is not null)
                message.Headers.TryAddWithoutValidation("Accept", request.Headers["accept"]);

            if (request.Headers["access-control-request-method"] is not null)
                message.Headers.TryAddWithoutValidation("Access-Control-Request-Method", request.Headers["access-control-request-method"]);

            if (request.Headers["access-control-request-headers"] is not null)
                message.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", request.Headers["access-control-request-headers"]);

            if (request.Headers["origin"] is not null)
            {
                var sharedLedgeUrl = _leagueEdgeUrl;
                message.Headers.TryAddWithoutValidation("Origin", sharedLedgeUrl);
            }

            message.Headers.TryAddWithoutValidation("user-agent", request.Headers["user-agent"]);

            if (request.Headers["sec-fetch-mode"] is not null)
                message.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", request.Headers["sec-fetch-mode"]);

            if (request.Headers["sec-fetch-site"] is not null)
                message.Headers.TryAddWithoutValidation("Sec-Fetch-Site", request.Headers["sec-fetch-site"]);

            if (request.Headers["sec-fetch-dest"] is not null)
                message.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", request.Headers["sec-fetch-dest"]);

            if (request.Headers["accept-encoding"] is not null)
                message.Headers.TryAddWithoutValidation("Accept-Encoding", request.Headers["accept-encoding"]);

            if (request.Headers["accept-language"] is not null)
                message.Headers.TryAddWithoutValidation("Accept-Language", request.Headers["accept-language"]);

            var response = await _Client.SendAsync(message);

            return response;
        }
        private static async Task<HttpResponseMessage> GetLedge(IHttpRequest request)
        {
            var url = LEDGE_URL + request.RawUrl;

            using var message = new HttpRequestMessage(HttpMethod.Get, url);

            if (request.Headers["connection"] is not null)
                message.Headers.TryAddWithoutValidation("Connection", request.Headers["connection"]);

            if (request.Headers["sec-ch-ua"] is not null)
                message.Headers.TryAddWithoutValidation("sec-ch-ua", request.Headers["sec-ch-ua"]);

            if (request.Headers["sec-ch-ua-mobile"] is not null)
                message.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", request.Headers["sec-ch-ua-mobile"]);

            if (request.Headers["accept-encoding"] is not null)
                message.Headers.TryAddWithoutValidation("Accept-Encoding", request.Headers["accept-encoding"]);

            message.Headers.TryAddWithoutValidation("user-agent", request.Headers["user-agent"]);

            if (request.Headers["authorization"] is not null)
                message.Headers.TryAddWithoutValidation("Authorization", request.Headers["authorization"]);

            if (request.Headers["sec-ch-ua-platform"] is not null)
                message.Headers.TryAddWithoutValidation("sec-ch-ua-platform", request.Headers["sec-ch-ua-platform"]);

            if (request.Headers["origin"] is not null)
            {
                var sharedLedgeUrl = _leagueEdgeUrl;
                message.Headers.TryAddWithoutValidation("Origin", sharedLedgeUrl);
            }

            if (request.Headers["sec-fetch-site"] is not null)
                message.Headers.TryAddWithoutValidation("Sec-Fetch-Site", request.Headers["sec-fetch-site"]);

            if (request.Headers["sec-fetch-mode"] is not null)
                message.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", request.Headers["sec-fetch-mode"]);

            if (request.Headers["sec-fetch-dest"] is not null)
                message.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", request.Headers["sec-fetch-dest"]);

            if (request.Headers["accept-language"] is not null)
                message.Headers.TryAddWithoutValidation("Accept-Language", request.Headers["accept-language"]);

            if (request.Headers["Content-type"] is not null)
                message.Headers.TryAddWithoutValidation("Content-Type", request.Headers["Content-type"]);

            if (request.Headers["accept"] is not null)
                message.Headers.TryAddWithoutValidation("Accept", request.Headers["accept"]);

            var response = await _Client.SendAsync(message);

            return response;
        }
        private async Task SendResponse(HttpResponseMessage response, string content)
        {
            var responseBuffer = Encoding.UTF8.GetBytes(content);

            HttpContext.Response.SendChunked = false;
            HttpContext.Response.ContentType = "application/json";
            HttpContext.Response.ContentLength64 = responseBuffer.Length;
            HttpContext.Response.StatusCode = (int)response.StatusCode;

            HttpContext.Response.Headers["Access-Control-Allow-Origin"] = "*"; // Allows all origins; restrict as needed
            HttpContext.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS"; // Allowed HTTP methods
            HttpContext.Response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type"; // Allowed headers

            if (HttpContext.Request.HttpMethod == "OPTIONS")
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.OK;
                await HttpContext.Response.OutputStream.FlushAsync();
                return;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                Trace.WriteLine("[WARN] Ledge request returned 403 Forbidden; possibly blocked by Cloudflare.");
            }

            await HttpContext.Response.OutputStream.WriteAsync(responseBuffer);
            HttpContext.Response.OutputStream.Close();
        }
    }
    internal sealed class GeopassController : WebApiController
    {
        private readonly static HttpClient _Client = new(new HttpClientHandler
        {
            UseCookies = false,
            UseProxy = false,
            Proxy = null,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
            CheckCertificateRevocationList = false
        });
        private static string GEOPASS_URL => _geopassUrl;

        [Route(HttpVerbs.Get, "/", true)]
        public async Task GetGeopass()
        {

            var response = await GetGeopass(HttpContext.Request);
            var content = await response.Content.ReadAsStringAsync();

            if (HttpContext.Request.Url.LocalPath == "/pas/v1/service/chat")
            {
                GeopassHandlerChat.DecodeAndStoreUserRegion(content);
                Trace.WriteLine("[INFO] Chat JWT sucessfully passed to decoder");
            }
            if (HttpContext.Request.Url.LocalPath == "/pas/v1/service/rms")
            {
                GeopassHandlerRms.DecodeAndStoreUserRegion(content);
                Trace.WriteLine("[INFO] RMS JWT sucessfully passed to decoder");
            }

            await SendResponse(response, content);
        }
        [Route(HttpVerbs.Put, "/", true)]
        public async Task PutGeopass()
        {
            string requestBody;
            using (var reader = new StreamReader(HttpContext.OpenRequestStream()))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            var response = await PutGeopass(HttpContext.Request, requestBody);
            var content = await response.Content.ReadAsStringAsync();


            await SendResponse(response, content);
        }
        private async Task<HttpResponseMessage> GetGeopass(IHttpRequest request)
        {
            var url = GEOPASS_URL + request.RawUrl;

            using var message = new HttpRequestMessage(HttpMethod.Get, url);

            if (request.Headers["accept-encoding"] is not null)
                message.Headers.TryAddWithoutValidation("Accept-Encoding", request.Headers["accept-encoding"]);

            message.Headers.TryAddWithoutValidation("user-agent", HttpContext.Request.Headers["user-agent"]);

            if (request.Headers["authorization"] is not null)
                message.Headers.TryAddWithoutValidation("Authorization", request.Headers["authorization"]);

            if (request.Headers["x-pas-affinity-hint"] is not null)
                message.Headers.TryAddWithoutValidation("X-PAS-affinity-hint", request.Headers["x-pas-affinity-hint"]);

            if (request.Headers["baggage"] is not null)
                message.Headers.TryAddWithoutValidation("baggage", request.Headers["baggage"]);

            if (request.Headers["traceparent"] is not null)
                message.Headers.TryAddWithoutValidation("traceparent", request.Headers["traceparent"]);

            message.Headers.TryAddWithoutValidation("Accept", "application/json");

            var response = await _Client.SendAsync(message);

            return response;
        }
        private async Task<HttpResponseMessage> PutGeopass(IHttpRequest request, string body)
        {
            var url = GEOPASS_URL + request.RawUrl;

            using var message = new HttpRequestMessage(HttpMethod.Put, url);

            if (request.Headers["accept-encoding"] is not null)
                message.Headers.TryAddWithoutValidation("Accept-Encoding", request.Headers["accept-encoding"]);

            message.Headers.TryAddWithoutValidation("user-agent", HttpContext.Request.Headers["user-agent"]);

            if (request.Headers["authorization"] is not null)
                message.Headers.TryAddWithoutValidation("Authorization", request.Headers["authorization"]);

            if (request.Headers["baggage"] is not null)
                message.Headers.TryAddWithoutValidation("baggage", request.Headers["baggage"]);

            if (request.Headers["traceparent"] is not null)
                message.Headers.TryAddWithoutValidation("traceparent", request.Headers["traceparent"]);

            if (request.Headers["accept"] is not null)
                message.Headers.TryAddWithoutValidation("Accept", request.Headers["accept"]);

            if (request.Headers["content-length"] is not null)
                message.Headers.TryAddWithoutValidation("Content-Length", request.Headers["content-length"]);

            if (request.Headers["content-type"] is not null)
                message.Headers.TryAddWithoutValidation("Content-Type", request.Headers["content-type"]);

            string requestBody;
            using (var reader = new StreamReader(HttpContext.OpenRequestStream()))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            if (body != null)
            {
                message.Content = new StringContent(body, Encoding.UTF8, request.Headers["content-type"]);
            }
            var response = await _Client.SendAsync(message);

            return response;
        }
        private async Task SendResponse(HttpResponseMessage response, string content)
        {
            var responseBuffer = Encoding.UTF8.GetBytes(content);

            HttpContext.Response.SendChunked = false;
            HttpContext.Response.ContentType = "application/json";
            HttpContext.Response.ContentLength64 = responseBuffer.Length;
            HttpContext.Response.StatusCode = (int)response.StatusCode;

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Trace.WriteLine($"[ERROR] Request to {HttpContext.Request.Url.LocalPath} returned {response.StatusCode}");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {HttpContext.Request.Url.LocalPath} returned 403 Forbidden; possibly blocked by Cloudflare.");
                Console.ResetColor();
            }

            await HttpContext.Response.OutputStream.WriteAsync(responseBuffer);
            HttpContext.Response.OutputStream.Close();
        }
    }
    internal sealed class LcuContentController : WebApiController
    {
        private readonly static HttpClient _Client = new(new HttpClientHandler
        {
            UseCookies = false,
            UseProxy = false,
            Proxy = null,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
            CheckCertificateRevocationList = false
        });
        private static string LCUCONTENTURL => _lcuNavigationUrl;

        [Route(HttpVerbs.Get, "/", true)]
        public async Task GetPublishingLcu()
        {

            var response = await GetLcuContent(HttpContext.Request);
            var content = await response.Content.ReadAsStringAsync();

            if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Nobloatware)
            {
                if (HttpContext.Request.Url.LocalPath == "/publishing-content/v1.0/public/client-navigation/league_client_navigation/")
                {
                    var configObject = JsonSerializer.Deserialize<JsonNode>(content);

                    if (configObject?["data"] is JsonArray dataArray)
                    {
                        var itemToRemove = dataArray.FirstOrDefault(item => item?["title"]?.ToString() == LeaguePatchCollectionUX._latestBloatKey);

                        if (itemToRemove != null)
                        {
                            dataArray.Remove(itemToRemove);
                        }
                    }

                    content = JsonSerializer.Serialize(configObject);
                }
            }

                await SendResponse(response, content);
        }
        private async Task<HttpResponseMessage> GetLcuContent(IHttpRequest request)
        {
            var url = LCUCONTENTURL + request.RawUrl;

            using var message = new HttpRequestMessage(HttpMethod.Get, url);

            if (request.Headers["accept-encoding"] is not null)
                message.Headers.TryAddWithoutValidation("Accept-Encoding", request.Headers["accept-encoding"]);

            message.Headers.TryAddWithoutValidation("user-agent", HttpContext.Request.Headers["user-agent"]);

            message.Headers.TryAddWithoutValidation("Content-Type", "application/json");

            message.Headers.TryAddWithoutValidation("Accept", "application/json");


            var response = await _Client.SendAsync(message);

            return response;
        }
        private async Task SendResponse(HttpResponseMessage response, string content)
        {
            var responseBuffer = Encoding.UTF8.GetBytes(content);

            HttpContext.Response.SendChunked = false;
            HttpContext.Response.ContentType = "application/json";
            HttpContext.Response.ContentLength64 = responseBuffer.Length;
            HttpContext.Response.StatusCode = (int)response.StatusCode;

            if (response.StatusCode != HttpStatusCode.OK)
            {
                Trace.WriteLine($"[ERROR] Request to {HttpContext.Request.Url.LocalPath} returned {response.StatusCode}");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {HttpContext.Request.Url.LocalPath} returned 403 Forbidden; possibly blocked by Cloudflare.");
                Console.ResetColor();
            }

            await HttpContext.Response.OutputStream.WriteAsync(responseBuffer);
            HttpContext.Response.OutputStream.Close();
        }
    }

    internal sealed class HttpProxyServer<T> where T : WebApiController, new()
    {
        private readonly WebServer _WebServer;
        private readonly int _Port;

        public string Url => $"http://127.0.0.1:{_Port}";

        public HttpProxyServer(int port)
        {
            _Port = port;

            _WebServer = new WebServer(o => o
                    .WithUrlPrefix(Url)
                    .WithMode(HttpListenerMode.EmbedIO))
                    .WithWebApi("/", m => m
                        .WithController<T>()
                    );
        }

        public void Start(CancellationToken cancellationToken = default)
        {
            _WebServer.Start(cancellationToken);
        }

        public void Dispose()
        {
            _WebServer?.Dispose();
        }

        public Task RunAsync(CancellationToken cancellationToken = default)
        {
            return _WebServer.RunAsync(cancellationToken);
        }
    }
    private static void StartBackgroundTaskForRmsAffinities(JsonObject rmsAffinities)
    {
        Task.Run(() =>
        {
            Trace.WriteLine("[INFO] Received RMS Affinities:");

            string? userRegionRms = null;
            while (string.IsNullOrEmpty(userRegionRms))
            {
                userRegionRms = GeopassHandlerRms.GetUserRegion();
                Thread.Sleep(100);
            }

            if (!string.IsNullOrEmpty(userRegionRms) && rmsAffinities[userRegionRms] is JsonNode tempRmsHost)
            {
                Trace.WriteLine($"[INFO] RMS Host for user region '{userRegionRms}': {tempRmsHost}");
                _rmsHost = tempRmsHost.ToString();
            }
        });
    }
    private static void StartBackgroundTaskForChatAffinities(JsonObject chatAffinities)
    {
        Task.Run(() =>
        {
            Trace.WriteLine("[INFO] Received Chat Affinities:");

            string? userRegion = null;
            while (string.IsNullOrEmpty(userRegion))
            {
                userRegion = GeopassHandlerChat.GetUserRegion();
                Thread.Sleep(100);
            }
            if (!string.IsNullOrEmpty(userRegion) && chatAffinities[userRegion] is JsonNode tempChatHost)
            {
                Trace.WriteLine($"[INFO] Chat Host for user region '{userRegion}': {tempChatHost}");
                _chatHost = tempChatHost.ToString();
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
    static void SetKey(JsonNode? configObject, string key, object value)
    {
        if (configObject == null) return;

        if (configObject[key] != null)
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

    public static class JwtDecoder
    {
        public static string? DecodeAndGetRegion(string jwtToken)
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
    }

    public static class GeopassHandlerChat
    {
        private static string? _userRegion;

        public static void DecodeAndStoreUserRegion(string content)
        {
            _userRegion = JwtDecoder.DecodeAndGetRegion(content);
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
            _userRegion = JwtDecoder.DecodeAndGetRegion(content);
        }

        public static string GetUserRegion()
        {
            return _userRegion ?? "";
        }
    }
    static void ClientVersionOverride(JsonNode configObject, string patchline)
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
                        config["patch_url"] = "https://lol.secure.dyn.riotcdn.net/channels/public/releases/5465E4D9A61B4F5A.manifest";
                    }

                    var patchArtifacts = config?["patch_artifacts"]?.AsArray();
                    if (patchArtifacts != null)
                    {
                        foreach (var artifact in patchArtifacts)
                        {
                            if (artifact?["type"]?.ToString() == "patch_url")
                            {
                                artifact["patch_url"] = "https://lol.secure.dyn.riotcdn.net/channels/public/releases/5465E4D9A61B4F5A.manifest";
                            }
                        }
                    }
                    config["launchable_on_update_fail"] = true;
                }
            }
        }
    }
    static void AppendLauncherArguments(JsonNode? configObject, string patchline)
    {
        if (configObject == null) return;

        var productNode = configObject?[patchline];
        if (productNode?["platforms"] is JsonObject platforms)
        {
            foreach (var platform in platforms)
            {
                var configs = platform.Value?["configurations"]?.AsArray();
                if (configs != null)
                {
                    foreach (var config in configs)
                    {
                        var launcherArray = config?["launcher"]?["arguments"]?.AsArray();
                        launcherArray?.Add("--system-yaml-override=Config/system.yaml");
                    }
                }
            }
        }
    }
    static void RemoveVanguardDependencies(JsonNode? configObject, string path)
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
}
