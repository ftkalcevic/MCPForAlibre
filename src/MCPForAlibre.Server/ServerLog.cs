namespace MCPForAlibre.Server;

// The server's own log, %LOCALAPPDATA%\MCPForAlibre\MCPForAlibre.Server.log: starts, the parent
// it watches, and why it stopped, so a server that didn't start can be told from one that stopped.
// Shared by every running server, so each line has the process id.
static class ServerLog
{
    static readonly object s_lock = new();

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCPForAlibre", "MCPForAlibre.Server.log");

    public static void Write(string text)
    {
        try
        {
            lock (s_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                // FileShare.ReadWrite: other servers append to the same file.
                using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(stream);
                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {text}");
            }
        }
        catch (Exception)
        {
            // Logging must never stop the server.
        }
    }
}
