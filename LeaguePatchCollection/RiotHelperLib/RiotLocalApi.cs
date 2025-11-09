using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace LeaguePatchCollection.RiotHelperLib;
public enum ApiTarget { Lcu, Rcs }

public static class RiotLocalApi
{
    private static async Task<string?> GetLockfileContent(ApiTarget target)
    {
        string lockfilePath;

        if (target == ApiTarget.Lcu)
        {
            if (!ProcessUtil.IsProcessRunning("LeagueClient"))
                return null;

            lockfilePath = Path.Combine("C:\\Riot Games\\League of Legends", "lockfile");
        }
        else
        {
            if (!ProcessUtil.IsProcessRunning("RiotClientServices")) return null;

        lockfilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Riot Games", "Riot Client", "Config", "lockfile");
        }

        if (!File.Exists(lockfilePath))
        {
            Trace.WriteLine(" [WARN] Lockfile does not exist or was not found.");
            return null;
        }

        try
        {
            return await LockfileReader.ReadLockfileAsync(lockfilePath);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($" [ERROR] Failed to access {target} lockfile: {ex.Message}");
            return null;
        }
    }

    public static async Task<HttpResponseMessage?> SendRequest(ApiTarget target, string endpoint, HttpMethod method, HttpContent? content = null)
    {
        string? lockfileContent = await GetLockfileContent(target);
        if (lockfileContent == null) return null;

        var parts = lockfileContent.Split(':');
        if (parts.Length != 5)
        {
            Trace.WriteLine($" [ERROR] {target} lockfile format is incorrect.");
            return null;
        }

        string port = parts[2];
        string password = parts[3];
        string authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{password}"));
        string host = "127.0.0.1";

        StringBuilder headerBuilder = new();
        headerBuilder.Append($"{method.Method} {endpoint} HTTP/1.1\r\n");
        headerBuilder.Append($"Host: {host}:{port}\r\n");
        headerBuilder.Append($"Authorization: Basic {authValue}\r\n");
        headerBuilder.Append("Accept: application/json\r\n");

        byte[] bodyBytes = [];
        if (content != null)
        {
            bodyBytes = await content.ReadAsByteArrayAsync();
            if (content.Headers.ContentType != null)
            {
                headerBuilder.Append($"Content-Type: {content.Headers.ContentType}\r\n");
            }
            headerBuilder.Append($"Content-Length: {bodyBytes.Length}\r\n");
        }
        else if (method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Delete)
        {
            headerBuilder.Append("Content-Length: 0\r\n");
        }

        headerBuilder.Append("\r\n");
        byte[] headerBytes = Encoding.UTF8.GetBytes(headerBuilder.ToString());

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(host, int.Parse(port));
            using var sslStream = new SslStream(tcpClient.GetStream(), false, (sender, cert, chain, errors) => true);

            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12
            });

            await sslStream.WriteAsync(headerBytes);
            if (bodyBytes.Length > 0)
                await sslStream.WriteAsync(bodyBytes);
            await sslStream.FlushAsync();

            using MemoryStream responseBuffer = new();
            byte[] readBuffer = new byte[8192];
            int bytesRead;
            string headersString = "";
            int contentLength = -1;
            while ((bytesRead = await sslStream.ReadAsync(readBuffer)) > 0)
            {
                responseBuffer.Write(readBuffer, 0, bytesRead);
                headersString = Encoding.UTF8.GetString(responseBuffer.GetBuffer(), 0, (int)responseBuffer.Length);

                int headerEndIndex = headersString.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEndIndex >= 0)
                {
                    string headerPart = headersString[..headerEndIndex];
                    foreach (string line in headerPart.Split("\r\n"))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            string value = line["Content-Length:".Length..].Trim();
                            if (int.TryParse(value, out int len))
                            {
                                contentLength = len;
                            }
                            break;
                        }
                    }

                    int bodyStart = headerEndIndex + 4;
                    int bodyAlreadyRead = (int)responseBuffer.Length - bodyStart;

                    byte[] finalResponse;
                    if (contentLength >= 0)
                    {
                        while (bodyAlreadyRead < contentLength)
                        {
                            bytesRead = await sslStream.ReadAsync(readBuffer);
                            if (bytesRead <= 0) break;
                            responseBuffer.Write(readBuffer, 0, bytesRead);
                            bodyAlreadyRead += bytesRead;
                        }

                        finalResponse = responseBuffer.ToArray();
                    }
                    else
                    {
                        while ((bytesRead = await sslStream.ReadAsync(readBuffer)) > 0)
                        {
                            responseBuffer.Write(readBuffer, 0, bytesRead);
                        }
                        finalResponse = responseBuffer.ToArray();
                    }

                    var responseMessage = new HttpResponseMessage(HttpStatusCode.OK);
                    var headerBytesOnly = Encoding.UTF8.GetBytes(headerPart + "\r\n\r\n");
                    var bodyBytesOnly = finalResponse.Skip(headerBytesOnly.Length).ToArray();
                    responseMessage.Content = new ByteArrayContent(bodyBytesOnly);

                    foreach (string line in headerPart.Split("\r\n"))
                    {
                        int colonIndex = line.IndexOf(':');
                        if (colonIndex > 0)
                        {
                            string name = line[..colonIndex].Trim();
                            string value = line[(colonIndex + 1)..].Trim();
                            if (!responseMessage.Headers.TryAddWithoutValidation(name, value))
                            {
                                responseMessage.Content.Headers.TryAddWithoutValidation(name, value);
                            }
                        }
                    }

                    return responseMessage;
                }
            }

            return null;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($" [ERROR] Failed to access {target} API: {ex.Message}"); 
            return null;
        }
    }
}
