using System.Text;
using LogViewer.Engine.Tests;
using Serilog.Formatting.Compact.Reader;

namespace LogViewer.Engine.Tests;

/// <summary>
/// Logs written inside a request carry trace and span ids (@tr, @sp); logs from an older logger, or from background work, do not,
/// and one file can hold both. Everything has to work for any mix, and the two ways of asking for a trace (the index and a typed
/// expression) have to agree.
/// </summary>
public class TraceTests
{
    private const string T = "\"@t\":\"2024-01-01T00:00:00Z\"";
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "clef-trace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ---------------------------------------------------------------- the reader

    public static IEnumerable<TestCaseData> ValidLines()
    {
        TestCaseData Case(string name, string line) => new TestCaseData(line).SetName(name);

        yield return Case("trace and span", "{" + T + ",\"@mt\":\"x\",\"@tr\":\"0123456789abcdef0123456789abcdef\",\"@sp\":\"0123456789abcdef\"}");
        yield return Case("trace only", "{" + T + ",\"@mt\":\"x\",\"@tr\":\"0123456789abcdef0123456789abcdef\"}");
        yield return Case("span only", "{" + T + ",\"@mt\":\"x\",\"@sp\":\"0123456789abcdef\"}");
        yield return Case("neither", "{" + T + ",\"@mt\":\"x\"}");
        yield return Case("both null", "{" + T + ",\"@mt\":\"x\",\"@tr\":null,\"@sp\":null}");
        yield return Case("after the properties", "{" + T + ",\"@mt\":\"x {A}\",\"A\":1,\"@tr\":\"0123456789abcdef0123456789abcdef\",\"@sp\":\"0123456789abcdef\"}");
        yield return Case("escaped digits", "{" + T + ",\"@mt\":\"x\",\"@tr\":\"\\u0030123456789abcdef0123456789abcdef\",\"@sp\":\"\\u0030123456789abcdef\"}");
        yield return Case("with exception and level", "{" + T + ",\"@mt\":\"x\",\"@l\":\"Error\",\"@x\":\"boom\",\"@tr\":\"ffffffffffffffffffffffffffffffff\",\"@sp\":\"ffffffffffffffff\"}");
    }

    [TestCaseSource(nameof(ValidLines))]
    public void Trace_And_Span_Ids_Are_Read_On_The_Fast_Path_And_Match_Serilog(string line)
    {
        var reference = FastReaderTests.Reference(line);
        Assert.IsNotNull(reference, "Serilog reads this line");

        Assert.IsTrue(FastReaderTests.FastPath(line, out var fast), "the fast reader handles it itself");
        FastReaderTests.AssertSame(reference!, fast, "trace line");
    }

    public static IEnumerable<TestCaseData> InvalidLines()
    {
        TestCaseData Case(string name, string line) => new TestCaseData(line).SetName(name);

        yield return Case("uppercase hex", "{" + T + ",\"@mt\":\"x\",\"@tr\":\"0123456789ABCDEF0123456789ABCDEF\"}");
        yield return Case("short trace", "{" + T + ",\"@mt\":\"x\",\"@tr\":\"0123\"}");
        yield return Case("non hex", "{" + T + ",\"@mt\":\"x\",\"@sp\":\"0123456789abcdeg\"}");
        yield return Case("long span", "{" + T + ",\"@mt\":\"x\",\"@sp\":\"0123456789abcdef00\"}");
        yield return Case("number", "{" + T + ",\"@mt\":\"x\",\"@tr\":12345}");
        yield return Case("object", "{" + T + ",\"@mt\":\"x\",\"@sp\":{\"a\":1}}");
        yield return Case("empty", "{" + T + ",\"@mt\":\"x\",\"@tr\":\"\"}");
        yield return Case("all zeros trace", "{" + T + ",\"@mt\":\"x\",\"@tr\":\"00000000000000000000000000000000\"}");
        yield return Case("all zeros span", "{" + T + ",\"@mt\":\"x\",\"@sp\":\"0000000000000000\"}");
        yield return Case("mixed case", "{" + T + ",\"@mt\":\"x\",\"@tr\":\"0123456789abcdef0123456789abcdeF\"}");
        yield return Case("trailing space", "{" + T + ",\"@mt\":\"x\",\"@sp\":\"0123456789abcde \"}");
    }

    [TestCaseSource(nameof(InvalidLines))]
    public void Ids_That_Serilog_Rejects_Are_Rejected_Or_Handled_Identically(string line)
    {
        var reference = FastReaderTests.Reference(line);
        if (FastReaderTests.FastPath(line, out var fast))
        {
            Assert.IsNotNull(reference, "the fast reader accepted a line Serilog rejects");
            FastReaderTests.AssertSame(reference!, fast, "invalid id");
        }
    }

    // ---------------------------------------------------------------- the index

    [Test]
    public void Trace_Ids_Are_Indexed_For_Rows_That_Have_Them_And_Absent_For_The_Rest()
    {
        var path = Path.Combine(_dir, "traces.json");
        TestData.WriteTraceLog(path, 400);
        using var index = LogIndex.Open(path);

        // group 1 (rows 10-19): trace and span on every line
        var trace1 = index.FindTraceId(TestData.TraceOf(1));
        Assert.IsNotNull(trace1);
        var rows = index.Query(new LogQuery { TraceId = trace1 });
        CollectionAssert.AreEqual(Enumerable.Range(10, 10), Enumerable.Range(0, rows.Count).Select(rows.RowAt));
        Assert.AreEqual(TestData.TraceOf(1), index.GetTraceText(index.GetRowInfo(12).TraceId));

        // group 2 (rows 20-29): @tr on every line except the ones written as null
        var rows2 = index.Query(new LogQuery { TraceId = index.FindTraceId(TestData.TraceOf(2)) });
        CollectionAssert.AreEqual(new[] { 21, 22, 23, 24, 26, 27, 28, 29 }, Enumerable.Range(0, rows2.Count).Select(rows2.RowAt));
        Assert.AreEqual(-1, index.GetRowInfo(20).TraceId, "a null @tr is no trace");

        // groups 0 and 3: background work, and span-only lines. No trace id.
        foreach (var row in Enumerable.Range(0, 10).Concat(Enumerable.Range(30, 10)))
            Assert.AreEqual(-1, index.GetRowInfo(row).TraceId, $"row {row}");

        Assert.IsNull(index.GetTraceText(-1));
        Assert.AreEqual(20, index.GetStats().TraceCount);
        Assert.IsNull(index.FindTraceId(TestData.TraceOf(0)));
    }

    [Test]
    public void The_Two_Ways_Of_Asking_For_A_Trace_Return_The_Same_Rows()
    {
        var path = Path.Combine(_dir, "traces.json");
        TestData.WriteTraceLog(path, 1000);
        using var index = LogIndex.Open(path);

        foreach (var group in new[] { 1, 2, 5, 6, 37, 50, 98 })
        {
            var hex = TestData.TraceOf(group);
            var byIndex = index.Query(new LogQuery { TraceId = index.FindTraceId(hex) });
            var byExpression = index.Query(new LogQuery { Filter = $"@tr = '{hex}'" });

            Assert.Greater(byIndex.Count, 0, $"group {group} has trace rows");
            CollectionAssert.AreEqual(Enumerable.Range(0, byIndex.Count).Select(byIndex.RowAt), Enumerable.Range(0, byExpression.Count).Select(byExpression.RowAt), $"trace {hex}");
        }

        // and combined with the other index filters, in either order
        var trace = index.FindTraceId(TestData.TraceOf(5));
        var request = index.Query(new LogQuery { TraceId = trace, Levels = LogLevelMask.Warning | LogLevelMask.Debug });
        var viaFilter = index.Query(new LogQuery { Levels = LogLevelMask.Warning | LogLevelMask.Debug, Filter = $"@tr = '{TestData.TraceOf(5)}'" });
        Assert.AreEqual(request.Count, viaFilter.Count);
    }

    [Test]
    public void Entries_Carry_Their_Own_Trace_And_Span()
    {
        var path = Path.Combine(_dir, "traces.json");
        TestData.WriteTraceLog(path, 100);
        using var index = LogIndex.Open(path);

        void Check(int row, string? trace, string? span, string what)
        {
            var entry = index.ReadRow(row);
            Assert.AreEqual(trace, entry.TraceId, what + " trace");
            Assert.AreEqual(span, entry.SpanId, what + " span");
        }

        Check(0, null, null, "background row");
        Check(10, TestData.TraceOf(1), TestData.SpanOf(10), "full row");
        Check(21, TestData.TraceOf(2), null, "trace only");
        Check(33, null, TestData.SpanOf(33), "span only");

        var page = index.ReadPage(index.All(), 1, 100, SortOrder.Ascending);
        Assert.AreEqual(100, page.Count);
        Assert.AreEqual(TestData.TraceOf(1), page[15].TraceId);
    }

    [Test]
    public void A_File_With_No_Trace_Ids_Is_Unaffected()
    {
        var path = Path.Combine(_dir, "plain.json");
        TestData.WriteMixedLog(path, 600);
        using var index = LogIndex.Open(path);

        Assert.AreEqual(0, index.GetStats().TraceCount);
        Assert.IsNull(index.FindTraceId(TestData.TraceOf(1)));
        Assert.AreEqual(0, index.Query(new LogQuery { TraceId = 0 }).Count, "an id nothing carries matches nothing");
        Assert.AreEqual(0, index.Query(new LogQuery { Filter = "Has(@tr)" }).Count);
        Assert.AreEqual(600, index.Query(new LogQuery { Filter = "Not(Has(@tr))" }).Count);
        Assert.IsTrue(Enumerable.Range(0, 600).All(r => index.GetRowInfo(r).TraceId == -1));
        Assert.IsNull(index.ReadRow(3).TraceId);
    }

    [Test]
    public void Trace_Ids_Serilog_Rejects_Do_Not_Break_Searches()
    {
        var good = "{" + T + ",\"@mt\":\"good {N}\",\"N\":1,\"@tr\":\"0123456789abcdef0123456789abcdef\"}";
        var bad = "{" + T + ",\"@mt\":\"bad {N}\",\"N\":2,\"@tr\":\"NOT-A-TRACE-ID\"}";
        var path = Write("bad.json", good + "\n" + bad + "\n" + good + "\n");
        using var index = LogIndex.Open(path);

        Assert.AreEqual(3, index.Count);
        Assert.Throws<InvalidDataException>(() => index.ReadRow(1), "the unreadable row is reported, not read wrongly");
        Assert.AreEqual(2, index.Query(new LogQuery { Filter = "N > 0" }).Count, "searches skip it, as they skip any unreadable line");
        Assert.AreEqual(2, index.Query(new LogQuery { Filter = "good" }).Count);
    }

    [Test]
    public void Escaped_Trace_Ids_Are_The_Same_Trace_As_Plain_Ones()
    {
        var plain = "{" + T + ",\"@mt\":\"a\",\"@tr\":\"0123456789abcdef0123456789abcdef\"}";
        var escaped = "{" + T + ",\"@mt\":\"b\",\"@tr\":\"\\u0030123456789abcdef0123456789abcdef\"}";
        using var index = LogIndex.Open(Write("escaped.json", plain + "\n" + escaped + "\n"));

        Assert.AreEqual(1, index.GetStats().TraceCount);
        Assert.AreEqual(2, index.Query(new LogQuery { TraceId = index.FindTraceId("0123456789abcdef0123456789abcdef") }).Count);
    }

    // ---------------------------------------------------------------- a log that changes while it is open

    [Test]
    public void A_Live_File_Can_Start_Without_Trace_Ids_And_Gain_Them_Or_Lose_Them()
    {
        var path = Path.Combine(_dir, "live.json");
        string Line(int n, string? trace) => "{" + T + $",\"@mt\":\"line {{N}}\",\"N\":{n}" + (trace is null ? "" : $",\"@tr\":\"{trace}\",\"@sp\":\"{n:x16}\"") + "}";

        File.WriteAllLines(path, Enumerable.Range(0, 20).Select(n => Line(n, null)));
        using var index = LogIndex.Open(path);
        Assert.AreEqual(0, index.GetStats().TraceCount);

        // the application starts writing inside requests
        File.AppendAllLines(path, Enumerable.Range(20, 15).Select(n => Line(n, TestData.TraceOf(n < 30 ? 1 : 2))));
        Assert.IsTrue(index.Refresh());
        Assert.AreEqual(2, index.GetStats().TraceCount);
        Assert.AreEqual(10, index.Query(new LogQuery { TraceId = index.FindTraceId(TestData.TraceOf(1)) }).Count);
        Assert.AreEqual(-1, index.GetRowInfo(5).TraceId, "rows from before are unchanged");

        // ... and then a stretch without them again, then the same trace once more
        File.AppendAllLines(path, Enumerable.Range(35, 5).Select(n => Line(n, null)).Concat(new[] { Line(40, TestData.TraceOf(1)) }));
        Assert.IsTrue(index.Refresh());
        Assert.AreEqual(2, index.GetStats().TraceCount, "the same trace keeps its id");
        Assert.AreEqual(11, index.Query(new LogQuery { TraceId = index.FindTraceId(TestData.TraceOf(1)) }).Count);

        // the file is replaced by one with no trace ids at all
        var generation = index.Generation;
        File.WriteAllLines(path, Enumerable.Range(0, 8).Select(n => Line(n, null)));
        Assert.IsTrue(index.Refresh());
        Assert.AreEqual(generation + 1, index.Generation);
        Assert.AreEqual(0, index.GetStats().TraceCount);
        Assert.IsNull(index.FindTraceId(TestData.TraceOf(1)));
    }
}
