using System.Text.Json;

namespace LogViewer.Engine.Tests;

/// <summary>
/// Following a request, trace or session through a log. The engine must not assume which property an application uses: every candidate gets
/// its own column, nothing is merged, and the answer can be checked against a plain count of the file's own lines.
/// </summary>
public class CorrelationTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "clef-corr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, true);

    private static string Fixture(string version) => Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", $"umbraco-{version}.excerpt.json");

    /// <summary>The oracle: parse every line with System.Text.Json, nothing from the engine.</summary>
    private static List<Dictionary<string, string>> Lines(string path)
        => File.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l =>
        {
            using var doc = JsonDocument.Parse(l);
            return doc.RootElement.EnumerateObject()
                .Where(p => p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                .ToDictionary(p => p.Name, p => p.Value.ToString());
        }).ToList();

    private static int[] RowsOf(RowSet rows) => Enumerable.Range(0, rows.Count).Select(rows.RowAt).OrderBy(r => r).ToArray();

    // ------------------------------------------------------------ real Umbraco logs

    [TestCase("12.3.10", false)]
    [TestCase("13.16.2", true)]
    [TestCase("17.7.1", true)]
    [TestCase("18.2.1", true)]
    public void Each_Property_Is_Indexed_On_Its_Own_And_Matches_A_Plain_Count_Of_The_File(string version, bool hasTraceIds)
    {
        var lines = Lines(Fixture(version));
        using var index = LogIndex.Open(Fixture(version));
        Assert.AreEqual(lines.Count, index.Count);

        foreach (var property in new LogIndexOptions().CorrelationProperties)
        {
            var expectedLines = lines.Count(l => l.ContainsKey(property));
            var expectedValues = lines.Where(l => l.ContainsKey(property)).Select(l => l[property]).Distinct().ToList();
            var stats = index.GetStats().Correlations.Single(c => c.Property == property);

            Assert.AreEqual(expectedLines, stats.Lines, $"{property}: lines that have it");
            Assert.AreEqual(expectedValues.Count, stats.DistinctValues, $"{property}: distinct values");

            foreach (var value in expectedValues)
            {
                var expectedRows = Enumerable.Range(0, lines.Count).Where(i => lines[i].TryGetValue(property, out var v) && v == value).ToArray();
                var rows = index.Query(new LogQuery { Correlation = index.FindCorrelation(property, value) });

                CollectionAssert.AreEqual(expectedRows, RowsOf(rows), $"{property} = {value}");
            }
        }

        Assert.AreEqual(hasTraceIds ? lines.Select(l => l.GetValueOrDefault("@tr")).Where(t => t is not null).Distinct().Count() : 0, index.GetStats().TraceCount);
    }

    [TestCase("12.3.10")]
    [TestCase("13.16.2")]
    [TestCase("17.7.1")]
    [TestCase("18.2.1")]
    public void One_Request_Is_Not_Split_Across_The_Ids_Of_Different_Properties(string version)
    {
        // The regression this exists for: HttpRequestId and RequestId used to share one column, so a request's lines were split
        // between two id spaces and "same request" returned a fraction of it (3 rows of 38 in a real Umbraco 17 log).
        var lines = Lines(Fixture(version));
        using var index = LogIndex.Open(Fixture(version));

        foreach (var requestId in lines.Where(l => l.ContainsKey("RequestId")).Select(l => l["RequestId"]).Distinct())
        {
            var whole = Enumerable.Range(0, lines.Count).Where(i => lines[i].GetValueOrDefault("RequestId") == requestId).ToArray();
            var byRequestId = RowsOf(index.Query(new LogQuery { Correlation = index.FindCorrelation("RequestId", requestId) }));
            CollectionAssert.AreEqual(whole, byRequestId, $"RequestId {requestId} is every line of the request");

            // Umbraco's own HttpRequestId is missing from the first and last line of a request, so it covers the middle only: never more
            var umbracoIds = whole.Select(i => lines[i].GetValueOrDefault("HttpRequestId")).Where(v => v is not null).Distinct().ToList();
            Assert.AreEqual(1, umbracoIds.Count, "one Umbraco request id per request");
            var byUmbraco = RowsOf(index.Query(new LogQuery { Correlation = index.FindCorrelation("HttpRequestId", umbracoIds[0]!) }));
            CollectionAssert.IsSubsetOf(byUmbraco, byRequestId);
            Assert.Less(byUmbraco.Length, byRequestId.Length, "HttpRequestId alone would have returned only part of the request");
        }
    }

    [TestCase("13.16.2")]
    [TestCase("17.7.1")]
    [TestCase("18.2.1")]
    public void The_Trace_Id_Is_The_Whole_Request_And_Agrees_With_RequestId(string version)
    {
        var lines = Lines(Fixture(version));
        using var index = LogIndex.Open(Fixture(version));

        foreach (var trace in lines.Where(l => l.ContainsKey("@tr")).Select(l => l["@tr"]).Distinct())
        {
            var expected = Enumerable.Range(0, lines.Count).Where(i => lines[i].GetValueOrDefault("@tr") == trace).ToArray();
            var byTrace = RowsOf(index.Query(new LogQuery { TraceId = index.FindTraceId(trace) }));
            CollectionAssert.AreEqual(expected, byTrace, $"trace {trace}");

            var requestIds = expected.Select(i => lines[i].GetValueOrDefault("RequestId")).Distinct().ToList();
            Assert.AreEqual(1, requestIds.Count, "in these logs a trace is one request");
            CollectionAssert.AreEqual(byTrace, RowsOf(index.Query(new LogQuery { Correlation = index.FindCorrelation("RequestId", requestIds[0]!) })));
        }

        // startup and background lines belong to no trace
        var withoutTrace = Enumerable.Range(0, lines.Count).Where(i => !lines[i].ContainsKey("@tr")).ToArray();
        Assert.Greater(withoutTrace.Length, 0);
        Assert.IsTrue(withoutTrace.All(r => index.GetRowInfo(r).TraceId == -1));
    }

    [TestCase("12.3.10")]
    [TestCase("13.16.2")]
    [TestCase("17.7.1")]
    [TestCase("18.2.1")]
    public void Row_Values_Are_Readable_And_Every_Line_Of_The_Real_Log_Parses(string version)
    {
        var lines = Lines(Fixture(version));
        using var index = LogIndex.Open(Fixture(version));

        Assert.AreEqual(0, index.GetStats().MalformedLines);
        for (var row = 0; row < lines.Count; row++)
        {
            Assert.AreEqual(lines[row].GetValueOrDefault("RequestId"), index.GetCorrelationValue(row, "RequestId"), $"row {row} RequestId");
            Assert.AreEqual(lines[row].GetValueOrDefault("HttpRequestId"), index.GetCorrelationValue(row, "HttpRequestId"), $"row {row} HttpRequestId");
            var entry = index.ReadRow(row);
            Assert.AreEqual(lines[row].GetValueOrDefault("@tr"), entry.TraceId, $"row {row} trace");
            Assert.AreEqual(lines[row].GetValueOrDefault("@sp"), entry.SpanId, $"row {row} span");
        }
    }

    [Test]
    public void The_Real_Logs_Differ_Between_Versions_Exactly_As_Measured()
    {
        // Facts about real Umbraco output that the design depends on, so a change to a fixture or to the reader is noticed
        using var v12 = LogIndex.Open(Fixture("12.3.10"));
        using var v13 = LogIndex.Open(Fixture("13.16.2"));
        using var v17 = LogIndex.Open(Fixture("17.7.1"));

        Assert.AreEqual(0, v12.GetStats().TraceCount, "Umbraco 12 does not write trace ids");
        Assert.Greater(v13.GetStats().TraceCount, 0, "Umbraco 13 does");
        Assert.Greater(v17.GetStats().TraceCount, 0, "and so does 17");
        foreach (var index in new[] { v12, v13, v17 })
        {
            var stats = index.GetStats().Correlations.ToDictionary(c => c.Property);
            Assert.Greater(stats["RequestId"].Lines, stats["HttpRequestId"].Lines, "ASP.NET's RequestId covers more lines than Umbraco's own id");
            Assert.AreEqual(0, stats["CorrelationId"].Lines, "properties this app does not write cost nothing");
            Assert.AreEqual(0, stats["TraceId"].Lines);
        }
    }

    // ------------------------------------------------------------ not tied to one application

    private string Write(string name, IEnumerable<string> lines)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string Line(int n, string extra) => $"{{\"@t\":\"2024-03-01T10:00:{n % 60:00}.{n:000}Z\",\"@mt\":\"event {{N}}\",\"N\":{n}{extra}}}";

    [Test]
    public void Any_Property_Name_Can_Be_Followed_Through_Options()
    {
        // Not Umbraco at all: an app that calls it X-Correlation-Id, with numeric session ids and a name needing no special handling
        var path = Write("other.json", Enumerable.Range(0, 60).Select(n => Line(n, $",\"X-Correlation-Id\":\"c-{n / 6}\",\"SessionId\":{n / 20},\"UserName\":\"u{n % 3}\"")));
        using var index = LogIndex.Open(path, options: new LogIndexOptions { CorrelationProperties = new[] { "X-Correlation-Id", "SessionId" } });

        var stats = index.GetStats().Correlations;
        CollectionAssert.AreEqual(new[] { "X-Correlation-Id", "SessionId" }, stats.Select(c => c.Property));
        Assert.AreEqual((60L, 10), (stats[0].Lines, stats[0].DistinctValues));
        Assert.AreEqual((60L, 3), (stats[1].Lines, stats[1].DistinctValues), "numbers are values too");

        CollectionAssert.AreEqual(Enumerable.Range(18, 6), RowsOf(index.Query(new LogQuery { Correlation = index.FindCorrelation("X-Correlation-Id", "c-3") })));
        CollectionAssert.AreEqual(Enumerable.Range(20, 20), RowsOf(index.Query(new LogQuery { Correlation = index.FindCorrelation("SessionId", "1") })));
        Assert.AreEqual("c-9", index.GetCorrelationValue(59, "X-Correlation-Id"));

        // a property that is not indexed is not guessed at
        Assert.IsNull(index.FindCorrelation("UserName", "u1"));
        Assert.IsNull(index.GetCorrelationValue(5, "UserName"));
        Assert.AreEqual(0, index.Query(new LogQuery { Correlation = new CorrelationFilter("UserName", 0) }).Count);
    }

    [Test]
    public void No_Correlation_Properties_At_All_Is_A_Valid_Choice()
    {
        var path = Write("none.json", Enumerable.Range(0, 30).Select(n => Line(n, ",\"RequestId\":\"r\"")));
        using var index = LogIndex.Open(path, options: new LogIndexOptions { CorrelationProperties = Array.Empty<string>() });

        Assert.AreEqual(30, index.Count);
        Assert.IsEmpty(index.GetStats().Correlations);
        Assert.IsNull(index.FindCorrelation("RequestId", "r"));
        Assert.AreEqual(30, index.Query(new LogQuery { Filter = "RequestId = 'r'" }).Count, "the expression still finds it, only the index column is absent");
    }

    [TestCase("")]
    [TestCase("  ")]
    [TestCase("@tr")]
    [TestCase("@mt")]
    public void Names_That_Are_Not_Ordinary_Properties_Are_Refused(string name)
    {
        var path = Write("x.json", new[] { Line(0, "") });
        Assert.Throws<ArgumentException>(() => LogIndex.Open(path, options: new LogIndexOptions { CorrelationProperties = new[] { name } }));
    }

    [Test]
    public void Duplicates_And_Too_Many_Names_Are_Refused()
    {
        var path = Write("x.json", new[] { Line(0, "") });
        Assert.Throws<ArgumentException>(() => LogIndex.Open(path, options: new LogIndexOptions { CorrelationProperties = new[] { "A", "A" } }));
        Assert.Throws<ArgumentException>(() => LogIndex.Open(path, options: new LogIndexOptions { CorrelationProperties = Enumerable.Range(0, 9).Select(i => "P" + i).ToArray() }));
        using var eight = LogIndex.Open(path, options: new LogIndexOptions { CorrelationProperties = Enumerable.Range(0, 8).Select(i => "P" + i).ToArray() });
        Assert.AreEqual(8, eight.GetStats().Correlations.Count);
    }

    [Test]
    public void A_Property_That_Only_Some_Lines_Have_Gives_The_Others_No_Value_Including_Across_Chunks()
    {
        // ~45 MB so the file is scanned in several parallel chunks; the property first appears more than halfway through,
        // so its column is created late and the rows before it must read as "none"
        const int Rows = 150_000;
        var padding = new string('x', 200);
        var path = Write("late.json", Enumerable.Range(0, Rows).Select(n =>
            Line(n, $",\"Pad\":\"{padding}\"" + (n >= 90_000 && n % 4 != 0 ? $",\"RequestId\":\"r-{n / 40}\"" : ""))));
        using var index = LogIndex.Open(path);

        var stats = index.GetStats().Correlations.Single(c => c.Property == "RequestId");
        var expectedLines = Enumerable.Range(90_000, Rows - 90_000).Count(n => n % 4 != 0);
        Assert.AreEqual(expectedLines, stats.Lines);
        Assert.AreEqual((Rows - 1) / 40 - 90_000 / 40 + 1, stats.DistinctValues);

        foreach (var row in new[] { 0, 1, 44_999, 59_999, 89_999, 90_000, 90_001, 90_002, 120_001, 149_999 })
            Assert.AreEqual(row >= 90_000 && row % 4 != 0 ? $"r-{row / 40}" : null, index.GetCorrelationValue(row, "RequestId"), $"row {row}");

        var request = index.Query(new LogQuery { Correlation = index.FindCorrelation("RequestId", "r-3000") });
        CollectionAssert.AreEqual(Enumerable.Range(120_000, 40).Where(n => n % 4 != 0), RowsOf(request));
    }

    [Test]
    public void A_Live_File_Can_Gain_A_Correlation_Property_And_Replacing_It_Starts_Over()
    {
        var path = Write("live.json", Enumerable.Range(0, 20).Select(n => Line(n, "")));
        using var index = LogIndex.Open(path);
        Assert.AreEqual(0, index.GetStats().Correlations.Sum(c => c.Lines));
        Assert.IsNull(index.FindCorrelation("RequestId", "r1"));

        File.AppendAllLines(path, Enumerable.Range(20, 20).Select(n => Line(n, $",\"RequestId\":\"r{n / 10}\"")));
        Assert.IsTrue(index.Refresh());
        Assert.AreEqual(20, index.GetStats().Correlations.Single(c => c.Property == "RequestId").Lines);
        CollectionAssert.AreEqual(Enumerable.Range(20, 10), RowsOf(index.Query(new LogQuery { Correlation = index.FindCorrelation("RequestId", "r2") })));
        Assert.IsNull(index.GetCorrelationValue(3, "RequestId"), "rows from before have none");

        // the column keeps working as more rows arrive after it was created
        File.AppendAllLines(path, Enumerable.Range(40, 5).Select(n => Line(n, $",\"RequestId\":\"r2\"")));
        Assert.IsTrue(index.Refresh());
        Assert.AreEqual(15, index.Query(new LogQuery { Correlation = index.FindCorrelation("RequestId", "r2") }).Count);

        File.WriteAllLines(path, Enumerable.Range(0, 8).Select(n => Line(n, "")));
        Assert.IsTrue(index.Refresh());
        Assert.AreEqual(0, index.GetStats().Correlations.Sum(c => c.Lines));
        Assert.IsNull(index.FindCorrelation("RequestId", "r2"));
    }

    [Test]
    public void A_Value_Written_In_Escaped_Form_Is_The_Same_Value()
    {
        var path = Write("esc.json", new[]
        {
            Line(0, ",\"RequestId\":\"a\\u0062c\""),
            Line(1, ",\"RequestId\":\"abc\""),
            Line(2, ",\"RequestId\":\"abd\""),
            Line(3, ",\"RequestId\":null"),
            Line(4, ",\"RequestId\":{\"nested\":1}"),
            Line(5, ",\"RequestId\":true"),
        });
        using var index = LogIndex.Open(path);

        var stats = index.GetStats().Correlations.Single(c => c.Property == "RequestId");
        Assert.AreEqual((3L, 2), (stats.Lines, stats.DistinctValues), "null, objects and booleans are not ids");
        CollectionAssert.AreEqual(new[] { 0, 1 }, RowsOf(index.Query(new LogQuery { Correlation = index.FindCorrelation("RequestId", "abc") })));
    }
}
