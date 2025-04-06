using Microsoft.VisualBasic.Logging;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LeaguePatchCollection;

public class LeagueProxy
{
    private static CancellationTokenSource? _ServerCTS;
    private static readonly XMPPProxy _ChatProxy;
    private static readonly RMSProxy _RmsProxy;
    private static readonly RTMPProxy _RtmpProxy;
    private static readonly ConfigProxy _ConfigProxy;
    private static readonly GeopassProxy _GeopassProxy;
    private static readonly MailboxProxy _MailboxProxy;
    private static readonly PbTokenProxy _PbTokenProxy;
    private static readonly PlatformProxy _PlatformProxy;
    private static readonly LedgeProxy _LedgeProxy;
    private static readonly LcuNavProxy _LcuNavProxy;

    public static int ChatPort { get; private set; }
    public static int RtmpPort { get; private set; }
    public static int RmsPort { get; private set; }
    public static int ConfigPort { get; private set; }
    public static int GeopassPort { get; private set; }
    public static int MailboxPort { get; private set; }
    public static int PbTokenPort { get; private set; }
    public static int LcuNavigationPort { get; private set; }
    public static int LedgePort { get; private set; }
    public static int PlatformPort { get; private set; }

    static LeagueProxy()
    {
        _ChatProxy = new XMPPProxy();
        _RmsProxy = new RMSProxy();
        _RtmpProxy = new RTMPProxy();

        _ConfigProxy = new ConfigProxy();
        _GeopassProxy = new GeopassProxy();
        _MailboxProxy = new MailboxProxy();
        _PbTokenProxy = new PbTokenProxy();
        _PlatformProxy = new PlatformProxy();
        _LedgeProxy = new LedgeProxy();
        _LcuNavProxy = new LcuNavProxy();
    }

    public static async Task Start()
    {
        if (_ServerCTS is not null)
        {
            Trace.WriteLine("[INFO] Proxy is already running. Attempting to restart.");
            Stop();
        }

        await FindAvailablePortsAsync();

        SystemYamlLive.LoadProductInstallPath();
        _ServerCTS = new CancellationTokenSource();

        _ChatProxy?.RunAsync(_ServerCTS.Token);
        _RmsProxy?.RunAsync(_ServerCTS.Token);
        //_RtmpProxy?.RunAsync(_ServerCTS.Token);

        _ConfigProxy?.RunAsync(_ServerCTS.Token);
        _GeopassProxy?.RunAsync(_ServerCTS.Token);
        _MailboxProxy?.RunAsync(_ServerCTS.Token);
        _PbTokenProxy?.RunAsync(_ServerCTS.Token);
        _PlatformProxy?.RunAsync(_ServerCTS.Token);
        _LedgeProxy?.RunAsync(_ServerCTS.Token);
        _LcuNavProxy?.RunAsync(_ServerCTS.Token);
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
        PbTokenPort = ports[6];
        LcuNavigationPort = ports[7];
        LedgePort = ports[8];
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

        //_RtmpProxy.Stop();

        _ConfigProxy.Stop();
        _GeopassProxy.Stop();
        _MailboxProxy.Stop();
        _PbTokenProxy.Stop();
        _PlatformProxy.Stop();
        _LedgeProxy.Stop();
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