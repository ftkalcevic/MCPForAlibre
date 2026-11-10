using System.Diagnostics;

namespace MCPForAlibre.Server;

// What this server has done since it started: every tool call's time and outcome, per-tool totals
// and the last calls. Written by tool calls, read by the tray window, so everything is locked.
public sealed class ServerStats
{
    public const int RecentLimit = 200;

    public sealed record Call(DateTime At, string Tool, double Milliseconds, string? Error);

    public sealed class ToolTotals
    {
        public int Calls { get; internal set; }
        public int Errors { get; internal set; }
        public double TotalMilliseconds { get; internal set; }
        public double LongestMilliseconds { get; internal set; }
        public DateTime Last { get; internal set; }
        public double AverageMilliseconds => Calls == 0 ? 0 : TotalMilliseconds / Calls;
    }

    readonly object m_lock = new();
    readonly Dictionary<string, ToolTotals> m_tools = new(StringComparer.Ordinal);
    readonly LinkedList<Call> m_recent = new();
    int m_calls;
    int m_errors;
    long m_version;

    public DateTime Started { get; } = DateTime.Now;

    // Changes whenever a call is recorded, so the window redraws only when something happened.
    public long Version { get { lock (m_lock) return m_version; } }

    public void Record(string tool, TimeSpan elapsed, string? error)
    {
        lock (m_lock)
        {
            var call = new Call(DateTime.Now, tool, elapsed.TotalMilliseconds, error);
            if (!m_tools.TryGetValue(tool, out var totals))
                m_tools[tool] = totals = new ToolTotals();
            totals.Calls++;
            totals.TotalMilliseconds += call.Milliseconds;
            totals.LongestMilliseconds = Math.Max(totals.LongestMilliseconds, call.Milliseconds);
            totals.Last = call.At;
            m_calls++;
            if (error is not null)
            {
                totals.Errors++;
                m_errors++;
            }
            m_recent.AddFirst(call);
            while (m_recent.Count > RecentLimit)
                m_recent.RemoveLast();
            m_version++;
        }
    }

    // Times a call and records it, rethrowing whatever it threw.
    public async Task<T> Measure<T>(string tool, Func<Task<T>> call)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            T result = await call();
            Record(tool, watch.Elapsed, null);
            return result;
        }
        catch (Exception ex)
        {
            Record(tool, watch.Elapsed, ex.Message);
            throw;
        }
    }

    public sealed record Snapshot(DateTime Started, int Calls, int Errors,
                                  IReadOnlyList<(string Tool, int Calls, int Errors, double Average, double Longest, DateTime Last)> Tools,
                                  IReadOnlyList<Call> Recent);

    public Snapshot Take()
    {
        lock (m_lock)
        {
            var tools = m_tools.OrderBy(t => t.Key, StringComparer.Ordinal)
                               .Select(t => (t.Key, t.Value.Calls, t.Value.Errors, t.Value.AverageMilliseconds,
                                             t.Value.LongestMilliseconds, t.Value.Last))
                               .ToList();
            return new Snapshot(Started, m_calls, m_errors, tools, m_recent.ToList());
        }
    }
}
