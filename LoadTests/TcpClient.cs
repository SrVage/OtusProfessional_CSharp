using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Common;
using System.Text.Json;

namespace LoadTests;

public class TcpClient : IDisposable
{
    private IPAddress _serverAddress;
    private int _serverPort;
    private Socket _clientSocket;

    public TcpClient(string ip, int port)
    {
        if (IPAddress.TryParse(ip, out var ipAddress))
        {
            _serverAddress = ipAddress;
            _serverPort = port;
        }
    }

    public TcpClient()
    {
        _serverAddress = IPAddress.Loopback;
        _serverPort = 8080;
    }
    
    public async Task ConnectAsync()
    {
        _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await _clientSocket.ConnectAsync(new IPEndPoint(_serverAddress, _serverPort));
    }

    public async Task SetAsync(string key, UserProfile profile)
    {
        if (_clientSocket == null || !_clientSocket.Connected)
            throw new InvalidOperationException("Client is not connected");

        var profileValue = JsonSerializer.Serialize(profile);
        string command = $"SET {key} {profileValue}\r\n";
        byte[] commandBytes = Encoding.UTF8.GetBytes(command);
        byte[] responseBuffer = ArrayPool<byte>.Shared.Rent(1024);

        try
        {
            await _clientSocket.SendAsync(new ReadOnlyMemory<byte>(commandBytes), SocketFlags.None);

            int bytesRead = await _clientSocket.ReceiveAsync(new Memory<byte>(responseBuffer), SocketFlags.None);
            string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

            if (!response.StartsWith("OK"))
            {
                throw new Exception($"SET command failed: {response.Trim()}");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(responseBuffer);
        }
    }

    public async Task<UserProfile?> GetAsync(string key)
    {
        if (_clientSocket == null || !_clientSocket.Connected)
            throw new InvalidOperationException("Client is not connected");

        string command = $"GET {key}\r\n";
        byte[] commandBytes = Encoding.UTF8.GetBytes(command);
        byte[] responseBuffer = ArrayPool<byte>.Shared.Rent(1024);
        
        try
        {
            await _clientSocket.SendAsync(new ReadOnlyMemory<byte>(commandBytes), SocketFlags.None);
            
            int bytesRead = await _clientSocket.ReceiveAsync(new Memory<byte>(responseBuffer), SocketFlags.None);

            if (bytesRead == 0)
                return null;
            
            string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
            if (response.StartsWith("Key not found"))
            {
                return null;
            }

            byte[] result = new byte[bytesRead];
            Array.Copy(responseBuffer, result, bytesRead);
            var profile = JsonSerializer.Deserialize<UserProfile>(result);
            return profile;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(responseBuffer);
        }
    }

    public void Dispose()
    {
        _clientSocket.Close();
        _clientSocket.Dispose();
    }
}