using System.Net;
using System.Net.Sockets;
using System.Text;

var serverAddress = IPAddress.Loopback;
var serverPort = 8080;

await SendCommandAsync(serverAddress, serverPort, "  SET user:1 John");
await SendCommandAsync(serverAddress, serverPort, "  GET user:1");

Console.WriteLine("Client stopped");

static async Task SendCommandAsync(IPAddress serverAddress, int serverPort, string command)
{
    using var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    
    try
    {
        await clientSocket.ConnectAsync(new IPEndPoint(serverAddress, serverPort));

        byte[] commandBytes = Encoding.UTF8.GetBytes(command + "\n");
        await clientSocket.SendAsync(commandBytes, SocketFlags.None);
        Console.WriteLine($"Send: {command}");
        clientSocket.Shutdown(SocketShutdown.Both);
    }
    catch (SocketException ex)
    {
        Console.WriteLine($"Socket error {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex.Message}");
    }
}



