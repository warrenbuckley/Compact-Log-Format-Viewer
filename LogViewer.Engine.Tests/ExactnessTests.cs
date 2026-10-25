using LogViewer.Server;
using LogViewer.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Moq;
using EngineSort = LogViewer.Engine.SortOrder;
using ServerSort = LogViewer.Server.Models.SortOrder;

namespace LogViewer.Engine.Tests;

/// <summary>
/// The real LogViewer.Server LogParser is the oracle: for every search the engine must return the same rows,
/// the same totals and the same template list that the application returns today.
/// </summary>
public class ExactnessTests
{
    private string _dir = null!;
    private readonly Dictionary<string, (LogParser Old, LogIndex New)> _files = new();

    [OneTimeSetUp]
    public void Open()
    {
        _dir = Path.Combine(Path.GetTempPath(), "clef-exact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        var mixed = Path.Combine(_dir, "mixed.json");
        TestData.WriteMixedLog(mixed, 6000);
        var traces = Path.Combine(_dir, "traces.json");
        TestData.WriteTraceLog(traces, 6000);

        foreach (var path in new[] { Path.Combine(TestContext.CurrentContext.TestDirectory, "UmbracoTraceLog.UNITTEST.20181112.json"), mixed, traces })
        {
            var old = new LogParser(new Mock<IHubContext<LogHub>>().Object);
            old.ReadLogs(path);
            _files[Path.GetFileName(path)] = (old, LogIndex.Open(path));
        }
    }

    [OneTimeTearDown]
    public void Close()
    {
        foreach (var (old, index) in _files.Values)
        {
            index.Dispose();
            old.Dispose();
        }

        Directory.Delete(_dir, true);
    }

    public static IEnumerable<string> Filters => new[]
    {
        // used in the server's own tests
        "", "Has(@Exception)", "IsDefined(@x)", "IsDefined(Duration) and Duration > 1000", "Has(@x)", "Has(Duration) and Duration > 1000",
        "Duration > 1000", "Not(@Level = 'Verbose') and Not(@Level = 'Debug')", "Not(@l = 'Verbose') and Not(@l = 'Debug')",
        "StartsWith(SourceContext, 'Umbraco.Core')", "@MessageTemplate = '{EndMessage} ({Duration}ms) [Timing {TimingId}]'",
        "@mt = '{EndMessage} ({Duration}ms) [Timing {TimingId}]'", "SortedComponentTypes[?] = 'Umbraco.Web.Search.ExamineComponent'",
        "Contains(SortedComponentTypes[?], 'DatabaseServer')", "@Message like '%localhost%'", "@m like '%localhost%'", "runtime",

        // plain words, including ones that cross the template / property boundary
        "RUNTIME", "Timing", "Hello Person3", "Hello", "from Town2", "Bob 5", "Failure for", "Brace", "label", "nope", "zzzznotfound",

        // characters that mean something to like, to regular expressions or to the expression parser
        "50%", "a_b", "%", "_", "{", "}", "[Timing", "'", "\"", "\\", ".", "*", "(", ")", "((", "-", "a-b", "x=y", "<", ">", "+",
        " ", "   ", "a b", "é", "Ünïcode", "İ", "ß",

        // expression shapes
        "@m like 'Hello%'", "@m like '%P_rson3%' ci", "@m like '%person3%'", "@m like '%Person3%'", "@mt like 'Hello%'", "Contains(@m, 'town')",
        "StartsWith(@m, 'Hello')", "IsMatch(@m, 'T.wn[0-9]')", "@l = 'Error' or @l = 'Warning'", "@l = 'Error' and Has(@x)",
        "Has(Duration) and Not(Has(Info))", "Duration > 100 or Name = 'Person1'", "Info.Label = 'label3'", "Info.Id > 3000",
        "Tags[?] = 'b2'", "Tags[*] like 'b%'", "@p['Duration'] > 2000", "@p[?] = 2997", "Properties['Name'] = 'Person3'", "Has(@p['Other'])",
        "Coalesce(Duration, 0) > 1000", "Round(Duration / 7, 0) = 100", "Duration > 1000 and", "@@@", "Has(", "not an expression at all",
        "Duration >", "Name = ", "@l = 'Nope'", "@t > 5", "1 = 1", "true", "false", "@x like '%nope 4%'", "Contains(@x, 'nope')",
    };

    /// <summary>Filters that look at trace and span ids, run on a log where some requests have them, some have half, and some have none.</summary>
    public static IEnumerable<string> TraceFilters => new[]
    {
        "Has(@tr)", "Not(Has(@tr))", "Has(@sp)", "IsDefined(@tr)", "@tr is null", "@sp is null", "@tr is not null", "@sp is not null",
        $"@tr = '{TestData.TraceOf(1)}'", $"@tr = '{TestData.TraceOf(2)}'", $"@tr = '{TestData.TraceOf(9)}'", $"@sp = '{TestData.SpanOf(11)}'",
        $"@sp = '{TestData.SpanOf(33)}'", "StartsWith(@tr, '0000000000000000000000000000000')", "@tr like '%1'", "Coalesce(@tr, 'none') = 'none'",
        "Has(@tr) and Has(@sp)", "Has(@tr) and Not(Has(@sp))", "Has(@sp) and Not(Has(@tr))", $"@tr = '{TestData.TraceOf(1)}' and @l = 'Warning'",
        "@tr", "Length(@tr) = 32", "TypeOf(@tr) = 'ActivityTraceId'", "@tr = @tr", "Duration > 60 and Has(@tr)", "@m like '%page%' and Has(@sp)",
    };

    public static IEnumerable<TestCaseData> Cases =>
        new[] { "UmbracoTraceLog.UNITTEST.20181112.json", "mixed.json", "traces.json" }
            .SelectMany(file => (file == "traces.json" ? Filters.Concat(TraceFilters) : Filters)
                .Select(filter => new TestCaseData(file, filter).SetName($"{file} :: {(filter.Length == 0 ? "<empty>" : filter)}")));

    [TestCaseSource(nameof(Cases))]
    public void Search_Returns_What_The_Server_Returns_Today(string file, string filter)
    {
        var (old, index) = _files[file];

        var expected = old.Search(1, int.MaxValue / 2, filter, ServerSort.Ascending);
        var rows = index.Query(new LogQuery { Filter = filter });

        Assert.AreEqual(expected.Logs.TotalItems, rows.Count, "number of matching rows");

        var expectedRows = expected.Logs.Items!.Select(m => (m.Timestamp.UtcTicks, m.Level, m.RenderedMessage, m.MessageTemplateText, m.Exception)).ToList();
        var actualRows = index.ReadPage(rows, 1, int.MaxValue / 2, EngineSort.Ascending).Select(e => (e.Timestamp.UtcTicks, e.Level, e.RenderedMessage, e.MessageTemplate, e.Exception)).ToList();
        CollectionAssert.AreEqual(expectedRows, actualRows, "matching rows, in order");

        var expectedTemplates = expected.MessageTemplates.Select(t => (t.MessageTemplate, (long)t.Count)).ToList();
        var actualTemplates = index.GetPatterns(rows).Select(t => (t.Template, t.Count)).ToList();
        CollectionAssert.AreEqual(expectedTemplates, actualTemplates, "template list");
    }

    [TestCase("UmbracoTraceLog.UNITTEST.20181112.json")]
    [TestCase("mixed.json")]
    [TestCase("traces.json")]
    public void Totals_Match_The_Server(string file)
    {
        var (old, index) = _files[file];
        var all = index.All();

        var expected = old.TotalCounts();
        var actual = index.CountLevels(all);
        Assert.AreEqual((expected.Verbose, expected.Debug, expected.Information, expected.Warning, expected.Error, expected.Fatal),
            ((int)actual.Verbose, (int)actual.Debug, (int)actual.Information, (int)actual.Warning, (int)actual.Error, (int)actual.Fatal));
        Assert.AreEqual(old.TotalErrors(), index.CountErrors(all));
    }

    [TestCase("UmbracoTraceLog.UNITTEST.20181112.json")]
    [TestCase("mixed.json")]
    [TestCase("traces.json")]
    public void Newest_First_Pages_Match_The_Server(string file)
    {
        var (old, index) = _files[file];

        var expected = old.Search(1, 100, null, ServerSort.Descending).Logs.Items!.Select(m => (m.Timestamp.UtcTicks, m.RenderedMessage)).ToList();
        var actual = index.ReadPage(index.All(), 1, 100, EngineSort.Descending).Select(e => (e.Timestamp.UtcTicks, e.RenderedMessage)).ToList();

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestCase(false, 7)]
    [TestCase(true, 7)]
    [TestCase(false, 50)]
    [TestCase(true, 1)]
    public void Every_Page_Matches_When_Many_Rows_Share_A_Timestamp(bool outOfOrder, int pageSize)
    {
        // Runs of identical timestamps (1 to 40 rows) so page boundaries land inside runs; optionally with rows written out of time order
        var rng = new Random(11);
        var path = Path.Combine(_dir, $"ties-{outOfOrder}-{pageSize}.json");
        var t0 = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var lines = new List<string>();
        var second = 0;
        while (lines.Count < 700)
        {
            var run = rng.Next(1, 41);
            for (var i = 0; i < run; i++)
            {
                var level = rng.Next(4) == 0 ? "Warning" : "Debug";
                lines.Add($"{{\"@t\":\"{t0.AddSeconds(second):O}\",\"@mt\":\"row {{N}} of {{G}}\",\"@l\":\"{level}\",\"N\":{lines.Count},\"G\":{second}}}");
            }

            second++;
        }

        if (outOfOrder)
            for (var i = 0; i < 40; i++)
            {
                var a = rng.Next(lines.Count);
                var b = Math.Clamp(a + rng.Next(-60, 60), 0, lines.Count - 1);
                (lines[a], lines[b]) = (lines[b], lines[a]);
            }

        File.WriteAllLines(path, lines);
        var old = new LogParser(new Mock<IHubContext<LogHub>>().Object);
        old.ReadLogs(path);
        using var index = LogIndex.Open(path);

        foreach (var filter in new string?[] { null, "Warning", "@l = 'Warning'" })
        {
            var rows = filter is null ? index.All() : index.Query(new LogQuery { Filter = filter });
            var pages = (rows.Count + pageSize - 1) / pageSize;
            for (var page = 1; page <= pages; page++)
            {
                foreach (var (serverSort, engineSort) in new[] { (ServerSort.Descending, EngineSort.Descending), (ServerSort.Ascending, EngineSort.Ascending) })
                {
                    var expected = old.Search(page, pageSize, filter, serverSort).Logs.Items!.Select(m => (m.Timestamp.UtcTicks, m.RenderedMessage)).ToList();
                    var actual = index.ReadPage(rows, page, pageSize, engineSort).Select(e => (e.Timestamp.UtcTicks, e.RenderedMessage)).ToList();
                    CollectionAssert.AreEqual(expected, actual, $"filter {filter ?? "<none>"}, {serverSort}, page {page}");
                }
            }
        }

        old.Dispose();
    }
}
