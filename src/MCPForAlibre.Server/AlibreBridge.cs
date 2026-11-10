using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MCPForAlibre.Server;

// The client end of the bridge protocol (docs\PROTOCOL.md): one JSON object per line each way.
// Connects on first use and again after Alibre restarts; calls are sent one at a time.
public sealed class AlibreBridge : IAsyncDisposable
{
    public const string DefaultPipeName = "MCPForAlibre";
    public const int ProtocolVersion = 1;

    readonly string m_pipeName;
    readonly TimeSpan m_connectTimeout;
    readonly Func<Action<string?>, CancellationToken, Task>? m_startAlibre;
    readonly SemaphoreSlim m_lock = new(1, 1);
    NamedPipeClientStream? m_pipe;
    StreamReader? m_reader;
    StreamWriter? m_writer;
    long m_nextId;

    // startAlibre is called when the pipe can't be reached, to start Alibre and wait for it
    // (AlibreLauncher.EnsureRunningAsync in the real server); null leaves it to the user.
    public AlibreBridge(string pipeName, TimeSpan? connectTimeout = null,
                        Func<Action<string?>, CancellationToken, Task>? startAlibre = null)
    {
        m_pipeName = pipeName;
        m_connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(3);
        m_startAlibre = startAlibre;
    }

    // What the bridge is doing while it isn't simply connected, for the tray window.
    public string? Status { get; private set; }

    // Sends one request and returns its result, or throws AlibreException with the add-on's message.
    public async Task<JsonNode?> CallAsync(string method, object? parameters = null, CancellationToken cancel = default)
    {
        await m_lock.WaitAsync(cancel);
        try
        {
            // One retry, only when the request couldn't be sent: a pipe left from an Alibre that has
            // since closed fails on write. A failure after sending isn't retried, as the call may have run.
            for (int attempt = 0; ; ++attempt)
            {
                try
                {
                    await EnsureConnectedAsync(cancel);
                    return await SendAsync(method, parameters, cancel);
                }
                catch (NotSentException) when (attempt == 0)
                {
                    Disconnect();
                }
                catch (NotSentException ex)
                {
                    Disconnect();
                    throw new IOException(ex.Message, ex.InnerException);
                }
                catch (IOException)
                {
                    Disconnect();
                    throw;
                }
            }
        }
        finally
        {
            m_lock.Release();
        }
    }

    async Task EnsureConnectedAsync(CancellationToken cancel)
    {
        if (m_pipe is { IsConnected: true })
            return;
        Disconnect();
        var pipe = await ConnectAsync(cancel);
        if (pipe is null && m_startAlibre is not null)
        {
            // Alibre isn't answering: start it (or wait for one that's loading), then try again.
            await m_startAlibre(s => Status = s, cancel);
            pipe = await ConnectAsync(cancel);
        }
        if (pipe is null)
        {
            throw new AlibreException("Alibre Design isn't running, or the MCPForAlibre add-on isn't loaded " +
                                      "(install it with deploy\\install.ps1, then restart Alibre; its MCPForAlibre > Status menu shows the pipe).");
        }
        m_pipe = pipe;
        var utf8 = new UTF8Encoding(false);
        m_reader = new StreamReader(pipe, utf8);
        m_writer = new StreamWriter(pipe, utf8) { AutoFlush = true, NewLine = "\n" };

        var hello = await SendAsync("hello", null, cancel);
        int protocol = hello?["protocol"]?.GetValue<int>() ?? 0;
        if (protocol != ProtocolVersion)
        {
            Disconnect();
            throw new AlibreException($"The add-on in Alibre speaks bridge protocol {protocol}, but this server needs {ProtocolVersion}. " +
                                      "Install the same version of both.");
        }
        AddOnVersion = hello?["addOn"]?.GetValue<string>();
    }

    // The pipe, connected, or null if nothing answered within the connect timeout.
    async Task<NamedPipeClientStream?> ConnectAsync(CancellationToken cancel)
    {
        var pipe = new NamedPipeClientStream(".", m_pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(m_connectTimeout);
            await pipe.ConnectAsync(timeout.Token);
            return pipe;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            await pipe.DisposeAsync();
            return null;
        }
    }

    // For the tray window: whether the pipe to Alibre is open now, and the add-on's version from its hello.
    public bool IsConnected => m_pipe is { IsConnected: true };
    public string? AddOnVersion { get; private set; }

    async Task<JsonNode?> SendAsync(string method, object? parameters, CancellationToken cancel)
    {
        long id = Interlocked.Increment(ref m_nextId);
        var request = new JsonObject
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters is null ? new JsonObject() : JsonSerializer.SerializeToNode(parameters),
        };
        try
        {
            await m_writer!.WriteLineAsync(request.ToJsonString().AsMemory(), cancel);
        }
        catch (IOException ex)
        {
            throw new NotSentException(ex);
        }
        string? line = await m_reader!.ReadLineAsync(cancel);
        if (line is null)
            throw new IOException("Alibre closed the connection.");
        var reply = JsonNode.Parse(line)?.AsObject() ?? throw new IOException("Alibre sent an empty reply.");
        if (reply["id"]?.GetValue<long>() != id)
            throw new IOException("Alibre's reply was for a different request.");
        if (reply["error"] is JsonNode error)
            throw new AlibreException(error.GetValue<string>());
        return reply["result"];
    }

    void Disconnect()
    {
        m_reader?.Dispose();
        m_writer = null;
        m_reader = null;
        m_pipe?.Dispose();
        m_pipe = null;
    }

    public ValueTask DisposeAsync()
    {
        Disconnect();
        m_lock.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class AlibreException(string message) : Exception(message);

sealed class NotSentException(IOException inner) : Exception("Couldn't send the request to Alibre: " + inner.Message, inner);
