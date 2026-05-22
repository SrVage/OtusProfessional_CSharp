using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using OtusProfessional_CSharp;
using System.Text.Json;
using Common;

namespace TCPServer;

public class TcpServer : IDisposable
{
    private const int BACKLOG = 10;
    private const int BUFFER_SIZE = 1024;
    private const int MAX_USERS_COUNT = 4;
    private readonly IPAddress _ipAddress;
    private readonly int _port;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Socket _socket;
    private readonly Lock _lock = new();
    private readonly SimpleStore _simpleStore;
    private byte[] _keyNotFoundResponse;
    private byte[] _okResponse;
    private byte[] _unknownCommandResponse;
    private byte[] _invalidJsonResponse;
    private byte[] _corruptedDataResponse;
    private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(MAX_USERS_COUNT, MAX_USERS_COUNT);

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
        _invalidJsonResponse = Encoding.UTF8.GetBytes("-ERR Invalid JSON\r\n");
        _corruptedDataResponse = Encoding.UTF8.GetBytes("-ERR Corrupted data\r\n");
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
                await _semaphoreSlim.WaitAsync(token);
                Socket clientSocket = await _socket.AcceptAsync(token);
                _ = ProcessClientAsync(clientSocket, token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Socket stopped");
                _semaphoreSlim.Release();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: {0}", e);
                _semaphoreSlim.Release();
            }
        }
    }

    private async Task ProcessClientAsync(Socket clientSocket, CancellationToken token)
    {
        var clientEndpoint = clientSocket.RemoteEndPoint;
        Console.WriteLine($"Client connected from {clientEndpoint}");
        
        byte[]? rentedBuffer = null;
        int bufferedBytes = 0;

        try
        {
            rentedBuffer = ArrayPool<byte>.Shared.Rent(BUFFER_SIZE);
            while (!token.IsCancellationRequested)
            {
                int bytesRead = await clientSocket.ReceiveAsync(
                    rentedBuffer.AsMemory(bufferedBytes, rentedBuffer.Length - bufferedBytes),
                    SocketFlags.None,
                    token);

                if (bytesRead == 0)
                {
                    Console.WriteLine($"Client disconnected from {clientEndpoint}");
                    break;
                }

                bufferedBytes += bytesRead;

                int processedFrom = 0;
                while (true)
                {
                    var unparsed = new ReadOnlySpan<byte>(rentedBuffer, processedFrom, bufferedBytes - processedFrom);
                    int crlfIdx = unparsed.IndexOf("\r\n"u8);
                    if (crlfIdx < 0) break;

                    await HandleCommandAsync(clientSocket, rentedBuffer, processedFrom, crlfIdx, clientEndpoint, token);
                    processedFrom += crlfIdx + 2;
                }

                int leftover = bufferedBytes - processedFrom;
                if (processedFrom > 0 && leftover > 0)
                {
                    Buffer.BlockCopy(rentedBuffer, processedFrom, rentedBuffer, 0, leftover);
                }
                bufferedBytes = leftover;

                if (bufferedBytes == rentedBuffer.Length)
                {
                    Console.WriteLine($"Command exceeds buffer size from {clientEndpoint}");
                    break;
                }
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
                _semaphoreSlim.Release();
                Console.WriteLine($"Client disconnected from {clientEndpoint}");
            }
        }
    }

    private async Task HandleCommandAsync(Socket clientSocket, byte[] buffer, int offset, int length, EndPoint? clientEndpoint, CancellationToken token)
    {
        string receivedData = Encoding.UTF8.GetString(buffer, offset, length);
        Console.WriteLine("Received {0} bytes: {1}", length, receivedData);

        ReadOnlySpan<char> span = receivedData.AsSpan();
        var parseResult = CommandParser.Parse(span);

        var commandName = parseResult.Command.ToString();
        var key = parseResult.Key.ToString();
        var rawValue = parseResult.Value.ToString();

        using (var activity = Telemetry.ActivitySource.StartActivity(
                   $"tcp.command {commandName}",
                   ActivityKind.Server))
        {
            activity?.SetTag("command.name", commandName);
            activity?.SetTag("command.key", key);
            activity?.SetTag("command.payload_bytes", length);
            activity?.SetTag("net.peer.address", clientEndpoint?.ToString());

            var stopwatch = Stopwatch.StartNew();
            var status = "ok";

            try
            {
                switch (parseResult.Command)
                {
                    case "GET":
                        await ProcessGetCommandAsync(clientSocket, key, activity, token);
                        break;
                    case "SET":
                        await ProcessSetCommandAsync(clientSocket, key, rawValue, activity, token);
                        break;
                    case "DELETE":
                        await ProcessDeleteCommandAsync(clientSocket, key, token);
                        break;
                    default:
                        await clientSocket.SendAsync(_unknownCommandResponse, SocketFlags.None, token);
                        status = "unknown";
                        activity?.SetStatus(ActivityStatusCode.Error, "Unknown command");
                        break;
                }
            }
            catch (Exception ex)
            {
                status = "error";
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

                if (status == "ok" && activity?.Status == ActivityStatusCode.Error)
                    status = "error";

                activity?.SetTag("command.status", status);
                activity?.SetTag("command.duration_ms", elapsedMs);

                var tags = new TagList
                {
                    { "command.name", commandName },
                    { "command.status", status }
                };

                Telemetry.CommandsProcessed.Add(1, tags);
                Telemetry.CommandDuration.Record(elapsedMs, tags);
            }
        }

        Console.WriteLine("----Parse result----");
        Console.WriteLine("Command: {0}", commandName);
        Console.WriteLine("Key: {0}", key);
        Console.WriteLine("Value: {0}", rawValue);
        Console.WriteLine("----End----");
    }

    private async Task ProcessDeleteCommandAsync(Socket clientSocket, string key, CancellationToken token)
    {
        _simpleStore.Delete(key);
        await clientSocket.SendAsync(_okResponse, SocketFlags.None, token);
    }

    private async Task ProcessSetCommandAsync(Socket clientSocket, string key, string value, Activity? activity, CancellationToken token)
    {
        UserProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<UserProfile>(value);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Invalid JSON for key '{key}': {ex.Message}");
            await clientSocket.SendAsync(_invalidJsonResponse, SocketFlags.None, token);
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid JSON");
            return;
        }
        if (profile == null)
        {
            await clientSocket.SendAsync(_unknownCommandResponse, SocketFlags.None, token);
            activity?.SetStatus(ActivityStatusCode.Error, "Empty profile");
            return;
        }
        _simpleStore.Set(key, profile);
        await clientSocket.SendAsync(_okResponse, SocketFlags.None, token);
    }

    private async Task ProcessGetCommandAsync(Socket clientSocket, string key, Activity? activity, CancellationToken token)
    {
        UserProfile? result;
        try
        {
            result = _simpleStore.Get(key);
        }
        catch (System.IO.InvalidDataException ex)
        {
            Console.WriteLine($"Corrupted data for key '{key}': {ex.Message}");
            await clientSocket.SendAsync(_corruptedDataResponse, SocketFlags.None, token);
            activity?.SetStatus(ActivityStatusCode.Error, "Corrupted data");
            return;
        }
        if (result is null)
        {
            await clientSocket.SendAsync(_keyNotFoundResponse, SocketFlags.None, token);
            return;
        }
        var profileValue = JsonSerializer.SerializeToUtf8Bytes(result);
        await clientSocket.SendAsync(profileValue, SocketFlags.None, token);
    }

    public void Dispose()
    {
        Stop();
        _socket?.Dispose();
        _cancellationTokenSource?.Dispose();
        GC.SuppressFinalize(this);
    }
}