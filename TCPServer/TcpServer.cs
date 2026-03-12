using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using OtusProfessional_CSharp;

namespace TCPServer;

public class TcpServer : IDisposable
{
    private const int BACKLOG = 10;
    const int BUFFER_SIZE = 1024;
    private readonly IPAddress _ipAddress;
    private readonly int _port;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Socket _socket;
    private readonly Lock _lock = new();
    private readonly SimpleStore _simpleStore;
    private byte[] _keyNotFoundResponse;
    private byte[] _okResponse;
    private byte[] _unknownCommandResponse;

    private bool IsRunning
    {
        get
        {
            lock (_lock) return field;
        }
        set
        {
            lock (_lock) field = value;
        }
    }
    
    public TcpServer(string ip, int port, SimpleStore simpleStore)
    {
        InitializeStandardResponses();
        _simpleStore = simpleStore;
        if (IPAddress.TryParse(ip, out IPAddress? ipAddress))
        {
            _ipAddress = ipAddress;
        }
        else
        {
            throw new ArgumentException($"Invalid IP address: {ip}");
        }
        if (port is > 0 and < 65536)
        {
            _port = port;
        }
        else
        {
            throw new ArgumentException($"Invalid port number: {port}");
        }
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public TcpServer(SimpleStore simpleStore)
    {
        InitializeStandardResponses();
        _simpleStore = simpleStore;
        if (IPAddress.TryParse("127.0.0.1", out IPAddress? ipAddress))
        {
            _ipAddress = ipAddress;
            _port = 8080;
            _cancellationTokenSource = new CancellationTokenSource();
        }
    }

    private void InitializeStandardResponses()
    {
        _keyNotFoundResponse = Encoding.UTF8.GetBytes("Key not found \r\n");
        _okResponse = Encoding.UTF8.GetBytes("OK\r\n");
        _unknownCommandResponse = Encoding.UTF8.GetBytes("-ERR Unknown command\r\n");
    }

    public async Task StartAsync()
    {
        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            IPEndPoint endPoint = new IPEndPoint(_ipAddress, _port);
            _socket.Bind(endPoint);
            _socket.Listen(BACKLOG);

            IsRunning = true;

            Console.WriteLine($"Listening on {_ipAddress}:{_port}");
            
            await AcceptConnectionsAsync(_cancellationTokenSource.Token);
        }
        catch(SocketException e)
        {
            Console.WriteLine("SocketException: {0}", e);
            throw;
        }
        catch(Exception e)
        {
            Console.WriteLine("Exception: {0}", e);
            throw;
        }
    }

    public void Stop()
    {
        if(!IsRunning)
            return;
        
        IsRunning = false;
        _cancellationTokenSource.Cancel();
        try
        {
            _socket?.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine("Error stop server: {0}", e);
        }
    }

    private async Task AcceptConnectionsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && IsRunning)
        {
            try
            {
                Socket clientSocket = await _socket.AcceptAsync(token);
                _ = ProcessClientAsync(clientSocket, token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Socket stopped");
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: {0}", e);
            }
        }
    }

    private async Task ProcessClientAsync(Socket clientSocket, CancellationToken token)
    {
        var clientEndpoint = clientSocket.RemoteEndPoint;
        Console.WriteLine($"Client connected from {clientEndpoint}");
        
        byte[]? rentedBuffer = null;

        try
        {
            rentedBuffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
            while (!token.IsCancellationRequested)
            {
                int bytesRead = await clientSocket.ReceiveAsync(
                    new Memory<byte>(rentedBuffer, 0, BUFFER_SIZE),
                    SocketFlags.None,
                    token);

                if (bytesRead == 0)
                {
                    Console.WriteLine($"Client disconnected from {clientEndpoint}");
                    break;
                }
                
                string receivedData = Encoding.UTF8.GetString(rentedBuffer, 0, bytesRead);
                Console.WriteLine("Received {0} bytes: {1}", bytesRead, receivedData.Trim());
                
                ReadOnlySpan<char> span = receivedData.AsSpan();
                var parseResult = CommandParser.Parse(span);

                switch (parseResult.Command)
                {
                    case "GET":
                        ProcessGetCommand(clientSocket, parseResult);
                        break;
                    case "SET":
                        ProcessSetCommand(clientSocket, parseResult);
                        break;
                    case "DELETE":
                        ProcessDeleteCommand(clientSocket, parseResult);
                        break;
                    default:
                        clientSocket.SendAsync(_unknownCommandResponse);
                        break;
                }
                
                Console.WriteLine("----Parse result----");
                Console.WriteLine("Command: {0}", parseResult.Command.ToString());
                Console.WriteLine("Key: {0}", parseResult.Key.ToString());
                Console.WriteLine("Value: {0}", parseResult.Value.ToString());
                Console.WriteLine("----End----");
            }
        }
        catch (SocketException e)
        {
            Console.WriteLine($"SocketException: {e}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Socket stopped");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Exception: {e}");
        }
        finally
        {
            if (rentedBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }

            try
            {
                if (clientSocket.Connected)
                {
                    clientSocket.Shutdown(SocketShutdown.Both);
                }
            }
            catch
            {
                
            }
            finally
            {
                clientSocket.Close();
                clientSocket.Dispose();
                Console.WriteLine($"Client disconnected from {clientEndpoint}");
            }
        }
    }

    private void ProcessDeleteCommand(Socket clientSocket, ParseCommand parseResult)
    {
        var key = parseResult.Key.ToString();
        _simpleStore.Delete(key);
        clientSocket.SendAsync(_okResponse);
    }

    private void ProcessSetCommand(Socket clientSocket, ParseCommand parseResult)
    {
        var key = parseResult.Key.ToString();
        var valueBytesCount = Encoding.UTF8.GetByteCount(parseResult.Value);
        var spanArray = ArrayPool<byte>.Shared.Rent(valueBytesCount);
        Encoding.UTF8.GetBytes(parseResult.Value, spanArray);
        _simpleStore.Set(key, spanArray);
        ArrayPool<byte>.Shared.Return(spanArray);
        clientSocket.SendAsync(_okResponse);
    }

    private void ProcessGetCommand(Socket clientSocket, ParseCommand parseResult)
    {
        var key = parseResult.Key.ToString();
        var result = _simpleStore.Get(key);
        clientSocket.SendAsync(result ?? _keyNotFoundResponse);
    }

    public void Dispose()
    {
        Stop();
        _socket?.Dispose();
        _cancellationTokenSource?.Dispose();
        GC.SuppressFinalize(this);
    }
}