using System.Text;

namespace LogViewer.Engine.Tests;

public class EdgeCaseTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "clef-engine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, true);

    private static string Line(DateTimeOffset t, string level = "Information", string message = "Hello", string? request = null)
        => $"{{\"@t\":\"{t:O}\",\"@mt\":\"{message} {{N}}\"" + (level == "Information" ? "" : $",\"@l\":\"{level}\"") + ",\"N\":1"
           + (request is null ? "" : $",\"HttpRequestId\":\"{request}\"") + "}";

    private string Write(string name, string content, Encoding? encoding = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
        return path;
    }

    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void Unsorted_File_Is_Paged_In_Time_Order()
    {
        // Two rolled files concatenated: the second starts earlier than the first ends
        var lines = new[] { 10, 11, 12, 1, 2, 3 }.Select(s => Line(T0.AddSeconds(s), message: "m" + s));
        using var index = LogIndex.Open(Write("unsorted.json", string.Join("\n", lines) + "\n"));

        Assert.IsFalse(index.GetStats().Chronological);

        var ascending = index.ReadPage(index.All(), 1, 10, SortOrder.Ascending);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 10, 11, 12 }, ascending.Select(e => (int)(e.Timestamp - T0).TotalSeconds));

        var recent = index.Query(new LogQuery { From = T0.AddSeconds(3) });
        Assert.AreEqual(4, recent.Count);
        CollectionAssert.AreEqual(new[] { 3, 10, 11, 12 }, index.ReadPage(recent, 1, 10, SortOrder.Ascending).Select(e => (int)(e.Timestamp - T0).TotalSeconds));
    }

    [Test]
    public void Bom_And_Crlf_Line_Endings_Are_Handled()
    {
        var content = Line(T0) + "\r\n" + Line(T0.AddSeconds(1), "Error") + "\r\n";
        using var index = LogIndex.Open(Write("bom.json", content, new UTF8Encoding(true)));

        Assert.AreEqual(2, index.Count);
        Assert.AreEqual(1, index.CountErrors(index.All()));
        Assert.AreEqual("Hello 1", index.ReadRow(0).RenderedMessage);
    }

    [Test]
    public void Malformed_And_Blank_Lines_Are_Skipped_Not_Fatal()
    {
        var content = Line(T0) + "\n\nthis is not json\n{\"no\":\"timestamp\"}\n" + Line(T0.AddSeconds(1)) + "\n";
        using var index = LogIndex.Open(Write("messy.json", content));

        Assert.AreEqual(2, index.Count);
        Assert.AreEqual(2, index.GetStats().MalformedLines);
    }

    [Test]
    public void Refresh_Reads_Only_New_Lines_And_Waits_For_A_Half_Written_One()
    {
        var path = Write("live.json", Line(T0) + "\n" + Line(T0.AddSeconds(1)) + "\n");
        using var index = LogIndex.Open(path);
        Assert.AreEqual(2, index.Count);
        Assert.IsFalse(index.Refresh(), "nothing new");

        // Writer is mid-line
        var full = Line(T0.AddSeconds(2), "Warning");
        File.AppendAllText(path, Line(T0.AddSeconds(3)) + "\n" + full[..20]);
        Assert.IsTrue(index.Refresh());
        Assert.AreEqual(3, index.Count, "the complete line is indexed, the partial one is not");

        File.AppendAllText(path, full[20..] + "\n");
        Assert.IsTrue(index.Refresh());
        Assert.AreEqual(4, index.Count);
        Assert.IsFalse(index.GetStats().Chronological, "T+3 was written before T+2");
        Assert.AreEqual(1, index.CountLevels(index.All()).Warning);

        // Final line complete but no trailing newline yet
        File.AppendAllText(path, Line(T0.AddSeconds(9)));
        Assert.IsTrue(index.Refresh());
        Assert.AreEqual(5, index.Count);
        Assert.IsFalse(index.Refresh());
    }

    [Test]
    public void Refresh_Starts_Over_When_The_File_Is_Truncated()
    {
        var path = Write("rolled.json", string.Join("\n", Enumerable.Range(0, 50).Select(i => Line(T0.AddSeconds(i)))) + "\n");
        using var index = LogIndex.Open(path);
        Assert.AreEqual(50, index.Count);

        File.WriteAllText(path, Line(T0.AddDays(1), "Error") + "\n");
        Assert.IsTrue(index.Refresh());
        Assert.AreEqual(1, index.Count);
        Assert.AreEqual(1, index.CountErrors(index.All()));
        Assert.IsTrue(index.GetStats().Chronological);
    }

    [Test]
    public void Large_File_Is_Split_Into_Chunks_Without_Losing_Or_Reordering_Rows()
    {
        // ~40 MB: enough for several parallel chunks, with lines that land on chunk boundaries
        var sb = new StringBuilder();
        const int rows = 160_000;
        for (var i = 0; i < rows; i++)
            sb.Append(Line(T0.AddMilliseconds(i * 10), i % 100 == 0 ? "Error" : "Debug", "message number " + new string('x', i % 200), "req-" + (i / 25))).Append('\n');

        using var index = LogIndex.Open(Write("big.json", sb.ToString()));

        Assert.AreEqual(rows, index.Count);
        Assert.IsTrue(index.GetStats().Chronological);
        Assert.AreEqual(1600, index.CountErrors(index.All()));
        Assert.AreEqual(rows / 25, index.GetStats().Correlations.Single(c => c.Property == "HttpRequestId").DistinctValues);

        for (var row = 0; row < rows; row += 7919)
            Assert.AreEqual(T0.AddMilliseconds(row * 10), index.GetRowInfo(row).Timestamp);

        var sameRequest = index.Query(new LogQuery { Correlation = index.FindCorrelation("HttpRequestId", "req-1234") });
        Assert.AreEqual(25, sameRequest.Count);
        Assert.AreEqual(1234 * 25, sameRequest.RowAt(0));

        var refined = index.Query(new LogQuery { Levels = LogLevelMask.Error, Filter = "message" });
        Assert.AreEqual(1600, refined.Count);
    }

    [Test]
    public void Escaped_Characters_In_Templates_And_Request_Ids_Are_Unescaped_Once()
    {
        var content = "{\"@t\":\"2024-01-01T00:00:00Z\",\"@mt\":\"Quote \\\"{N}\\\" and \\u00e9\",\"N\":1,\"HttpRequestId\":\"a\\u0062c\"}\n"
                    + "{\"@t\":\"2024-01-01T00:00:01Z\",\"@mt\":\"Quote \\\"{N}\\\" and é\",\"N\":2,\"HttpRequestId\":\"abc\"}\n";
        using var index = LogIndex.Open(Write("escapes.json", content));

        var patterns = index.GetPatterns(index.All());
        Assert.AreEqual(1, patterns.Count, "both spellings are the same template");
        Assert.AreEqual("Quote \"{N}\" and é", patterns[0].Template);
        Assert.AreEqual(1, index.GetStats().Correlations.Single(c => c.Property == "HttpRequestId").DistinctValues);
    }

    [Test]
    public void Empty_File_Opens_With_No_Rows()
    {
        using var index = LogIndex.Open(Write("empty.json", ""));

        Assert.AreEqual(0, index.Count);
        Assert.AreEqual(0, index.GetHistogram(index.All(), 10).Buckets.Count);
        Assert.AreEqual(0, index.ReadPage(index.All(), 1, 100, SortOrder.Descending).Count);
    }
}

public class EmptyCandidateTests
{
    [Test]
    public void A_Filter_Over_Rows_The_Index_Has_Already_Ruled_Out_Returns_Nothing_Instead_Of_Crashing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clef-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "log.json");
            TestData.WriteMixedLog(path, 600);
            using var index = LogIndex.Open(path);
            var t = index.GetRowInfo(0).Timestamp;

            // Every one of these narrows to zero rows with the index alone and then still has a text filter to apply
            var empty = new[]
            {
                new LogQuery { Levels = LogLevelMask.Fatal, Filter = "Hello" },
                new LogQuery { Levels = LogLevelMask.Fatal, Filter = "Duration > 10" },
                new LogQuery { From = t.AddYears(5), Filter = "Hello" },
                new LogQuery { To = t.AddYears(-5), Filter = "Hello" },
                new LogQuery { TemplateId = 9999, Filter = "Hello" },
                new LogQuery { Correlation = new CorrelationFilter("HttpRequestId", 9999), Filter = "Hello" },
                new LogQuery { Correlation = new CorrelationFilter("NotIndexed", 0), Filter = "Hello" },
                new LogQuery { TraceId = 9999, Filter = "Hello" },
            };

            foreach (var query in empty)
            {
                var rows = index.Query(query);
                Assert.AreEqual(0, rows.Count);
                Assert.AreEqual(0, index.ReadPage(rows, 1, 50, SortOrder.Descending).Count);
                Assert.AreEqual(0, index.GetPatterns(rows).Count);
                Assert.AreEqual(0, index.GetHistogram(rows, 10).Buckets.Count);
            }

            // and through the typed search box
            Assert.AreEqual(0, index.QueryText("Hello from:2999-01-01").Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class ReadEventsTests
{
    [Test]
    public void Every_Event_Comes_Back_In_File_Order_Exactly_As_Serilog_Reads_Them()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clef-events-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // out of time order on purpose: an export is written in file order, not time order
            var path = Path.Combine(dir, "mixed.json");
            TestData.WriteMixedLog(path, 5000);
            var lines = File.ReadAllLines(path).ToList();
            (lines[10], lines[11]) = (lines[11], lines[10]);
            lines.Insert(7, "this line is not json");
            lines.Insert(8, "{\"@t\":\"2024-01-01T00:00:00Z\",\"@mt\":\"x\",\"@l\":\"Nope\"}");
            File.WriteAllLines(path, lines);

            using var index = LogIndex.Open(path);
            var actual = index.ReadEvents().ToList();
            var expected = lines
                .Select(l => FastReaderTests.Reference(l))
                .Where(e => e is not null).ToList();

            Assert.AreEqual(expected.Count, actual.Count, "unreadable lines are skipped");
            for (var i = 0; i < expected.Count; i++)
                FastReaderTests.AssertSame(expected[i]!, actual[i], $"event {i}");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void An_Empty_File_And_A_Cancelled_Read_Are_Handled()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clef-events2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var empty = Path.Combine(dir, "empty.json");
            File.WriteAllText(empty, "");
            using var none = LogIndex.Open(empty);
            Assert.IsEmpty(none.ReadEvents().ToList());

            var path = Path.Combine(dir, "log.json");
            TestData.WriteMixedLog(path, 5000);
            using var index = LogIndex.Open(path);
            using var cts = new CancellationTokenSource();
            var seen = 0;
            Assert.Throws<OperationCanceledException>(() =>
            {
                foreach (var _ in index.ReadEvents(cts.Token))
                    if (++seen == 100) cts.Cancel();
            });
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
