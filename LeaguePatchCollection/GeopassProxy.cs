﻿using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Diagnostics;

namespace LeaguePatchCollection;
public partial class GeopassProxy
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private static readonly string[] separator = ["\r\n"];

    public async Task RunAsync(CancellationToken token)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _listener = new TcpListener(IPAddress.Any, LeagueProxy.GeopassPort);
        _listener.Start();

        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                _ = HandleClientAsync(client, token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Proxy Listener failed: {ex.Message}");
        }
        finally
        {
            Stop();
        }
    }

    private static async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        NetworkStream? clientStream = null;
        try
        {
            clientStream = client.GetStream();

            var buffer = new byte[8192];
            using MemoryStream requestStream = new();
            int bytesRead;
            bool headersComplete = false;
            int headerEndIndex = -1;
            byte[] headerTerminator = Encoding.UTF8.GetBytes("\r\n\r\n");

            while (!headersComplete && (bytesRead = await clientStream.ReadAsync(buffer, token)) > 0)
            {
                requestStream.Write(buffer, 0, bytesRead);
                headerEndIndex = IndexOf(requestStream, headerTerminator);
                if (headerEndIndex != -1)
                {
                    headersComplete = true;
                    break;
                }
            }
            if (!headersComplete)
            {
                return;
            }

            int headerSectionLength = headerEndIndex + headerTerminator.Length;

            string headersText = Encoding.UTF8.GetString(requestStream.GetBuffer(), 0, headerSectionLength);

            string[] requestLines = headersText.Split(separator, StringSplitOptions.RemoveEmptyEntries);
            string endpoint = string.Empty;
            if (requestLines.Length > 0)
            {
                string[] parts = requestLines[0].Split(' ');
                if (parts.Length > 1)
                {
                    endpoint = parts[1];
                }
            }

            int contentLength = 0;
            foreach (var line in requestLines)
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    string value = line["Content-Length:".Length..].Trim();
                    if (int.TryParse(value, out int len))
                    {
                        contentLength = len;
                    }
                }
            }

            int bodyBytesReceived = (int)(requestStream.Length - headerSectionLength);
            while (bodyBytesReceived < contentLength && (bytesRead = await clientStream.ReadAsync(buffer, token)) > 0)
            {
                requestStream.Write(buffer, 0, bytesRead);
                bodyBytesReceived += bytesRead;
            }

            byte[] fullRequestBytes = requestStream.ToArray();

            string? targetHost = ConfigProxy.GeopassUrl?.Replace("https://", "");
            if (string.IsNullOrEmpty(targetHost))
                throw new Exception("Target host is not ready yet.");

            using var serverClient = new TcpClient(targetHost, 443);
            using var sslStream = new SslStream(serverClient.GetStream(), false, (sender, certificate, chain, sslPolicyErrors) => true);
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                EnabledSslProtocols = SslProtocols.Tls12
            }, token);

            headersText = ReplaceHost().Replace(headersText, targetHost);
            headersText = ReplaceOrigin().Replace(headersText, $"https://{targetHost}");
            byte[] modifiedHeaderBytes = Encoding.UTF8.GetBytes(headersText);

            int bodyLength = fullRequestBytes.Length - headerSectionLength;
            byte[] bodyBytes = new byte[bodyLength];
            Array.Copy(fullRequestBytes, headerSectionLength, bodyBytes, 0, bodyLength);

            await sslStream.WriteAsync(modifiedHeaderBytes, token);
            if (bodyLength > 0)
            {
                await sslStream.WriteAsync(bodyBytes.AsMemory(0, bodyLength), token);
            }
            await sslStream.FlushAsync(token);

            await ForwardServerToClientAsync(sslStream, clientStream, endpoint, token);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ERROR] Client handling error: {ex.Message}");
        }
        finally
        {
            client?.Close();
            clientStream?.Dispose();
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
        var baseEndpoint = endpoint.Split('?')[0];

        var content = Encoding.UTF8.GetString(payload);

        if (baseEndpoint == "/pas/v1/service/chat")
        {
            ConfigProxy.GeopassHandlerChat.DecodeAndStoreUserRegion(content);
        }
        else if (baseEndpoint == "/pas/v1/service/rms")
        {
            ConfigProxy.GeopassHandlerRms.DecodeAndStoreUserRegion(content);
        }
        else if (baseEndpoint == "/pas/v1/service/mailbox")
        {
            ConfigProxy.GeopassHandlerMailbox.DecodeAndStoreUserRegion(content);
        }
        else if (baseEndpoint == "/pas/v1/service/discipline-tokenservice")
        {
            ConfigProxy.GeopassHandlerPBtoken.DecodeAndStoreUserRegion(content);
        }

        return payload;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
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