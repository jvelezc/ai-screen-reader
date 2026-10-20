using System.Net;
using System.Text.Json;
using HeadlessScreenReader.Domain;
using HeadlessScreenReader.Egress;

using var shutdown = new CancellationTokenSource(TimeSpan.FromMinutes(30));
Console.CancelKeyPress += (_, args) => { args.Cancel = true; shutdown.Cancel(); };
try
{
    if (args.Length is 2 or 3 && args[0] == "proxy")
    {
        await new ConnectProxy(new PublicTargetPolicy(args[1].Split(',')), args.Length == 3 ? args[2] : null).RunAsync(shutdown.Token);
        return 0;
    }
    if (args.Length == 2 && args[0] == "sentinel")
    {
        var address = IPAddress.Parse(args[1]);
        var tcp = await IsolationProbe.SentinelAsync(address, false, shutdown.Token);
        var udp = await IsolationProbe.SentinelAsync(address, true, shutdown.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { ControlTcpSentinel = tcp, ControlUdpSentinel = udp }));
        return tcp && udp ? 0 : 1;
    }
    if (args.Length == 3 && args[0] == "probe")
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await IsolationProbe.VerifyAsync(IPAddress.Parse(args[1]), new(args[2].Split(',')), deadline.Token);
        Console.WriteLine(JsonSerializer.Serialize(result));
        return result.Passed ? 0 : 1;
    }
    Console.Error.WriteLine("Usage: proxy <approved-hosts> | sentinel <proxy-ip> | probe <proxy-ip> <approved-hosts>");
    return 2;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { return 0; }
catch (OperationCanceledException)
{
    Console.Error.WriteLine("EGRESS_TIMEOUT");
    return 2;
}
catch (Exception exception) when (exception is ArgumentException or IOException or System.Net.Sockets.SocketException)
{
    Console.Error.WriteLine("EGRESS_OPERATION_FAILED");
    return 2;
}
