using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace MCPForAlibre.Server;

// The tray icon and its status window (Frank's choice: in the server, opened on demand).
// Runs on its own STA thread with a WinForms message loop; the MCP host keeps stdio on the others.
// The window shows this server's calls and the add-on's shared log; closing it only hides it.
public sealed class TrayWindow : IDisposable
{
    readonly ServerStats m_stats;
    readonly AlibreBridge m_bridge;
    readonly Action m_closeServer;
    readonly Thread m_thread;
    readonly ManualResetEventSlim m_ready = new();
    ApplicationContext? m_context;
    NotifyIcon? m_tray;
    Control? m_invoker; // a hidden window on the tray thread, for running work there
    StatusForm? m_form;

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MCPForAlibre", "MCPForAlibre.log");

    public static Icon AppIcon { get; } = LoadIcon();

    static Icon LoadIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application; }
        catch (Exception) { return SystemIcons.Application; }
    }

    // closeServer stops the whole server (Close in the menu, after asking).
    public TrayWindow(ServerStats stats, AlibreBridge bridge, Action closeServer)
    {
        m_stats = stats;
        m_bridge = bridge;
        m_closeServer = closeServer;
        m_thread = new Thread(Run) { IsBackground = true, Name = "MCPForAlibre tray" };
        m_thread.SetApartmentState(ApartmentState.STA);
        m_thread.Start();
        m_ready.Wait(TimeSpan.FromSeconds(5));
    }

    void Run()
    {
        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        m_context = new ApplicationContext();
        m_invoker = new Control();
        m_invoker.CreateControl();
        _ = m_invoker.Handle;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowWindow());
        menu.Items.Add("Open log file", null, (_, _) => OpenLogFile());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Close server", null, (_, _) => AskToClose());
        m_tray = new NotifyIcon
        {
            Icon = AppIcon,
            Text = "MCPForAlibre",
            Visible = true,
            ContextMenuStrip = menu,
        };
        m_tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };

        // Keep the tooltip current: calls and errors so far.
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) =>
        {
            var s = m_stats.Take();
            string text = $"MCPForAlibre: {s.Calls} calls, {s.Errors} errors";
            m_tray.Text = text.Length > 63 ? text[..63] : text;
        };
        timer.Start();

        m_ready.Set();
        Application.Run(m_context);
        timer.Dispose();
        m_tray.Visible = false;
        m_tray.Dispose();
    }

    void ShowWindow()
    {
        if (m_form is null || m_form.IsDisposed)
            m_form = new StatusForm(m_stats, m_bridge);
        m_form.Show();
        if (m_form.WindowState == FormWindowState.Minimized)
            m_form.WindowState = FormWindowState.Normal;
        m_form.Activate();
    }

    // Closing ends this Claude session's link to Alibre until Claude starts the server again.
    void AskToClose()
    {
        var answer = MessageBox.Show(
            "Close this MCPForAlibre server (process " + Environment.ProcessId + ")?\r\n\r\n" +
            "The Claude session that started it loses its Alibre tools until Claude starts the server again " +
            "(restart Claude, or reconnect the alibre server). Alibre and other sessions aren't affected.",
            "MCPForAlibre", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.OK)
            m_closeServer();
    }

    internal static void OpenLogFile()
    {
        if (File.Exists(LogPath))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LogPath) { UseShellExecute = true });
        else
            MessageBox.Show("There's no log yet at " + LogPath + ".", "MCPForAlibre");
    }

    // Removes the tray icon (left behind until the mouse passes over it otherwise) and ends the thread.
    public void Dispose()
    {
        try
        {
            if (m_tray is not null && m_context is not null && m_thread.IsAlive)
            {
                var tray = m_tray;
                var form = m_form;
                var context = m_context;
                void close()
                {
                    form?.Dispose();
                    tray.Visible = false;
                    context.ExitThread();
                }
                if (Thread.CurrentThread == m_thread)
                    close(); // from the menu's Close: already on the tray thread, so don't wait on it
                else
                {
                    m_invoker?.BeginInvoke(new Action(close));
                    m_thread.Join(TimeSpan.FromSeconds(2));
                }
            }
        }
        catch (Exception)
        {
            // Closing down: nothing more to do.
        }
    }
}

// The status window: the icon and what this server knows, a Calls tab and a Log tab.
sealed class StatusForm : Form
{
    readonly ServerStats m_stats;
    readonly AlibreBridge m_bridge;
    readonly Label m_summary = new() { AutoSize = true };
    readonly ListView m_tools = NewList(("Tool", 170), ("Calls", 60), ("Errors", 60), ("Average ms", 90), ("Longest ms", 90), ("Last", 80));
    readonly ListView m_recent = NewList(("Time", 80), ("Tool", 170), ("ms", 70), ("Result", 420));
    readonly TextBox m_log = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
        Font = new Font(FontFamily.GenericMonospace, 9f), BackColor = SystemColors.Window,
    };
    readonly CheckBox m_follow = new() { Text = "Follow the end", Checked = true, AutoSize = true };
    readonly System.Windows.Forms.Timer m_timer = new() { Interval = 1000 };
    long m_shownVersion = -1;
    DateTime m_logWritten;
    long m_logLength = -1;

    const int LogLines = 500;

    public StatusForm(ServerStats stats, AlibreBridge bridge)
    {
        m_stats = stats;
        m_bridge = bridge;
        Text = "MCPForAlibre";
        Icon = TrayWindow.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(820, 560);
        MinimumSize = new Size(560, 380);
        Font = SystemFonts.MessageBoxFont ?? Font;

        // Header: the icon at 48 px, the name and the summary.
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(10, 10, 10, 6) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var picture = new PictureBox
        {
            Image = new Icon(TrayWindow.AppIcon, 48, 48).ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(48, 48), Margin = new Padding(0, 0, 10, 0),
        };
        var title = new Label { Text = "MCPForAlibre", AutoSize = true, Font = new Font(Font.FontFamily, 13f, FontStyle.Bold) };
        var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        text.Controls.Add(title);
        text.Controls.Add(m_summary);
        header.Controls.Add(picture, 0, 0);
        header.Controls.Add(text, 1, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var callsPage = new TabPage("Calls") { Padding = new Padding(6) };
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 150 };
        split.Panel1.Controls.Add(m_tools);
        split.Panel2.Controls.Add(m_recent);
        callsPage.Controls.Add(split);

        var logPage = new TabPage("Log") { Padding = new Padding(6) };
        var logButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
        var openLog = new Button { Text = "Open log file", AutoSize = true };
        openLog.Click += (_, _) => TrayWindow.OpenLogFile();
        logButtons.Controls.Add(openLog);
        logButtons.Controls.Add(m_follow);
        logPage.Controls.Add(m_log);
        logPage.Controls.Add(logButtons);

        tabs.TabPages.Add(callsPage);
        tabs.TabPages.Add(logPage);
        Controls.Add(tabs);
        Controls.Add(header);

        m_timer.Tick += (_, _) => UpdateView(force: false);
        m_timer.Start();
        Shown += (_, _) => UpdateView(force: true);
        VisibleChanged += (_, _) => { if (Visible) UpdateView(force: true); };
    }

    static ListView NewList(params (string Name, int Width)[] columns)
    {
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false };
        foreach (var (name, width) in columns)
            list.Columns.Add(name, width);
        return list;
    }

    // Closing hides the window; the server keeps running for Claude.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        m_timer.Dispose();
        base.OnFormClosing(e);
    }

    void UpdateView(bool force)
    {
        if (!Visible)
            return;
        RefreshSummary();
        if (force || m_stats.Version != m_shownVersion)
            RefreshCalls();
        RefreshLog(force);
    }

    void RefreshSummary()
    {
        var s = m_stats.Take();
        TimeSpan up = DateTime.Now - s.Started;
        string alibre = m_bridge.Status is string doing
            ? doing + "…"
            : m_bridge.IsConnected
                ? "connected to Alibre (add-on " + (m_bridge.AddOnVersion ?? "?") + ")"
                : "not connected to Alibre yet (it connects on the first call, starting Alibre if needed)";
        m_summary.Text =
            $"Process {Environment.ProcessId}, started {s.Started:HH:mm:ss} ({(int)up.TotalHours}:{up:mm\\:ss} ago), {alibre}\r\n" +
            $"{s.Calls} calls, {s.Errors} errors";
    }

    void RefreshCalls()
    {
        var s = m_stats.Take();
        m_shownVersion = m_stats.Version;
        m_tools.BeginUpdate();
        m_tools.Items.Clear();
        foreach (var t in s.Tools)
            m_tools.Items.Add(new ListViewItem(new[]
            {
                t.Tool, t.Calls.ToString(CultureInfo.CurrentCulture), t.Errors.ToString(CultureInfo.CurrentCulture),
                t.Average.ToString("0", CultureInfo.CurrentCulture), t.Longest.ToString("0", CultureInfo.CurrentCulture),
                t.Last.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
            }));
        m_tools.EndUpdate();

        m_recent.BeginUpdate();
        m_recent.Items.Clear();
        foreach (var c in s.Recent)
        {
            var item = new ListViewItem(new[]
            {
                c.At.ToString("HH:mm:ss", CultureInfo.CurrentCulture), c.Tool,
                c.Milliseconds.ToString("0", CultureInfo.CurrentCulture), c.Error ?? "OK",
            });
            if (c.Error is not null)
                item.ForeColor = Color.Firebrick;
            m_recent.Items.Add(item);
        }
        m_recent.EndUpdate();
    }

    // The last lines of the add-on's log, read again only when the file changes. The add-on
    // appends with File.AppendAllText, so open it shared for reading and writing.
    void RefreshLog(bool force)
    {
        try
        {
            var info = new FileInfo(TrayWindow.LogPath);
            if (!info.Exists)
            {
                m_log.Text = "No log yet at " + TrayWindow.LogPath + ". The add-on writes it when Alibre loads it.";
                return;
            }
            if (!force && info.LastWriteTimeUtc == m_logWritten && info.Length == m_logLength)
                return;
            m_logWritten = info.LastWriteTimeUtc;
            m_logLength = info.Length;

            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            const int tailBytes = 256 * 1024;
            if (stream.Length > tailBytes)
                stream.Seek(-tailBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            if (stream.Length > tailBytes && lines.Count > 0)
                lines.RemoveAt(0); // probably cut part way through
            if (lines.Count > LogLines)
                lines = lines.GetRange(lines.Count - LogLines, LogLines);

            int firstVisible = m_log.GetCharIndexFromPosition(new Point(1, 1));
            m_log.Text = string.Join("\r\n", lines);
            if (m_follow.Checked)
            {
                m_log.SelectionStart = m_log.TextLength;
                m_log.ScrollToCaret();
            }
            else
            {
                m_log.SelectionStart = Math.Min(firstVisible, m_log.TextLength);
                m_log.ScrollToCaret();
            }
        }
        catch (IOException ex)
        {
            m_log.Text = "Couldn't read " + TrayWindow.LogPath + ": " + ex.Message;
        }
    }
}
