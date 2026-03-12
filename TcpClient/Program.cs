using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

var serverAddress = IPAddress.Loopback;
var serverPort = 8080;

await SendCommandAsync(serverAddress, serverPort, "  SET user:1 John");
await Task.Delay(1000);
await SendCommandAsync(serverAddress, serverPort, "  GET user:1");
await Task.Delay(1000);
await SendCommandAsync(serverAddress, serverPort, "  DELETE user:1");

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
        var responseArray = ArrayPool<byte>.Shared.Rent(1024);
        await clientSocket.ReceiveAsync(responseArray, SocketFlags.None);
        var responseText = Encoding.UTF8.GetString(responseArray, 0, responseArray.Length);
        Console.WriteLine("Server response");
        Console.WriteLine(responseText);
        ArrayPool<byte>.Shared.Return(responseArray);
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



