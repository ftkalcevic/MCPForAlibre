using MCPForAlibre.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The MCP server Claude starts (stdio). It forwards each tool call over the named pipe
// \\.\pipe\MCPForAlibre to the add-on running inside Alibre Design, and shows a tray icon
// whose window has this server's calls and the add-on's log (MCPFORALIBRE_NO_TRAY=1 hides it).
ServerLog.Write("starting, version " + typeof(AlibreBridge).Assembly.GetName().Version);

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol, so every log goes to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<ServerStats>();
// Starts Alibre when a call finds it closed, unless MCPFORALIBRE_NO_LAUNCH=1.
builder.Services.AddSingleton<AlibreBridge>(_ => new AlibreBridge(AlibreBridge.DefaultPipeName,
    startAlibre: AlibreLauncher.Disabled ? null : (status, cancel) => AlibreLauncher.EnsureRunningAsync(AlibreBridge.DefaultPipeName, status, cancel)));
builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "MCPForAlibre", Version = "0.4.0" };
        o.ServerInstructions = ServerInstructions.Text; // conventions and Alibre's limits, sent on connect
    })
    .WithStdioServerTransport()
    .WithTools<AlibreTools>();

var host = builder.Build();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

// Stops the server; the stdio transport may sit waiting for input, so don't let it keep the process alive.
void CloseServer(string why)
{
    ServerLog.Write("closing: " + why);
    lifetime.StopApplication();
    _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ =>
    {
        ServerLog.Write("didn't stop within 3 s; exiting");
        Environment.Exit(0);
    });
}

TrayWindow? tray = null;
if (Environment.GetEnvironmentVariable("MCPFORALIBRE_NO_TRAY") != "1")
{
    tray = new TrayWindow(host.Services.GetRequiredService<ServerStats>(), host.Services.GetRequiredService<AlibreBridge>(),
                          () => CloseServer("Close server in the tray menu"));
    lifetime.ApplicationStopping.Register(tray.Dispose);
}

// End with the Claude session that started us, even if it never closes stdin.
ParentWatch.Start(() => CloseServer("the parent process has gone"), TimeSpan.FromSeconds(5));

try
{
    await host.RunAsync();
    ServerLog.Write("stopped (stdin closed or asked to stop)");
}
catch (OperationCanceledException)
{
    ServerLog.Write("stopped before it had finished starting");
}
catch (Exception ex)
{
    ServerLog.Write("failed: " + ex);
    throw;
}
tray?.Dispose();
