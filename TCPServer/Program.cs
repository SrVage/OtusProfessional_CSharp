using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OtusProfessional_CSharp;
using TCPServer;

var resourceBuilder = ResourceBuilder
    .CreateDefault()
    .AddService(serviceName: Telemetry.ServiceName, serviceVersion: Telemetry.ServiceVersion);

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddSource(Telemetry.ActivitySource.Name)
    .AddConsoleExporter()
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .SetResourceBuilder(resourceBuilder)
    .AddMeter(Telemetry.Meter.Name)
    .AddConsoleExporter()
    .Build();

var simpleStore = new SimpleStore();

using var server = new TcpServer(simpleStore);

Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    Console.WriteLine("Stopping...");
};

var serverTask = Task.Run(async () =>
{
    try
    {
        await server.StartAsync();
    }
    catch (Exception e)
    {
        Console.WriteLine("Server error: " + e);
    }
});

Console.WriteLine("Press CTRL+C to stop the server...");

try
{
    await serverTask;
}
catch (Exception e)
{
    Console.WriteLine("Server error: " + e);
}

Console.WriteLine("Server stopped.");
Console.ReadLine();