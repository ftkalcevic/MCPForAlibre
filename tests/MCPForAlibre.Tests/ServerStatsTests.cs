using MCPForAlibre.Server;
using Xunit;

namespace MCPForAlibre.Tests;

public sealed class ServerStatsTests
{
    [Fact]
    public async Task Measure_counts_calls_errors_and_times_per_tool()
    {
        var stats = new ServerStats();
        Assert.Equal("ok", await stats.Measure("status", () => Task.FromResult("ok")));
        Assert.Equal("ok", await stats.Measure("status", () => Task.FromResult("ok")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            stats.Measure<string>("extrude", () => throw new InvalidOperationException("no sketch")));

        var s = stats.Take();
        Assert.Equal(3, s.Calls);
        Assert.Equal(1, s.Errors);
        Assert.Equal(new[] { "extrude", "status" }, s.Tools.Select(t => t.Tool));
        var extrude = s.Tools.Single(t => t.Tool == "extrude");
        Assert.Equal((1, 1), (extrude.Calls, extrude.Errors));
        Assert.Equal(2, s.Tools.Single(t => t.Tool == "status").Calls);
        // Newest first, with the error's message.
        Assert.Equal("extrude", s.Recent[0].Tool);
        Assert.Equal("no sketch", s.Recent[0].Error);
        Assert.Null(s.Recent[1].Error);
    }

    [Fact]
    public void Recent_calls_are_capped_and_the_version_moves()
    {
        var stats = new ServerStats();
        long before = stats.Version;
        for (int i = 0; i < ServerStats.RecentLimit + 50; ++i)
            stats.Record("status", TimeSpan.FromMilliseconds(i), null);
        var s = stats.Take();
        Assert.Equal(ServerStats.RecentLimit, s.Recent.Count);
        Assert.Equal(ServerStats.RecentLimit + 50, s.Calls);
        Assert.True(stats.Version > before);
        Assert.Equal(ServerStats.RecentLimit + 49, s.Tools[0].Longest);
    }
}
