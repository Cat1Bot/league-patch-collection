using System.Diagnostics;
using System.Net.Sockets;
using System.Net;

namespace LeaguePatchCollection;

public class LeagueProxy
{
    private static CancellationTokenSource? _ServerCTS;
    private static readonly XMPPProxy _ChatProxy;
    private static readonly RMSProxy _RmsProxy;
    private static readonly ConfigProxy _ConfigProxy;
    private static readonly HttpProxy _GeopassProxy;
    private static readonly HttpProxy _MailboxProxy;
    private static readonly HttpProxy _PlatformProxy;
    private static readonly HttpProxy _LcuNavProxy;

    public static int ChatPort { get; private set; }
    public static int RtmpPort { get; private set; }
    public static int RmsPort { get; private set; }
    public static int ConfigPort { get; private set; }
    public static int GeopassPort { get; private set; }
    public static int MailboxPort { get; private set; }
    public static int LcuNavPort { get; private set; }
    public static int PlatformPort { get; private set; }

    static LeagueProxy()
    {
        _ChatProxy = new XMPPProxy();
        _RmsProxy = new RMSProxy();

        _ConfigProxy = new ConfigProxy();
        _GeopassProxy = new HttpProxy();
        _MailboxProxy = new HttpProxy();
        _PlatformProxy = new HttpProxy();
        _LcuNavProxy = new HttpProxy();
    }

    public static async Task Start()
    {
        if (_ServerCTS is not null)
        {
            Trace.WriteLine("[INFO] Proxy is already running. Attempting to restart.");
            Stop();
        }

        await FindAvailablePortsAsync();

        _ServerCTS = new CancellationTokenSource();

        _ChatProxy?.RunAsync(_ServerCTS.Token);
        _RmsProxy?.RunAsync(_ServerCTS.Token);

        _ConfigProxy?.RunAsync(_ServerCTS.Token);
        _GeopassProxy?.RunAsync(nameof(ConfigProxy.GeopassUrl), GeopassPort, _ServerCTS.Token);
        _MailboxProxy?.RunAsync(nameof(ConfigProxy.MailboxUrl), MailboxPort, _ServerCTS.Token);
        _PlatformProxy?.RunAsync(nameof(ConfigProxy.PlatformUrl), PlatformPort, _ServerCTS.Token);
        _LcuNavProxy?.RunAsync(nameof(ConfigProxy.LcuNavUrl), LcuNavPort, _ServerCTS.Token);
    }

    private static async Task FindAvailablePortsAsync()
    {
        int[] ports = new int[10];
        for (int i = 0; i < ports.Length; i++)
        {
            ports[i] = GetFreePort();
            await Task.Delay(10);
        }

        ChatPort = ports[0];
        RmsPort = ports[1];
        RtmpPort = ports[2];
        ConfigPort = ports[3];
        GeopassPort = ports[4];
        MailboxPort = ports[5];
        LcuNavPort = ports[7];
        PlatformPort = ports[9];
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static void Stop()
    {
        if (_ServerCTS is null)
        {
            Trace.WriteLine("[WARN] Unable to stop proxy service: Service is not running.");
            return;
        }

        _ServerCTS?.Cancel();

        _ChatProxy.Stop();
        _RmsProxy.Stop();


        _ConfigProxy.Stop();
        _GeopassProxy.Stop();
        _MailboxProxy.Stop();
        _PlatformProxy.Stop();
        _LcuNavProxy.Stop();

        _ServerCTS?.Dispose();
        _ServerCTS = null;

        Trace.WriteLine("[INFO] Proxy services successfully stopped.");
    }

    public static Process? LaunchRCS(IEnumerable<string>? args = null)
    {
        if (_ServerCTS is null)
        {
            Trace.WriteLine("[ERROR] RCS launch failed: Proxies were not started due to an error.");
        }
        return RiotClient.Launch(args);
    }
}