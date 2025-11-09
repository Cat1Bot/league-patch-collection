using System.Text;

namespace LeaguePatchCollection;

public static class ProxyUtils
{
    public static int IndexOf(MemoryStream stream, byte[] pattern)
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

    public static async Task ReadChunkedBodyAsync(Stream serverStream, MemoryStream bodyStream, CancellationToken token)
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

    public static async Task<string> ReadLineAsync(Stream stream, CancellationToken token)
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

    public static async Task<byte[]> ReadHeadersAsync(Stream stream, CancellationToken token)
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
}