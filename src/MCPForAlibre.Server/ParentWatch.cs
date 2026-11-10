using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MCPForAlibre.Server;

// Ends the server when the process that started it (the Claude session) goes away. A session that
// dies without closing the server's stdin left servers running, which Frank had to kill by hand.
// Only a parent seen running is watched: if it has already gone at startup (a launcher that exits
// at once), the server stays up and ends when stdin closes, as before.
static class ParentWatch
{
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(IntPtr process, int infoClass, ref ProcessBasicInformation info, int size, out int returned);

    public static int? ParentId()
    {
        try
        {
            var info = new ProcessBasicInformation();
            using var self = Process.GetCurrentProcess();
            int status = NtQueryInformationProcess(self.Handle, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _);
            return status == 0 ? info.InheritedFromUniqueProcessId.ToInt32() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Checks every few seconds; calls onGone once when the parent has exited.
    public static void Start(Action onGone, TimeSpan every)
    {
        int? id = ParentId();
        if (id is null)
        {
            ServerLog.Write("couldn't find the parent process; the server ends when stdin closes");
            return;
        }
        Process parent;
        DateTime parentStarted;
        try
        {
            parent = Process.GetProcessById(id.Value);
            parentStarted = parent.StartTime;
            using var self = Process.GetCurrentProcess();
            if (parentStarted > self.StartTime)
            {
                ServerLog.Write($"parent {id} has already gone (its id is reused); the server ends when stdin closes");
                return;
            }
            ServerLog.Write($"watching parent {id} ({parent.ProcessName})");
        }
        catch (ArgumentException)
        {
            ServerLog.Write($"parent {id} has already gone; the server ends when stdin closes");
            return;
        }
        catch (Exception ex)
        {
            ServerLog.Write($"parent {id} can't be watched ({ex.Message}); the server ends when stdin closes");
            return;
        }
        var thread = new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(every);
                bool gone;
                try
                {
                    parent.Refresh();
                    // A new process could reuse the id; the start time tells them apart.
                    gone = parent.HasExited || parent.StartTime != parentStarted;
                }
                catch (Exception)
                {
                    gone = true;
                }
                if (gone)
                {
                    ServerLog.Write($"parent {id} has gone; closing");
                    onGone();
                    return;
                }
            }
        }) { IsBackground = true, Name = "MCPForAlibre parent watch" };
        thread.Start();
    }
}
