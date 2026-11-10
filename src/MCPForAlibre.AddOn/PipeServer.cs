using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace MCPForAlibre.AddOn
{
    // Serves the bridge protocol (docs\PROTOCOL.md) on the named pipe \\.\pipe\MCPForAlibre.
    // One JSON object per line each way: {"id":n,"method":"...","params":{...}} in,
    // {"id":n,"result":...} or {"id":n,"error":"..."} out.
    sealed class PipeServer
    {
        public const string PipeName = "MCPForAlibre";
        public const int ProtocolVersion = 1;
        const int kInstances = 4;
        static readonly TimeSpan kCallTimeout = TimeSpan.FromSeconds(120);

        readonly UiThread m_ui;
        readonly Commands m_commands;
        readonly List<Thread> m_threads = new List<Thread>();
        volatile bool m_stopping;
        int m_connections;
        int m_requests;

        public PipeServer(UiThread ui, Commands commands)
        {
            m_ui = ui;
            m_commands = commands;
        }

        public string StatusText =>
            "Pipe \\\\.\\pipe\\" + PipeName + ", " + m_connections + " connections, " + m_requests +
            " requests since Alibre started.\r\nLog: " + Log.FilePath;

        public void Start()
        {
            for (int i = 0; i < kInstances; ++i)
            {
                var thread = new Thread(Serve) { IsBackground = true, Name = "MCPForAlibre pipe " + i };
                m_threads.Add(thread);
                thread.Start();
            }
            Log.Write("pipe", "listening on " + PipeName);
        }

        public void Stop()
        {
            m_stopping = true;
        }

        static PipeSecurity CurrentUserOnly()
        {
            var security = new PipeSecurity();
            var user = WindowsIdentity.GetCurrent().User;
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
            return security;
        }

        void Serve()
        {
            while (!m_stopping)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, kInstances,
                               PipeTransmissionMode.Byte, PipeOptions.None, 65536, 65536, CurrentUserOnly()))
                    {
                        pipe.WaitForConnection();
                        Interlocked.Increment(ref m_connections);
                        Log.Write("pipe", "client connected");
                        HandleClient(pipe);
                        Log.Write("pipe", "client disconnected");
                    }
                }
                catch (Exception ex)
                {
                    if (m_stopping)
                        return;
                    Log.Write("pipe", "error: " + ex.Message);
                    Thread.Sleep(1000); // e.g. another Alibre already owns every instance of the pipe
                }
            }
        }

        void HandleClient(Stream pipe)
        {
            var utf8 = new UTF8Encoding(false);
            var reader = new StreamReader(pipe, utf8);
            var writer = new StreamWriter(pipe, utf8) { AutoFlush = true, NewLine = "\n" };
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0)
                    continue;
                Interlocked.Increment(ref m_requests);
                writer.WriteLine(json.Serialize(Handle(json, line)));
            }
        }

        Dictionary<string, object> Handle(JavaScriptSerializer json, string line)
        {
            object id = null;
            string method = "?";
            try
            {
                var request = json.Deserialize<Dictionary<string, object>>(line);
                request.TryGetValue("id", out id);
                method = request.TryGetValue("method", out object m) ? m as string : null;
                if (string.IsNullOrEmpty(method))
                    throw new ArgumentException("The request has no method.");
                var args = new Args(request.TryGetValue("params", out object p) ? p as Dictionary<string, object> : null);

                object result = method == "hello"
                    ? new Dictionary<string, object> { ["protocol"] = ProtocolVersion, ["addOn"] = typeof(PipeServer).Assembly.GetName().Version.ToString() }
                    : m_ui.Run(() => m_commands.Invoke(method, args), kCallTimeout);
                return new Dictionary<string, object> { ["id"] = id, ["result"] = result };
            }
            catch (Exception ex)
            {
                string message = Commands.Describe(ex);
                Log.Write("call", method + " failed: " + message);
                return new Dictionary<string, object> { ["id"] = id, ["error"] = message };
            }
        }
    }
}
