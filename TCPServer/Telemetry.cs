using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace TCPServer;

public static class Telemetry
{
    public const string ServiceName = "OtusProfessional.TcpServer";

    public static readonly string ServiceVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

    public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);

    public static readonly Meter Meter = new(ServiceName, ServiceVersion);

    public static readonly Counter<long> CommandsProcessed = Meter.CreateCounter<long>(
        name: "tcp.commands.processed",
        unit: "{command}",
        description: "Number of TCP commands processed by the server");

    public static readonly Histogram<double> CommandDuration = Meter.CreateHistogram<double>(
        name: "tcp.command.duration",
        unit: "ms",
        description: "Duration of TCP command processing in milliseconds");
}
