using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using MCPForAlibre.Server;
using Xunit;

namespace MCPForAlibre.Tests;

// The bridge client against a fake add-on on a pipe of its own, so no Alibre is needed.
public sealed class AlibreBridgeTests
{
    static string NewPipeName() => "MCPForAlibreTest-" + Guid.NewGuid().ToString("N");

    // Answers like the add-on: hello with the given protocol, "echo" returns its params, anything else is an error.
    static async Task FakeAddOn(string pipeName, int protocol, int connections = 1)
    {
        for (int c = 0; c < connections; ++c)
        {
            await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                                                             PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync();
            var reader = new StreamReader(pipe, new UTF8Encoding(false));
            var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var request = JsonNode.Parse(line)!.AsObject();
                string method = request["method"]!.GetValue<string>();
                if (method == "drop")
                    break; // the add-on going away mid-call: close this connection without answering
                var reply = new JsonObject { ["id"] = request["id"]!.GetValue<long>() };
                switch (method)
                {
                    case "hello": reply["result"] = new JsonObject { ["protocol"] = protocol, ["addOn"] = "test" }; break;
                    case "echo": reply["result"] = request["params"]!.DeepClone(); break;
                    default: reply["error"] = "Unknown method '" + method + "'."; break;
                }
                await writer.WriteLineAsync(reply.ToJsonString());
            }
        }
    }

    [Fact]
    public async Task With_no_add_on_the_bridge_starts_Alibre_once_then_connects()
    {
        string name = NewPipeName();
        int starts = 0;
        Task? addOn = null;
        var statuses = new List<string?>();
        await using var bridge = new AlibreBridge(name, TimeSpan.FromMilliseconds(200), (status, _) =>
        {
            ++starts;
            status("starting Alibre");
            addOn = FakeAddOn(name, AlibreBridge.ProtocolVersion); // "Alibre" comes up with its add-on
            status(null);
            return Task.CompletedTask;
        });

        var result = await bridge.CallAsync("echo", new { a = 1 });

        Assert.Equal(1, result!["a"]!.GetValue<int>());
        Assert.Equal(1, starts);
        Assert.Null(bridge.Status);
    }

    [Fact]
    public async Task A_failed_start_gives_its_message()
    {
        await using var bridge = new AlibreBridge(NewPipeName(), TimeSpan.FromMilliseconds(200),
            (_, _) => throw new AlibreException("Alibre Design isn't installed where Windows records it."));

        var ex = await Assert.ThrowsAsync<AlibreException>(() => bridge.CallAsync("status"));
        Assert.Contains("isn't installed", ex.Message);
    }

    [Fact]
    public async Task A_running_add_on_never_starts_Alibre()
    {
        string name = NewPipeName();
        var server = FakeAddOn(name, AlibreBridge.ProtocolVersion);
        int starts = 0;
        await using var bridge = new AlibreBridge(name, startAlibre: (_, _) => { ++starts; return Task.CompletedTask; });

        Assert.NotNull(await bridge.CallAsync("echo", new { a = 1 }));
        Assert.Equal(0, starts);
    }

    [Fact]
    public void The_launcher_sees_pipes_without_connecting()
    {
        string name = NewPipeName();
        Assert.False(AlibreLauncher.PipeExists(name));
        using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Assert.True(AlibreLauncher.PipeExists(name));
    }

    [Fact]
    public void The_launcher_finds_Alibre_or_the_override()
    {
        // Wherever Alibre is installed, an override that doesn't exist finds nothing.
        Environment.SetEnvironmentVariable("MCPFORALIBRE_ALIBRE_EXE", @"C:\nowhere\Alibre Design.exe");
        try { Assert.Null(AlibreLauncher.FindAlibre()); }
        finally { Environment.SetEnvironmentVariable("MCPFORALIBRE_ALIBRE_EXE", null); }
        string? found = AlibreLauncher.FindAlibre();
        if (found is not null) // on a machine with Alibre
            Assert.EndsWith(@"Program\Alibre Design.exe", found);
    }

    [Fact]
    public async Task Call_returns_the_result()
    {
        string name = NewPipeName();
        var server = FakeAddOn(name, AlibreBridge.ProtocolVersion);
        await using var bridge = new AlibreBridge(name);

        var result = await bridge.CallAsync("echo", new { path = "C:\\a.AD_PRT", depth = 2.5, session = (string?)null });

        Assert.Equal("C:\\a.AD_PRT", result!["path"]!.GetValue<string>());
        Assert.Equal(2.5, result["depth"]!.GetValue<double>());
    }

    [Fact]
    public async Task An_add_on_error_becomes_an_AlibreException()
    {
        string name = NewPipeName();
        var server = FakeAddOn(name, AlibreBridge.ProtocolVersion);
        await using var bridge = new AlibreBridge(name);

        var ex = await Assert.ThrowsAsync<AlibreException>(() => bridge.CallAsync("nonsense"));
        Assert.Contains("nonsense", ex.Message);
        // The connection is still usable afterwards.
        Assert.NotNull(await bridge.CallAsync("echo", new { a = 1 }));
    }

    [Fact]
    public async Task No_add_on_says_Alibre_is_not_running()
    {
        await using var bridge = new AlibreBridge(NewPipeName(), TimeSpan.FromMilliseconds(200));

        var ex = await Assert.ThrowsAsync<AlibreException>(() => bridge.CallAsync("status"));
        Assert.Contains("isn't running", ex.Message);
    }

    [Fact]
    public async Task A_different_protocol_is_refused()
    {
        string name = NewPipeName();
        var server = FakeAddOn(name, AlibreBridge.ProtocolVersion + 1);
        await using var bridge = new AlibreBridge(name);

        var ex = await Assert.ThrowsAsync<AlibreException>(() => bridge.CallAsync("status"));
        Assert.Contains("protocol", ex.Message);
    }

    [Fact]
    public async Task Reconnects_after_the_add_on_restarts()
    {
        string name = NewPipeName();
        var server = FakeAddOn(name, AlibreBridge.ProtocolVersion, connections: 2);
        await using var bridge = new AlibreBridge(name);

        Assert.NotNull(await bridge.CallAsync("echo", new { a = 1 }));
        await Assert.ThrowsAsync<IOException>(() => bridge.CallAsync("drop"));
        // The next call connects again (to the fake's second connection).
        Assert.Equal(2, (await bridge.CallAsync("echo", new { a = 2 }))!["a"]!.GetValue<int>());
    }
}
