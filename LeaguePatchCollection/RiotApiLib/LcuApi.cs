using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;
using System.Net.Security;
using System.Net.Sockets;

namespace RiotApiLib;

public static class LcuApi
{
    private static async Task<string?> GetLockfileContent()
    {
        if (!ProcessChecker.IsProcessRunning("LeagueClient", "League of Legends"))
        {
            Trace.WriteLine("[ERROR] League Client is not running.");
            return null;
        }

        string? gameDir = GetProductInstallPath();
        if (string.IsNullOrEmpty(gameDir))
        {
            Trace.WriteLine("[ERROR] Could not determine game directory.");
            return null;
        }

        string lockfilePath = Path.Combine(gameDir, "lockfile");

        if (!File.Exists(lockfilePath))
        {
            Trace.WriteLine("[ERROR] Lockfile does not exist.");
            return null;
        }

        try
        {
            return await LockfileReader.ReadLockfileAsync(lockfilePath);
        }
        catch (IOException ex)
        {
            Trace.WriteLine($"[ERROR] Failed to access lockfile: {ex.Message}");
            return null;
        }
    }

    public static async Task<HttpResponseMessage?> SendRequest(string endpoint, HttpMethod method, HttpContent? content = null)
    {
        string? lockfileContent = await GetLockfileContent();
        if (lockfileContent == null)
        {
            return null;
        }

        var lockfileParts = lockfileContent.Split(':');
        if (lockfileParts.Length != 5)
        {
            Trace.WriteLine("[ERROR] Lockfile format is incorrect.");
            return null;
        }

        string port = lockfileParts[2];
        string password = lockfileParts[3];
        string authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{password}"));

        string host = "127.0.0.1";
        string requestLine = $"{method.Method} {endpoint} HTTP/1.1\r\n";

        StringBuilder headerBuilder = new();
        headerBuilder.Append(requestLine);
        headerBuilder.Append($"Host: 127.0.0.1:{port}\r\n");
        headerBuilder.Append($"Authorization: Basic {authValue}\r\n");
        headerBuilder.Append("Accept: application/json\r\n");

        byte[] bodyBytes = Array.Empty<byte>();
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

        string headerString = headerBuilder.ToString();

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(host, int.Parse(port));

            using var networkStream = tcpClient.GetStream();
            using var sslStream = new SslStream(networkStream, false,
                (sender, certificate, chain, sslPolicyErrors) => true);

            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12
            });

            byte[] headerBytes = Encoding.UTF8.GetBytes(headerString);
            await sslStream.WriteAsync(headerBytes.AsMemory(0, headerBytes.Length));
            if (bodyBytes.Length > 0)
            {
                await sslStream.WriteAsync(bodyBytes.AsMemory(0, bodyBytes.Length));
            }
            await sslStream.FlushAsync();

            using MemoryStream responseStream = new();
            byte[] buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = await sslStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                responseStream.Write(buffer, 0, bytesRead);
                if (responseStream.Length >= 4)
                {
                    byte[] respBuffer = responseStream.GetBuffer();
                    for (int i = 0; i <= responseStream.Length - 4; i++)
                    {
                        if (respBuffer[i] == '\r' && respBuffer[i + 1] == '\n' &&
                            respBuffer[i + 2] == '\r' && respBuffer[i + 3] == '\n')
                        {
                            goto HeadersComplete;
                        }
                    }
                }
            }
        HeadersComplete:

            byte[] fullResponse = responseStream.ToArray();
            string responseText = Encoding.UTF8.GetString(fullResponse);

            int headerEnd = responseText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd == -1)
            {
                Trace.WriteLine("[ERROR] Incomplete HTTP response received.");
                return null;
            }

            string headerPart = responseText.Substring(0, headerEnd);
            string[] headerLines = headerPart.Split(new[] { "\r\n" }, StringSplitOptions.None);

            if (headerLines.Length == 0)
            {
                Trace.WriteLine("[ERROR] No status line in response.");
                return null;
            }
            string statusLine = headerLines[0];
            string[] statusParts = statusLine.Split(' ');
            if (statusParts.Length < 3 ||
                !int.TryParse(statusParts[1], out int statusCode))
            {
                Trace.WriteLine("[ERROR] Unable to parse status code.");
                return null;
            }

            int headerByteLength = Encoding.UTF8.GetByteCount(headerPart + "\r\n\r\n");
            int bodyLength = fullResponse.Length - headerByteLength;
            byte[] responseBody = new byte[bodyLength];
            Array.Copy(fullResponse, headerByteLength, responseBody, 0, bodyLength);

            var response = new HttpResponseMessage((HttpStatusCode)statusCode)
            {
                Content = new ByteArrayContent(responseBody)
            };

            for (int i = 1; i < headerLines.Length; i++)
            {
                int colonIndex = headerLines[i].IndexOf(':');
                if (colonIndex > 0)
                {
                    string key = headerLines[i].Substring(0, colonIndex).Trim();
                    string value = headerLines[i].Substring(colonIndex + 1).Trim();
                    if (!response.Headers.TryAddWithoutValidation(key, value))
                    {
                        response.Content?.Headers.TryAddWithoutValidation(key, value);
                    }
                }
            }

            return response;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ERROR] LCU request failed: {ex.Message}");
            return null;
        }
    }

    private static string? GetProductInstallPath()
    {
        string yamlFilePath = GetYamlFilePath();
        if (!File.Exists(yamlFilePath))
        {
            Trace.WriteLine("[WARN] YAML file not found. Falling back to default paths.");
            return GetHardcodedGamePath();
        }

        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            using var reader = new StreamReader(yamlFilePath);
            var yamlContent = deserializer.Deserialize<dynamic>(reader);
            return OperatingSystem.IsMacOS()
                ? Path.Combine(yamlContent["product_install_full_path"], "Contents", "LoL")
                : yamlContent["product_install_full_path"];
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ERROR] Failed to read YAML: {ex.Message}");
            return GetHardcodedGamePath();
        }
    }

    private static string GetYamlFilePath() =>
        OperatingSystem.IsMacOS()
            ? "/Users/Shared/Riot Games/Metadata/league_of_legends.live/league_of_legends.live.product_settings.yaml"
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games", "Metadata", "league_of_legends.live", "league_of_legends.live.product_settings.yaml");

    private static string? GetHardcodedGamePath() =>
        OperatingSystem.IsMacOS()
            ? "/Applications/League of Legends.app/Contents/LoL/"
            : "C:\\Riot Games\\League of Legends";
}
