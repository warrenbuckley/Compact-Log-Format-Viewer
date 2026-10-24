using LogViewer.Engine.Expressions;
using Serilog.Events;
using Serilog.Formatting.Compact.Reader;

namespace LogViewer.Engine.Bench;

/// <summary>
/// The current LogViewer.Server LogParser read and search path, copied so it can run on the same files.
/// Everything is parsed into a List&lt;LogEvent&gt; on open and every Search sorts and scans the full list.
/// </summary>
internal sealed class Baseline
{
    private List<LogEvent> _logItems = new();

    public int Open(string path)
    {
        var logItems = new List<LogEvent>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var stream = new StreamReader(fs);
        using var reader = new LogEventReader(stream);
        while (reader.TryRead(out var evt))
            logItems.Add(evt);

        _logItems = logItems;
        return logItems.Count;
    }

    public (long errors, long info) Totals()
    {
        var info = _logItems.Count(l => l.Level == LogEventLevel.Information);
        var errors = _logItems.Count(l => l.Level == LogEventLevel.Fatal || l.Level == LogEventLevel.Error || l.Exception != null);
        return (errors, info);
    }

    /// <summary>Mirrors LogParser.Search: filter, count, sort the whole list, take a page, render it, and group templates.</summary>
    public (int total, int pageRows, int templates) Search(int pageNumber, int pageSize, string? filter)
    {
        var predicate = FilterCompiler.Compile(filter);
        var filtered = predicate is null ? _logItems : _logItems.Where(predicate);
        var total = filtered.Count();
        var page = filtered
            .OrderByDescending(x => x.Timestamp)
            .Skip(pageSize * (pageNumber - 1))
            .Take(pageSize)
            .Select(x => (x.Timestamp, x.Level, x.MessageTemplate.Text, Exception: x.Exception?.ToString(), x.Properties, Rendered: x.RenderMessage()))
            .ToList();

        var templates = filtered.GroupBy(l => l.MessageTemplate.Text).Select(g => (g.Key, Count: g.Count())).OrderByDescending(t => t.Count).ToList();
        return (total, page.Count, templates.Count);
    }
}
