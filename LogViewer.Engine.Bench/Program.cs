using System.Diagnostics;
using LogViewer.Engine;
using LogViewer.Engine.Bench;

// gen <path> <megabytes>        write a synthetic CLEF file
// engine <path>                 time the new index
// baseline <path>               time the current in-memory parser
// Run engine and baseline as separate processes so their memory numbers do not mix.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: gen <path> <MB> | engine <path> | baseline <path>");
    return 1;
}

var mode = args[0];
var path = args[1];

switch (mode)
{
    case "gen":
        var sw = Stopwatch.StartNew();
        Generator.Write(path, long.Parse(args[2]) * 1024 * 1024);
        Console.WriteLine($"wrote {new FileInfo(path).Length / 1024 / 1024} MB in {sw.Elapsed.TotalSeconds:0.0}s");
        return 0;

    case "engine":
        RunEngine(path);
        return 0;

    case "baseline":
        RunBaseline(path);
        return 0;

    default:
        Console.Error.WriteLine($"unknown mode {mode}");
        return 1;
}

static double Time(Action action)
{
    var sw = Stopwatch.StartNew();
    action();
    return sw.Elapsed.TotalMilliseconds;
}

/// <summary>Median of three runs, after one untimed warm-up so JIT cost is not counted against the first query.</summary>
static double Median(Action action)
{
    action();
    var runs = new[] { Time(action), Time(action), Time(action) };
    Array.Sort(runs);
    return runs[1];
}

static void Row(string op, double ms, string detail = "") => Console.WriteLine($"| {op,-46} | {ms,10:N1} | {detail}");

static void Memory(string label)
{
    var managed = GC.GetTotalMemory(true) / 1024.0 / 1024.0;
    var peak = Process.GetCurrentProcess().PeakWorkingSet64 / 1024.0 / 1024.0;
    Console.WriteLine($"| {label,-46} | managed {managed,8:N0} MB | peak working set {peak:N0} MB");
}

static void Header(string title, string path)
{
    Console.WriteLine($"## {title} - {Path.GetFileName(path)} ({new FileInfo(path).Length / 1024 / 1024} MB, {Environment.ProcessorCount} cores)");
    Console.WriteLine($"| {"operation",-46} | {"ms",10} | result");
    Console.WriteLine($"|{new string('-', 48)}|{new string('-', 12)}|-------");
}

static void RunBaseline(string path)
{
    Header("Current parser (List<LogEvent>)", path);
    var parser = new Baseline();
    var rows = 0;
    Row("open (parse every line into memory)", Time(() => rows = parser.Open(path)), $"{rows:N0} rows");
    Memory("after open");

    (long, long) totals = default;
    Row("level counts + error count", Median(() => totals = parser.Totals()), $"errors {totals.Item1:N0}");

    (int total, int pageRows, int templates) r = default;
    Row("page 1, newest first, no filter", Median(() => r = parser.Search(1, 100, null)), $"{r.total:N0} rows, {r.templates} templates");
    Row("page 1000, newest first, no filter", Median(() => r = parser.Search(1000, 100, null)), $"{r.pageRows} rows");
    Row("filter: @l = 'Error'", Median(() => r = parser.Search(1, 100, "@l = 'Error'")), $"{r.total:N0} rows");
    Row("filter: Duration > 1000", Median(() => r = parser.Search(1, 100, "Duration > 1000")), $"{r.total:N0} rows");
    Row("filter: plain word 'timeout'", Median(() => r = parser.Search(1, 100, "timeout")), $"{r.total:N0} rows");
    Memory("end of run");
}

static void RunEngine(string path)
{
    Header("LogIndex", path);
    LogIndex index = null!;
    Row("open (index every line)", Time(() => index = LogIndex.Open(path)), $"{index.Count:N0} rows");
    Memory("after open");
    var stats = index.GetStats();
    Console.WriteLine($"|   index columns ~{stats.ApproxIndexMemoryBytes / 1024.0 / 1024.0:N0} MB, {stats.TemplateCount} templates, {stats.RequestCount:N0} request ids, chronological={stats.Chronological}");

    var all = index.All();
    LevelCounts counts = default;
    long errors = 0;
    Row("level counts + error count", Median(() => { counts = index.CountLevels(all); errors = index.CountErrors(all); }), $"errors {errors:N0}");

    int templates = 0;
    Row("page 1, newest first, no filter", Median(() =>
    {
        index.ReadPage(index.All(), 1, 100, SortOrder.Descending);
        templates = index.GetPatterns(index.All()).Count;
    }), $"{all.Count:N0} rows, {templates} templates (page + patterns)");
    Row("page 1000, newest first, no filter", Median(() => index.ReadPage(index.All(), 1000, 100, SortOrder.Descending)));

    RowSet rows = all;
    Row("filter: level = Error (index only)", Median(() => rows = index.Query(new LogQuery { Levels = LogLevelMask.Error })), $"{rows.Count:N0} rows");
    Row("filter: @l = 'Error' (parses every line)", Median(() => rows = index.Query(new LogQuery { Filter = "@l = 'Error'" })), $"{rows.Count:N0} rows");
    Row("filter: Duration > 1000", Median(() => rows = index.Query(new LogQuery { Filter = "Duration > 1000" })), $"{rows.Count:N0} rows");
    Row("filter: plain word 'timeout'", Median(() => rows = index.Query(new LogQuery { Filter = "timeout" })), $"{rows.Count:N0} rows");
    Row("filter: Error level + word 'timeout'", Median(() => rows = index.Query(new LogQuery { Levels = LogLevelMask.Error, Filter = "timeout" })), $"{rows.Count:N0} rows");

    var middle = index.GetRowInfo(index.Count / 2).Timestamp;
    Row("time range: 5 minutes", Median(() => rows = index.Query(new LogQuery { From = middle, To = middle.AddMinutes(5) })), $"{rows.Count:N0} rows");
    Histogram histogram = null!;
    Row("histogram, 100 buckets, whole file", Median(() => histogram = index.GetHistogram(all, 100)), $"{histogram.Buckets.Count} buckets");

    var sample = index.ReadRow(index.Count / 2);
    var requestId = index.FindRequestId(sample.Properties["HttpRequestId"].ToString().Trim('"'));
    Row("same request", Median(() => rows = index.Query(new LogQuery { RequestId = requestId })), $"{rows.Count:N0} rows");

    // Tail: append 2,000 lines to a copy and pick them up
    var tailPath = path + ".tail";
    File.Copy(path, tailPath, true);
    using (var tail = LogIndex.Open(tailPath))
    {
        var last = tail.GetRowInfo(tail.Count - 1).Timestamp;
        var lines = Enumerable.Range(1, 2000).Select(i => $"{{\"@t\":\"{last.AddMilliseconds(i):yyyy-MM-ddTHH:mm:ss.fffffff}Z\",\"@mt\":\"appended {{N}}\",\"N\":{i}}}");
        File.AppendAllLines(tailPath, lines);
        var before = tail.Count;
        Row("live tail: pick up 2,000 appended lines", Time(() => tail.Refresh()), $"+{tail.Count - before:N0} rows");
    }

    File.Delete(tailPath);
    Memory("end of run");
}
