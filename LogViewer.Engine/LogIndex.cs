using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LogViewer.Engine.Expressions;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using Serilog.Events;
using Serilog.Formatting.Compact.Reader;
using Serilog.Parsing;

namespace LogViewer.Engine;

/// <summary>
/// A compact in-memory index over a CLEF file: where each line lives plus its timestamp, level, template and request.
/// The file is parsed once (in parallel); after that level counts, time ranges, the histogram, patterns and
/// request tracing never touch the file, and pages are read by seeking to just the rows being shown.
/// </summary>
public sealed class LogIndex : IDisposable
{
    private const int TargetChunkBytes = 16 * 1024 * 1024;
    private const int MaxChunks = 512;
    private const int BatchBytes = 1 << 20;
    private const int LevelBits = 7;
    private const byte HasExceptionFlag = 0x80;
    private static readonly System.Buffers.SearchValues<byte> Whitespace = System.Buffers.SearchValues.Create(" \t\r\n"u8);

    private readonly ReaderWriterLockSlim _lock = new();
    private readonly SafeFileHandle _handle;

    private long[] _offsets = Array.Empty<long>();
    private int[] _lengths = Array.Empty<int>();
    private long[] _ticks = Array.Empty<long>();
    private byte[] _levels = Array.Empty<byte>();
    private int[] _templates = Array.Empty<int>();
    private readonly string[] _correlationNames;
    private readonly byte[][] _correlationNamesUtf8;
    private int[]?[] _correlation; // one column per correlation property, null until a row has a value for it; -1 where a row has none
    private Utf8Interner[] _correlationTables;
    private long[] _correlationLines;
    private int[] _traces = Array.Empty<int>();
    private int _count;

    private Utf8Interner _templateTable = new();
    private Utf8Interner _traceTable = new();
    private static readonly MessageTemplateParser TemplateParser = new();
    private MessageTemplate?[] _templateObjects = Array.Empty<MessageTemplate?>(); // parsed lazily, shared by every event of that template

    private bool _chronological = true;
    private int[]? _order; // row ids sorted by (time, row), only kept when the file is not already in time order
    private int _orderCount;

    // Fingerprints of the start of the file and of the bytes just before the indexed end, to notice a file that was replaced
    private const int FingerprintBytes = 512;
    private ulong _headHash;
    private int _headLength;
    private ulong _tailHash;
    private int _tailLength;
    private long _indexedBytes;
    private int _malformed;

    private LogIndex(string path, LogIndexOptions options)
    {
        options.Validate();
        _correlationNames = options.CorrelationProperties.ToArray();
        _correlationNamesUtf8 = _correlationNames.Select(Encoding.UTF8.GetBytes).ToArray();
        _correlation = new int[]?[_correlationNames.Length];
        _correlationTables = NewTables();
        _correlationLines = new long[_correlationNames.Length];

        FilePath = path;
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
    }

    public string FilePath { get; }

    private Utf8Interner[] NewTables() => _correlationNames.Select(_ => new Utf8Interner()).ToArray();

    /// <summary>Increases whenever the index is thrown away and rebuilt because the file was truncated or replaced, so row ids from before no longer mean anything.</summary>
    public int Generation { get; private set; }

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    /// <summary>Indexes the whole file. Lines that are not valid CLEF are skipped and counted in <see cref="IndexStats.MalformedLines"/>.</summary>
    public static LogIndex Open(string path, IProgress<double>? progress = null, CancellationToken ct = default, LogIndexOptions? options = null)
    {
        var index = new LogIndex(path, options ?? new LogIndexOptions());
        try
        {
            index.Refresh(progress, ct);
            return index;
        }
        catch
        {
            index.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Picks up lines appended since the last call, reading only the new bytes. Starts over if the file was truncated.
    /// A line that is still being written (no newline yet, not complete JSON) is left for the next call.
    /// </summary>
    /// <returns>true when rows were added.</returns>
    public bool Refresh(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        _lock.EnterWriteLock();
        try
        {
            var length = RandomAccess.GetLength(_handle);
            if (length < _indexedBytes || (_indexedBytes > 0 && !FingerprintsMatch()))
                Reset();

            if (length == _indexedBytes)
                return false;

            var end = ComputeIndexableEnd(_indexedBytes, length);
            if (end <= _indexedBytes)
                return false;

            var before = _count;
            var chunks = ScanChunks(_indexedBytes, end, progress, ct);
            Append(chunks);
            _indexedBytes = end;
            RememberFingerprints();
            return _count > before;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public IndexStats GetStats()
    {
        _lock.EnterReadLock();
        try
        {
            var columns = (long)_offsets.Length * (8 + 4 + 8 + 1 + 4 + 4) + (_order?.Length ?? 0) * 4L
                          + _correlation.Sum(c => c is null ? 0L : (long)c.Length * 4);
            var correlations = _correlationNames.Select((name, i) => new CorrelationStats(name, _correlationLines[i], _correlationTables[i].Count)).ToList();
            return new IndexStats(
                _count, _malformed, _indexedBytes, _templateTable.Count, correlations, _traceTable.Count, _chronological,
                columns + _templateTable.ApproxBytes() + _traceTable.ApproxBytes() + _correlationTables.Sum(t => t.ApproxBytes()));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    // ---------------------------------------------------------------- queries

    public RowSet All()
    {
        _lock.EnterReadLock();
        try { return AllRows(); }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Index fields (level, time, template, request) are filtered straight from memory. Only <see cref="LogQuery.Filter"/>
    /// has to parse lines, and it only parses the rows the index fields have not already ruled out.
    /// </summary>
    public RowSet Query(LogQuery query, CancellationToken ct = default)
    {
        var filter = FilterCompiler.Compile(query.Filter);

        _lock.EnterReadLock();
        try
        {
            var rows = FilterByIndex(query, ct);
            return filter is null ? rows : Refine(rows, filter, ct);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public LevelCounts CountLevels(RowSet rows)
    {
        _lock.EnterReadLock();
        try
        {
            Span<long> counts = stackalloc long[8];
            for (var i = 0; i < rows.Count; i++)
                counts[_levels[rows.RowAt(i)] & 7]++;

            return new LevelCounts(counts[0], counts[1], counts[2], counts[3], counts[4], counts[5]);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>Error or Fatal level, or any row that carries an exception.</summary>
    public long CountErrors(RowSet rows)
    {
        _lock.EnterReadLock();
        try
        {
            long errors = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                var level = _levels[rows.RowAt(i)];
                if ((level & HasExceptionFlag) != 0 || (level & 7) >= 4)
                    errors++;
            }

            return errors;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Groups by message template, most frequent first. Templates with the same count are ordered by where they first appear among
    /// the rows, in file order, which is how the server has always listed them.
    /// </summary>
    public IReadOnlyList<PatternCount> GetPatterns(RowSet rows)
    {
        _lock.EnterReadLock();
        try
        {
            var counts = new long[_templateTable.Count];
            var firstRow = new int[counts.Length];
            Array.Fill(firstRow, int.MaxValue);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows.RowAt(i);
                var template = _templates[row];
                counts[template]++;
                if (row < firstRow[template])
                    firstRow[template] = row;
            }

            return Enumerable.Range(0, counts.Length)
                .Where(id => counts[id] > 0)
                .OrderByDescending(id => counts[id])
                .ThenBy(id => firstRow[id])
                .Select(id => new PatternCount(id, TemplateText(id), counts[id]))
                .ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>Counts per time bucket per level (indexed by <see cref="LogEventLevel"/>), over the span of <paramref name="rows"/>.</summary>
    public Histogram GetHistogram(RowSet rows, int bucketCount)
    {
        if (bucketCount < 1)
            throw new ArgumentOutOfRangeException(nameof(bucketCount));

        _lock.EnterReadLock();
        try
        {
            if (rows.Count == 0)
                return new Histogram(DateTimeOffset.MinValue, TimeSpan.Zero, Array.Empty<HistogramBucket>());

            var min = _ticks[rows.RowAt(0)];
            var max = _ticks[rows.RowAt(rows.Count - 1)];
            var span = max - min + 1;
            var width = Math.Max(1, (span + bucketCount - 1) / bucketCount);
            var buckets = (int)((span + width - 1) / width);

            var counts = new long[buckets][];
            for (var b = 0; b < buckets; b++)
                counts[b] = new long[6];

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows.RowAt(i);
                var bucket = (int)Math.Min(buckets - 1, (_ticks[row] - min) / width);
                counts[bucket][_levels[row] & 7]++;
            }

            var result = new HistogramBucket[buckets];
            for (var b = 0; b < buckets; b++)
                result[b] = new HistogramBucket(new DateTimeOffset(min + b * width, TimeSpan.Zero), counts[b]);

            return new Histogram(new DateTimeOffset(min, TimeSpan.Zero), TimeSpan.FromTicks(width), result);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>The timestamp of the oldest row, or null for an empty index.</summary>
    public DateTimeOffset? OldestTimestamp => EdgeTimestamp(newest: false);

    /// <summary>The timestamp of the newest row (what <c>last:</c> counts back from), or null for an empty index.</summary>
    public DateTimeOffset? NewestTimestamp => EdgeTimestamp(newest: true);

    private DateTimeOffset? EdgeTimestamp(bool newest)
    {
        _lock.EnterReadLock();
        try { return _count == 0 ? null : new DateTimeOffset(_ticks[RowAtPosition(newest ? _count - 1 : 0)], TimeSpan.Zero); }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Runs what was typed in the search box: time tokens (<see cref="SearchText"/>) become the index time range, and the rest is the
    /// ordinary filter, so text without tokens behaves exactly as <see cref="Query"/> with that filter.
    /// </summary>
    public RowSet QueryText(string? text, LogLevelMask levels = LogLevelMask.All, CancellationToken ct = default)
    {
        var parsed = SearchText.Parse(text, NewestTimestamp ?? DateTimeOffset.UtcNow);
        return Query(new LogQuery { Levels = levels, From = parsed.From, To = parsed.To, Filter = parsed.Filter }, ct);
    }

    /// <summary>
    /// For "same request" (or any other correlation property): the value to pass as <see cref="LogQuery.Correlation"/>, or null when the
    /// property is not indexed (see <see cref="LogIndexOptions.CorrelationProperties"/>) or no row carries that value.
    /// </summary>
    public CorrelationFilter? FindCorrelation(string property, string value)
    {
        var column = Array.IndexOf(_correlationNames, property);
        if (column < 0)
            return null;

        _lock.EnterReadLock();
        try
        {
            var id = _correlationTables[column].Find(Encoding.UTF8.GetBytes(value));
            return id < 0 ? null : new CorrelationFilter(property, id);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>The value one row has for a correlation property, or null if the row has none or the property is not indexed.</summary>
    public string? GetCorrelationValue(int row, string property)
    {
        var column = Array.IndexOf(_correlationNames, property);
        if (column < 0)
            return null;

        _lock.EnterReadLock();
        try
        {
            var id = _correlation[column]?[row] ?? -1;
            return id < 0 ? null : Encoding.UTF8.GetString(_correlationTables[column][id]);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>For "same trace": the id of a trace id string (32 hex digits), or null if no row carries it.</summary>
    public int? FindTraceId(string traceId)
    {
        _lock.EnterReadLock();
        try
        {
            var id = _traceTable.Find(Encoding.UTF8.GetBytes(traceId));
            return id < 0 ? null : id;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>The text behind a <see cref="RowInfo.TraceId"/>, or null for -1 (the row has none).</summary>
    public string? GetTraceText(int traceId) => TextOf(_traceTable, traceId);

    private string? TextOf(Utf8Interner table, int id)
    {
        if (id < 0)
            return null;

        _lock.EnterReadLock();
        try
        {
            return Encoding.UTF8.GetString(table[id]);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>Index-only facts about a row, with no parsing and no file access.</summary>
    public RowInfo GetRowInfo(int row)
    {
        _lock.EnterReadLock();
        try
        {
            var level = _levels[row];
            return new RowInfo(
                new DateTimeOffset(_ticks[row], TimeSpan.Zero),
                (LogEventLevel)(level & 7),
                TemplateText(_templates[row]),
                (level & HasExceptionFlag) != 0,
                _templates[row],
                _traces[row]);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>Reads and fully parses just the rows on one page.</summary>
    public IReadOnlyList<LogEntry> ReadPage(RowSet rows, int pageNumber, int pageSize, SortOrder sort)
    {
        _lock.EnterReadLock();
        try
        {
            var skip = Math.Max(0, (pageNumber - 1) * pageSize);
            var take = Math.Max(0, Math.Min(pageSize, rows.Count - skip));
            var page = new List<LogEntry>(take);
            var fast = new FastEventReader();
            var positions = sort == SortOrder.Ascending ? Enumerable.Range(skip, take) : NewestFirstPositions(rows, skip, take);
            foreach (var position in positions)
                page.Add(ReadEntry(rows.RowAt(position), fast));

            return page;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Positions (in the chronological order of <paramref name="rows"/>) for a newest-first page. A plain reversal would flip rows that
    /// share a timestamp; the server's stable descending sort keeps those in file order, so each run of equal timestamps is read forwards.
    /// </summary>
    private IEnumerable<int> NewestFirstPositions(RowSet rows, int skip, int take)
    {
        var total = rows.Count;
        int runStart = 0, runEnd = -1;
        for (var d = skip; d < skip + take; d++)
        {
            var p = total - 1 - d; // where a plain reversal would look
            if (p < runStart || p > runEnd)
            {
                var ticks = _ticks[rows.RowAt(p)];
                runStart = p;
                while (runStart > 0 && _ticks[rows.RowAt(runStart - 1)] == ticks)
                    runStart--;

                runEnd = p;
                while (runEnd < total - 1 && _ticks[rows.RowAt(runEnd + 1)] == ticks)
                    runEnd++;
            }

            yield return runStart + (d - (total - 1 - runEnd));
        }
    }

    /// <summary>
    /// Every readable event in file order, which is what an export writes. Rows are read in blocks of a megabyte or so, so a huge file is
    /// never held in memory, and a line that cannot be read is skipped. The index may keep growing while this runs; rows added after it
    /// started are not included.
    /// </summary>
    public IEnumerable<LogEvent> ReadEvents(CancellationToken ct = default)
    {
        const int Block = 2048;
        var total = Count;
        var fast = new FastEventReader();

        for (var first = 0; first < total; first += Block)
        {
            ct.ThrowIfCancellationRequested();
            var events = new List<LogEvent>(Block);

            _lock.EnterReadLock();
            try
            {
                var end = Math.Min(Math.Min(first + Block, total), _count);
                if (end <= first)
                    yield break; // the file was replaced and is now shorter

                // rows are in file order here, so the block is one contiguous byte range
                var start = _offsets[first];
                var buffer = new byte[(int)(_offsets[end - 1] + _lengths[end - 1] - start)];
                ReadExactly(buffer, start);

                for (var row = first; row < end; row++)
                    if (TryParse(buffer.AsSpan((int)(_offsets[row] - start), _lengths[row]), TemplateFor(_templates[row]), fast, out var evt))
                        events.Add(evt);
            }
            finally
            {
                _lock.ExitReadLock();
            }

            foreach (var evt in events)
                yield return evt;
        }
    }

    public LogEntry ReadRow(int row)
    {
        _lock.EnterReadLock();
        try { return ReadEntry(row, new FastEventReader()); }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>The line as stored in the file (UTF-8 decoded), for diagnostics and tests.</summary>
    internal string ReadRawLine(int row)
    {
        _lock.EnterReadLock();
        try
        {
            var bytes = new byte[_lengths[row]];
            ReadExactly(bytes, _offsets[row]);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        _handle.Dispose();
        _lock.Dispose();
    }

    // ---------------------------------------------------------------- indexing

    private void Reset()
    {
        _offsets = Array.Empty<long>();
        _lengths = Array.Empty<int>();
        _ticks = Array.Empty<long>();
        _levels = Array.Empty<byte>();
        _templates = Array.Empty<int>();
        _traces = Array.Empty<int>();
        _count = 0;
        _templateTable = new Utf8Interner();
        _correlation = new int[]?[_correlationNames.Length];
        _correlationTables = NewTables();
        _correlationLines = new long[_correlationNames.Length];
        _traceTable = new Utf8Interner();
        _templateObjects = Array.Empty<MessageTemplate?>();
        _chronological = true;
        _order = null;
        _orderCount = 0;
        _indexedBytes = 0;
        _malformed = 0;
        _headLength = _tailLength = 0;
        Generation++;
    }

    private ulong HashBytes(long offset, int length)
    {
        var buffer = new byte[length];
        ReadExactly(buffer, offset);
        var hash = 14695981039346656037UL;
        foreach (var b in buffer)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    private void RememberFingerprints()
    {
        if (_headLength == 0)
        {
            _headLength = (int)Math.Min(FingerprintBytes, _indexedBytes);
            _headHash = HashBytes(0, _headLength);
        }

        _tailLength = (int)Math.Min(FingerprintBytes, _indexedBytes);
        _tailHash = HashBytes(_indexedBytes - _tailLength, _tailLength);
    }

    /// <summary>A log only ever grows at the end, so the bytes already indexed must be unchanged. A larger file that starts differently is a different file.</summary>
    private bool FingerprintsMatch()
    {
        if (_headLength == 0 || _tailLength == 0)
            return true;

        try
        {
            return HashBytes(0, _headLength) == _headHash && HashBytes(_indexedBytes - _tailLength, _tailLength) == _tailHash;
        }
        catch (EndOfStreamException)
        {
            return false; // the file shrank while we were looking at it
        }
    }

    private ChunkResult[] ScanChunks(long start, long end, IProgress<double>? progress, CancellationToken ct)
    {
        var total = end - start;
        var chunkCount = (int)Math.Clamp(total / TargetChunkBytes, 1, MaxChunks);

        var bounds = new long[chunkCount + 1];
        bounds[0] = start;
        bounds[chunkCount] = end;
        for (var i = 1; i < chunkCount; i++)
        {
            var target = Math.Max(bounds[i - 1], start + total * i / chunkCount);
            bounds[i] = FindNextLineStart(target, end);
        }

        var results = new ChunkResult[chunkCount];
        long bytesDone = 0;
        Action<int>? onRead = progress is null
            ? null
            : read => progress.Report((double)Interlocked.Add(ref bytesDone, read) / total);

        Parallel.For(0, chunkCount, new ParallelOptions { CancellationToken = ct }, i =>
        {
            var chunk = new ChunkResult(_correlationNames.Length);
            LineScanner.ScanRange(_handle, bounds[i], bounds[i + 1], chunk, _correlationNamesUtf8, ct, onRead);
            results[i] = chunk;
        });

        return results;
    }

    private void Append(ChunkResult[] chunks)
    {
        var added = chunks.Sum(c => c.Count);
        var firstNewRow = _count;
        EnsureCapacity(_count + added);

        foreach (var chunk in chunks)
        {
            _malformed += chunk.Malformed;
            if (chunk.Count == 0)
                continue;

            var templateMap = new int[chunk.TemplateTable.Count];
            for (var i = 0; i < templateMap.Length; i++)
                templateMap[i] = _templateTable.Intern(chunk.TemplateTable[i]);

            var traceMap = new int[chunk.TraceTable.Count];
            for (var i = 0; i < traceMap.Length; i++)
                traceMap[i] = _traceTable.Intern(chunk.TraceTable[i]);

            if (!chunk.Chronological || (_count > 0 && chunk.Ticks[0] < _ticks[_count - 1]))
                _chronological = false;

            CollectionsMarshal.AsSpan(chunk.Offsets).CopyTo(_offsets.AsSpan(_count));
            CollectionsMarshal.AsSpan(chunk.Lengths).CopyTo(_lengths.AsSpan(_count));
            CollectionsMarshal.AsSpan(chunk.Ticks).CopyTo(_ticks.AsSpan(_count));
            CollectionsMarshal.AsSpan(chunk.Levels).CopyTo(_levels.AsSpan(_count));

            for (var i = 0; i < chunk.Count; i++)
            {
                _templates[_count + i] = templateMap[chunk.Templates[i]];
                var trace = chunk.Traces[i];
                _traces[_count + i] = trace < 0 ? -1 : traceMap[trace];
            }

            for (var c = 0; c < _correlationNames.Length; c++)
            {
                var values = chunk.Correlations[c];
                var map = new int[chunk.CorrelationTables[c].Count];
                for (var i = 0; i < map.Length; i++)
                    map[i] = _correlationTables[c].Intern(chunk.CorrelationTables[c][i]);

                var column = _correlation[c];
                if (column is null)
                {
                    if (map.Length == 0)
                        continue; // nothing in this chunk either; the column stays unallocated and every row reads as "none"

                    column = _correlation[c] = new int[_offsets.Length];
                    Array.Fill(column, -1, 0, _count);
                }

                for (var i = 0; i < values.Count; i++)
                {
                    var value = values[i];
                    column[_count + i] = value < 0 ? -1 : map[value];
                    if (value >= 0)
                        _correlationLines[c]++;
                }
            }

            _count += chunk.Count;
        }

        if (_templateObjects.Length < _templateTable.Count)
            Array.Resize(ref _templateObjects, _templateTable.Count + 64);

        UpdateOrder(firstNewRow);
    }

    /// <summary>Row ids from <paramref name="from"/> up to <paramref name="to"/>, sorted by time with ties in file order.</summary>
    private int[] SortedRows(int from, int to)
    {
        var n = to - from;
        var keys = new long[n];
        var rows = new int[n];
        for (var i = 0; i < n; i++)
        {
            keys[i] = _ticks[from + i];
            rows[i] = from + i;
        }

        Array.Sort(keys, rows);

        // Array.Sort is not stable; put each run of equal timestamps back into file order
        for (var i = 0; i < n;)
        {
            var j = i + 1;
            while (j < n && keys[j] == keys[i])
                j++;

            if (j - i > 1)
                Array.Sort(rows, i, j - i);

            i = j;
        }

        return rows;
    }

    /// <summary>
    /// Keeps the time order up to date when rows are added. Logs written by several threads are slightly out of order all the time,
    /// so this must not re-sort everything on every refresh: only the new rows are sorted and merged in.
    /// </summary>
    private void UpdateOrder(int firstNewRow)
    {
        if (_chronological)
        {
            _order = null;
            _orderCount = 0;
            return;
        }

        if (_order is null)
        {
            var sorted = SortedRows(0, _count);
            _order = new int[_count + (_count >> 3) + 1024]; // headroom, so the first live append does not copy it
            sorted.CopyTo(_order, 0);
            _orderCount = _count;
            return;
        }

        if (_count == firstNewRow)
            return;

        var added = SortedRows(firstNewRow, _count);
        var capacity = _count + (_count >> 3) + 1024;

        if (_ticks[added[0]] >= _ticks[_order[_orderCount - 1]])
        {
            // Every new row sorts after the existing ones, the usual case: append in place. Readers holding an older
            // set only look below their own count, so they are unaffected.
            if (_order.Length < _count)
                Array.Resize(ref _order, capacity);

            added.CopyTo(_order, _orderCount);
        }
        else
        {
            // Some new rows belong inside the existing order. Build a new array (older sets keep the old one) and merge;
            // rows with equal times stay in file order because existing rows have the lower row ids.
            var merged = new int[capacity];
            int i = 0, j = 0, k = 0;
            while (i < _orderCount && j < added.Length)
                merged[k++] = _ticks[added[j]] < _ticks[_order[i]] ? added[j++] : _order[i++];

            while (i < _orderCount)
                merged[k++] = _order[i++];

            while (j < added.Length)
                merged[k++] = added[j++];

            _order = merged;
        }

        _orderCount = _count;
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _offsets.Length)
            return;

        // headroom, so the first append after open (live tail) does not have to copy every column
        var capacity = Math.Max(needed + (needed >> 2) + 1024, _offsets.Length * 2);
        Array.Resize(ref _offsets, capacity);
        Array.Resize(ref _lengths, capacity);
        Array.Resize(ref _ticks, capacity);
        Array.Resize(ref _levels, capacity);
        Array.Resize(ref _templates, capacity);
        Array.Resize(ref _traces, capacity);
        for (var c = 0; c < _correlation.Length; c++)
            if (_correlation[c] is not null)
                Array.Resize(ref _correlation[c], capacity);
    }

    private long FindNextLineStart(long position, long end)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (position < end)
            {
                var read = RandomAccess.Read(_handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, end - position)), position);
                if (read == 0)
                    break;

                var nl = buffer.AsSpan(0, read).IndexOf((byte)'\n');
                if (nl >= 0)
                    return position + nl + 1;

                position += read;
            }

            return end;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Everything up to the last newline is safe to index. A final line with no newline is only included
    /// when it is already complete JSON, so a half-written line is never indexed.
    /// </summary>
    private long ComputeIndexableEnd(long from, long length)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            long lastNewline = -1;
            var position = length;
            while (position > from)
            {
                var size = (int)Math.Min(buffer.Length, position - from);
                position -= size;
                ReadExactly(buffer.AsSpan(0, size), position);

                var nl = buffer.AsSpan(0, size).LastIndexOf((byte)'\n');
                if (nl >= 0)
                {
                    lastNewline = position + nl;
                    break;
                }
            }

            var tailStart = lastNewline < 0 ? from : lastNewline + 1;
            if (tailStart >= length)
                return length;

            var tailLength = length - tailStart;
            if (tailLength <= 4 * 1024 * 1024)
            {
                var tail = new byte[tailLength];
                ReadExactly(tail, tailStart);
                if (IsCompleteJsonObject(tail))
                    return length;
            }

            return tailStart;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsCompleteJsonObject(ReadOnlySpan<byte> bytes)
    {
        var trimmed = bytes;
        if (trimmed.StartsWith("﻿"u8))
            trimmed = trimmed[3..];

        var first = trimmed.IndexOfAnyExcept(Whitespace);
        if (first < 0)
            return true; // only whitespace

        try
        {
            var reader = new Utf8JsonReader(trimmed[first..]);
            while (reader.Read()) { }
            return reader.TokenType == JsonTokenType.EndObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private void ReadExactly(Span<byte> destination, long offset)
    {
        while (!destination.IsEmpty)
        {
            var read = RandomAccess.Read(_handle, destination, offset);
            if (read == 0)
                throw new EndOfStreamException($"The file ended unexpectedly while reading {FilePath}.");

            destination = destination[read..];
            offset += read;
        }
    }

    // ---------------------------------------------------------------- querying

    private int RowAtPosition(int position) => _order is null ? position : _order[position];

    private RowSet AllRows() => _order is null ? new RowSet(_count) : new RowSet(_order, _orderCount);

    /// <summary>First position, in time order, whose timestamp is at or after <paramref name="ticks"/>.</summary>
    private int LowerBound(long ticks)
    {
        int lo = 0, hi = _count;
        while (lo < hi)
        {
            var mid = (int)(((long)lo + hi) / 2);
            if (_ticks[RowAtPosition(mid)] < ticks)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    private RowSet FilterByIndex(LogQuery q, CancellationToken ct)
    {
        var mask = (int)q.Levels & (int)LogLevelMask.All;
        var allLevels = mask == (int)LogLevelMask.All;
        var timeFiltered = q.From is not null || q.To is not null;

        if (allLevels && !timeFiltered && q.TemplateId is null && q.Correlation is null && q.TraceId is null)
            return AllRows();

        var from = q.From?.UtcTicks ?? long.MinValue;
        var to = q.To?.UtcTicks ?? long.MaxValue;
        var template = q.TemplateId ?? -1;
        var matchTemplate = q.TemplateId is not null;
        var matchCorrelation = q.Correlation is not null;
        var correlationColumn = matchCorrelation ? Array.IndexOf(_correlationNames, q.Correlation!.Value.Property) : -1;
        var correlationId = q.Correlation?.ValueId ?? -1;
        int[]? correlation = correlationColumn >= 0 ? _correlation[correlationColumn] : null;
        if (matchCorrelation && correlation is null)
            return new RowSet(Array.Empty<int>()); // the property is not indexed, or no row has a value for it: nothing can match
        var matchTrace = q.TraceId is not null;
        var trace = q.TraceId ?? -1;

        int lo = 0, hi = _count;
        if (timeFiltered)
        {
            lo = LowerBound(from);
            hi = to == long.MaxValue ? _count : LowerBound(to);
        }

        var rows = new List<int>();
        for (var p = lo; p < hi; p++)
        {
            if ((p & 0xFFFF) == 0)
                ct.ThrowIfCancellationRequested();

            var row = RowAtPosition(p);
            if (((mask >> (_levels[row] & 7)) & 1) == 0)
                continue;

            if (matchTemplate && _templates[row] != template)
                continue;

            if (matchCorrelation && correlation![row] != correlationId)
                continue;

            if (matchTrace && _traces[row] != trace)
                continue;

            rows.Add(row);
        }

        return new RowSet(rows.ToArray());
    }

    /// <summary>Parses the candidate lines in parallel (a few MB at a time, in file order) and keeps the ones the predicate accepts.</summary>
    private RowSet Refine(RowSet candidates, CompiledFilter filter, CancellationToken ct)
    {
        var n = candidates.Count;
        if (n == 0)
            return candidates; // the index filters already ruled every row out; there is nothing to read (and no partition to make)

        var keep = new bool[n];

        Parallel.ForEach(
            Partitioner.Create(0, n, 2048),
            new ParallelOptions { CancellationToken = ct },
            range => EvaluateRange(candidates, range.Item1, range.Item2, keep, filter));

        var kept = 0;
        foreach (var k in keep)
            if (k) kept++;

        var rows = new int[kept];
        var next = 0;
        for (var i = 0; i < n; i++)
            if (keep[i])
                rows[next++] = candidates.RowAt(i);

        return new RowSet(rows);
    }

    private void EvaluateRange(RowSet rows, int from, int to, bool[] keep, CompiledFilter filter)
    {
        var fast = new FastEventReader();
        fast.KeepOnly(filter.Properties);
        byte[]? buffer = null;
        try
        {
            var i = from;
            while (i < to)
            {
                var firstRow = rows.RowAt(i);
                var start = _offsets[firstRow];
                var end = start + _lengths[firstRow];

                // Group neighbouring rows into one read when they sit close together in the file
                var j = i + 1;
                while (j < to)
                {
                    var row = rows.RowAt(j);
                    var rowStart = _offsets[row];
                    var rowEnd = rowStart + _lengths[row];
                    if (rowStart < start || rowEnd - start > BatchBytes)
                        break;

                    end = Math.Max(end, rowEnd);
                    j++;
                }

                var size = (int)(end - start);
                if (buffer is null || buffer.Length < size)
                {
                    if (buffer is not null)
                        ArrayPool<byte>.Shared.Return(buffer);

                    buffer = ArrayPool<byte>.Shared.Rent(size);
                }

                ReadExactly(buffer.AsSpan(0, size), start);

                for (var k = i; k < j; k++)
                {
                    var row = rows.RowAt(k);
                    var line = buffer.AsSpan((int)(_offsets[row] - start), _lengths[row]);
                    keep[k] = TryParse(line, TemplateFor(_templates[row]), fast, out var evt) && filter.Matches(evt);
                }

                i = j;
            }
        }
        finally
        {
            if (buffer is not null)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Fast path first; any line it declines goes to Serilog's own reader, which decides what is readable.</summary>
    private static bool TryParse(ReadOnlySpan<byte> line, MessageTemplate template, FastEventReader fast, out LogEvent evt)
    {
        if (fast.TryRead(line, template, out evt))
            return true;

        try
        {
            evt = LogEventReader.ReadFromString(Encoding.UTF8.GetString(line));
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Serilog's reader can also throw things like OverflowException for an out-of-range @i, not just the two documented types
            evt = null!;
            return false;
        }
    }

    private MessageTemplate TemplateFor(int id)
    {
        var template = _templateObjects[id];
        if (template is null)
        {
            template = TemplateParser.Parse(TemplateText(id));
            _templateObjects[id] = template; // a racing thread may parse the same template; the results are equivalent
        }

        return template;
    }

    private LogEntry ReadEntry(int row, FastEventReader fast)
    {
        var bytes = new byte[_lengths[row]];
        ReadExactly(bytes, _offsets[row]);

        if (!TryParse(bytes, TemplateFor(_templates[row]), fast, out var evt))
            throw new InvalidDataException($"Row {row} could not be read back from {FilePath}.");

        return new LogEntry(
            row,
            evt.Timestamp,
            evt.Level,
            evt.MessageTemplate.Text,
            evt.RenderMessage(),
            evt.Exception?.ToString(),
            evt.Properties,
            evt.TraceId?.ToHexString(),
            evt.SpanId?.ToHexString());
    }

    private string TemplateText(int id) => Encoding.UTF8.GetString(_templateTable[id]);
}
