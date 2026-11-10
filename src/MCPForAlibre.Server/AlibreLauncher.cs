using System.Diagnostics;
using Microsoft.Win32;

namespace MCPForAlibre.Server;

// Starts Alibre Design when a call finds it closed, and waits for the add-on's pipe (Frank's choice:
// on any call). Only one server starts it at a time (a named mutex); an Alibre that is already
// running but not answering yet is waited for, not started again. MCPFORALIBRE_NO_LAUNCH=1 turns
// this off; MCPFORALIBRE_ALIBRE_EXE says where Alibre is when Windows' install record doesn't.
public static class AlibreLauncher
{
    public const string ProcessName = "Alibre Design";
    public static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(3);

    public static bool Disabled => Environment.GetEnvironmentVariable("MCPFORALIBRE_NO_LAUNCH") == "1";

    // Whether the named pipe exists now, without connecting to it (which the add-on would log).
    public static bool PipeExists(string pipeName)
    {
        try
        {
            foreach (string path in Directory.EnumerateFiles(@"\\.\pipe\"))
                if (string.Equals(Path.GetFileName(path), pipeName, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        catch (Exception)
        {
            // Can't list pipes: let the connect try decide.
            return true;
        }
        return false;
    }

    // Alibre Design.exe: the override, else the newest "Alibre Design" in Windows' uninstall records.
    public static string? FindAlibre()
    {
        string? overridePath = Environment.GetEnvironmentVariable("MCPFORALIBRE_ALIBRE_EXE");
        if (!string.IsNullOrEmpty(overridePath))
            return File.Exists(overridePath) ? overridePath : null;

        string? best = null;
        Version? bestVersion = null;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null)
                continue;
            foreach (string name in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(name);
                if (entry?.GetValue("DisplayName") is not string display || !display.StartsWith("Alibre Design", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (entry.GetValue("InstallLocation") is not string location || string.IsNullOrEmpty(location))
                    continue;
                string exe = Path.Combine(location, "Program", ProcessName + ".exe");
                if (!File.Exists(exe))
                    continue;
                Version.TryParse(entry.GetValue("DisplayVersion") as string, out Version? version);
                version ??= new Version(0, 0);
                if (bestVersion is null || version > bestVersion)
                {
                    best = exe;
                    bestVersion = version;
                }
            }
        }
        return best;
    }

    // Makes sure Alibre is running with the add-on's pipe up, starting it if needed.
    public static async Task EnsureRunningAsync(string pipeName, Action<string?> status, CancellationToken cancel)
    {
        if (PipeExists(pipeName))
            return;
        try
        {
            Process? running = Running();
            if (running is null)
            {
                // Another server may be starting it at the same moment: one at a time, and look again inside.
                using var mutex = new Mutex(false, @"Local\MCPForAlibre.StartingAlibre");
                bool owned = false;
                try
                {
                    try { owned = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
                    catch (AbandonedMutexException) { owned = true; }
                    running = Running();
                    if (running is null && !PipeExists(pipeName))
                        running = Start(status);
                }
                finally
                {
                    if (owned)
                        mutex.ReleaseMutex();
                }
            }
            if (running is not null)
                await WaitForPipeAsync(pipeName, running, status, cancel);
        }
        finally
        {
            status(null);
        }
    }

    static Process? Running()
    {
        var all = Process.GetProcessesByName(ProcessName);
        return all.Length == 0 ? null : all.OrderBy(p => SafeStart(p)).First();
    }

    static DateTime SafeStart(Process p)
    {
        try { return p.StartTime; } catch (Exception) { return DateTime.Now; }
    }

    static Process Start(Action<string?> status)
    {
        string exe = FindAlibre() ?? throw new AlibreException(
            "Alibre Design isn't running, and it isn't installed where Windows records it. " +
            "Start it yourself, or set MCPFORALIBRE_ALIBRE_EXE to its Alibre Design.exe.");
        status("starting Alibre");
        ServerLog.Write("starting Alibre: " + exe);
        var process = Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        }) ?? throw new AlibreException("Couldn't start " + exe + ".");
        ServerLog.Write("started Alibre, process " + process.Id);
        return process;
    }

    // Waits for the pipe until LoadTimeout after Alibre started (so an Alibre that's been running
    // a while without the pipe fails at once: the add-on isn't loaded).
    static async Task WaitForPipeAsync(string pipeName, Process alibre, Action<string?> status, CancellationToken cancel)
    {
        status("waiting for Alibre to load");
        DateTime started = SafeStart(alibre);
        var watch = Stopwatch.StartNew();
        while (!PipeExists(pipeName))
        {
            alibre.Refresh();
            if (alibre.HasExited)
                throw new AlibreException("Alibre Design closed before the MCPForAlibre add-on answered.");
            if (DateTime.Now - started > LoadTimeout)
                throw new AlibreException("Alibre Design is running, but the MCPForAlibre add-on isn't answering. " +
                                          "Is it installed (deploy\\install.ps1) and loaded? Its MCPForAlibre > Status menu shows the pipe.");
            await Task.Delay(500, cancel);
        }
        ServerLog.Write($"Alibre's add-on answered after {watch.Elapsed.TotalSeconds:0.0} s of waiting");
        // The pipe exists as soon as the add-on loads; give Alibre a moment to finish opening its window.
        await Task.Delay(TimeSpan.FromSeconds(2), cancel);
    }
}
