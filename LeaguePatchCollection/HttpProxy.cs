using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LeaguePatchCollection;

public partial class HttpProxy
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private static readonly string[] separator = ["\r\n"];
    private string? _configVarName;
    internal static string banReason = string.Empty;

    public async Task RunAsync(string configVarName, int listenPort, CancellationToken token)
    {
        _configVarName = configVarName;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, listenPort);
            _listener.Start();

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

    private string? GetTargetHost()
    {
        var prop = typeof(ConfigProxy).GetProperty(_configVarName,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);

        if (prop == null)
            return null;

        var value = prop.GetValue(null) as string;
        return value?.Replace("https://", "");
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using var clientStream = client.GetStream();
        TcpClient? serverClient = null;
        SslStream? sslStream = null;

        try
        {
            string? targetHost = GetTargetHost();
            if (string.IsNullOrEmpty(targetHost))
                throw new Exception("Target host is not set or is invalid.");

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
                    headerEndIndex = ProxyUtils.IndexOf(requestStream, headerTerminator);
                    if (headerEndIndex != -1)
                        headersComplete = true;
                }

                if (!headersComplete)
                    break;

                int headerLength = headerEndIndex + headerTerminator.Length;
                string headersText = Encoding.UTF8.GetString(requestStream.GetBuffer(), 0, headerLength);

                string[] requestLines = headersText.Split(separator, StringSplitOptions.RemoveEmptyEntries);
                string endpoint = requestLines.Length > 0 ? requestLines[0].Split(' ')[1] : string.Empty;

                if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Nobehavior && endpoint.Contains("credibility-behavior-warning"))
                {
                    string jsonBody = "[]";
                    byte[] customBytes = Encoding.UTF8.GetBytes(jsonBody);

                    string response = "HTTP/1.1 200 OK\r\n" +
                                      "Date: " + DateTime.UtcNow.ToString("r") + "\r\n" +
                                      "Content-Type: application/json;charset=UTF-8\r\n" +
                                      "Content-Length: " + customBytes.Length + "\r\n" +
                                      "Access-Control-Allow-Origin: *\r\n" +
                                      "Expires: 0\r\n" +
                                      "Cache-Control: no-cache\r\n" +
                                      "Connection: keep-alive\r\n\r\n";

                    byte[] headerBytes = Encoding.UTF8.GetBytes(response);

                    await clientStream.WriteAsync(headerBytes, token);
                    await clientStream.WriteAsync(customBytes, token);
                    await clientStream.FlushAsync(token);
                    continue;
                }
                else if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Nobloatware)
                {
                    if (endpoint.StartsWith("/publishing-content/v2.0/public/channel/league_of_legends_client/page/info-hub"))
                    {
                        string dateHeader = "Date: " + DateTime.UtcNow.ToString("r") + "\r\n";
                        string jsonBody = "{\"status\":{\"message\":\"Forbidden\",\"status_code\":403}}";
                        string contentLengthHeader = "Content-Length: " + Encoding.UTF8.GetByteCount(jsonBody) + "\r\n";
                        string contentTypeHeader = "Content-Type: application/json\r\n";

                        string forbiddenResponse = "HTTP/1.1 403 Forbidden\r\n" +
                                                   dateHeader +
                                                   contentTypeHeader +
                                                   contentLengthHeader +
                                                   "Connection: keep-alive\r\n\r\n" +
                                                   jsonBody;

                        byte[] responseBytes = Encoding.UTF8.GetBytes(forbiddenResponse);
                        await clientStream.WriteAsync(responseBytes, token);
                        await clientStream.FlushAsync(token);
                        return;
                    }
                }

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
        catch (Exception) { }
        finally
        {
            sslStream?.Dispose();
            serverClient?.Close();
            client.Close();
        }
    }

    private async Task ForwardServerToClientAsync(Stream serverStream, Stream clientStream, string endpoint, CancellationToken token)
    {
        byte[] headerBytes = await ProxyUtils.ReadHeadersAsync(serverStream, token);
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
            await ProxyUtils.ReadChunkedBodyAsync(serverStream, bodyStream, token);
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

    private static byte[] ModifyResponsePayload(byte[] payload, string endpoint)
    {
        if (endpoint.StartsWith("/pas/v1/service/chat"))
        {
            var content = Encoding.UTF8.GetString(payload);
            ConfigProxy.GeopassHandlerChat.DecodeAndStoreUserRegion(content);
        }
        else if (endpoint.StartsWith("/pas/v1/service/rms"))
        {
            var content = Encoding.UTF8.GetString(payload);
            ConfigProxy.GeopassHandlerRms.DecodeAndStoreUserRegion(content);
        }
        else if (endpoint.StartsWith("/pas/v1/service/mailbox"))
        {
            var content = Encoding.UTF8.GetString(payload);
            ConfigProxy.GeopassHandlerMailbox.DecodeAndStoreUserRegion(content);
        }
        else if (endpoint.StartsWith("/login-queue/v2/login/products/lol/regions/"))
        {
            try
            {
                string responseJson = Encoding.UTF8.GetString(payload);

                using var jsonDoc = JsonDocument.Parse(responseJson);
                if (!jsonDoc.RootElement.TryGetProperty("token", out _))
                {
                    banReason = responseJson; // Set ban reason as the full response
                }
            }
            catch (Exception ex)
            {
                banReason = "Error processing ban status.";
                Trace.WriteLine($"[ERROR] Failed to parse login queue response: {ex.Message}");
            }
        }
        else if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Nobloatware && endpoint.StartsWith("/publishing-content/v1.0/public/client-navigation/league_client_navigation/"))
        {
            string payloadStr = Encoding.UTF8.GetString(payload);
            var configObject = JsonSerializer.Deserialize<JsonNode>(payload);

            if (configObject?["data"] is JsonArray dataArray)
            {
                foreach (var item in dataArray)
                {
                    var title = item?["title"]?.ToString()?.Trim();
                }

                var itemsToRemove = dataArray
                    .Where(item =>
                    {
                        var title = item?["title"]?.ToString()?.Trim();
                        return !string.IsNullOrEmpty(title) &&
                               LeaguePatchCollectionUX.LatestBloatKeys
                                   .Any(k => string.Equals(k, title, StringComparison.OrdinalIgnoreCase));
                    })
                    .ToList();

                foreach (var item in itemsToRemove)
                {
                    dataArray.Remove(item);
                }
            }

            payloadStr = JsonSerializer.Serialize(configObject);
            return Encoding.UTF8.GetBytes(payloadStr);
        }
        else if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Nobehavior && endpoint.StartsWith("/leaverbuster-ledge/restrictionInfo"))
        {
            string payloadStr = Encoding.UTF8.GetString(payload);
            var configObject = JsonSerializer.Deserialize<JsonNode>(payload);

            if (configObject?["rankedRestrictionEntryDto"] is JsonNode rankedRestrictionEntryDto)
            {
                rankedRestrictionEntryDto["rankedRestrictionAckNeeded"] = false;
            }

            if (configObject?["leaverBusterEntryDto"] is JsonNode leaverBusterEntryDto)
            {
                leaverBusterEntryDto["preLockoutAckNeeded"] = false;
                leaverBusterEntryDto["onLockoutAckNeeded"] = false;
            }

            payloadStr = JsonSerializer.Serialize(configObject);
            return Encoding.UTF8.GetBytes(payloadStr);
        }
        else if (LeaguePatchCollectionUX.SettingsManager.ConfigSettings.Namebypass && endpoint.Contains("/summoners/summoner-ids"))
        {
            string payloadStr = Encoding.UTF8.GetString(payload);
            var configObject = JsonSerializer.Deserialize<JsonNode>(payload);

            if (configObject is JsonArray jsonArray)
            {
                foreach (var item in jsonArray)
                {
                    var summoner = item?.AsObject();
                    if (summoner is not null)
                    {
                        summoner["nameChangeFlag"] = false;
                        summoner["unnamed"] = false;
                    }
                }
            }

            payloadStr = JsonSerializer.Serialize(configObject);
            return Encoding.UTF8.GetBytes(payloadStr);
        }
        return payload;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
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