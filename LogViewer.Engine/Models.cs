using Serilog.Events;

namespace LogViewer.Engine;

[Flags]
public enum LogLevelMask
{
    None = 0,
    Verbose = 1,
    Debug = 2,
    Information = 4,
    Warning = 8,
    Error = 16,
    Fatal = 32,
    All = 63
}

public enum SortOrder
{
    Ascending,
    Descending
}

/// <summary>
/// Everything except <see cref="Filter"/> is answered from the in-memory index alone, without touching the file.
/// </summary>
public sealed class LogQuery
{
    public LogLevelMask Levels { get; init; } = LogLevelMask.All;

    /// <summary>Inclusive.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Exclusive.</summary>
    public DateTimeOffset? To { get; init; }

    public int? TemplateId { get; init; }

    public int? RequestId { get; init; }

    /// <summary>Rows carrying this trace id (@tr). Files written outside a request, or by an older logger, simply have no trace ids.</summary>
    public int? TraceId { get; init; }

    /// <summary>The search box: a plain word or a Serilog expression. This is the only part that re-reads lines.</summary>
    public string? Filter { get; init; }
}

public sealed record LogEntry(
    int RowId,
    DateTimeOffset Timestamp,
    LogEventLevel Level,
    string MessageTemplate,
    string RenderedMessage,
    string? Exception,
    IReadOnlyDictionary<string, LogEventPropertyValue> Properties,
    string? TraceId = null,
    string? SpanId = null);

public readonly record struct RowInfo(
    DateTimeOffset Timestamp,
    LogEventLevel Level,
    string MessageTemplate,
    bool HasException,
    int TemplateId,
    int RequestId,
    int TraceId);

public readonly record struct LevelCounts(long Verbose, long Debug, long Information, long Warning, long Error, long Fatal)
{
    public long Total => Verbose + Debug + Information + Warning + Error + Fatal;
}

public sealed record PatternCount(int TemplateId, string Template, long Count);

public sealed record HistogramBucket(DateTimeOffset Start, long[] Counts);

public sealed record Histogram(DateTimeOffset Start, TimeSpan BucketWidth, IReadOnlyList<HistogramBucket> Buckets);

public sealed record IndexStats(
    int Rows,
    int MalformedLines,
    long IndexedBytes,
    int TemplateCount,
    int RequestCount,
    int TraceCount,
    bool Chronological,
    long ApproxIndexMemoryBytes);

/// <summary>
/// A set of row ids in chronological order. An unfiltered set is just a count, so it costs no memory.
/// </summary>
public sealed class RowSet
{
    private readonly int[]? _rows;

    internal RowSet(int count) => Count = count;

    /// <summary>Only the first <paramref name="count"/> entries of <paramref name="rows"/> belong to this set; the index may append to the array beyond that.</summary>
    internal RowSet(int[] rows, int count)
    {
        _rows = rows;
        Count = count;
    }

    internal RowSet(int[] rows) : this(rows, rows.Length)
    {
    }

    public int Count { get; }

    public int RowAt(int position) => _rows is null ? position : _rows[position];
}
