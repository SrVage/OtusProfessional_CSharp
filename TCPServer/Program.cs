using TCPServer;

var server = new TcpServer();

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