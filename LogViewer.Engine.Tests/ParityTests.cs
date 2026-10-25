using Serilog.Events;
using Serilog.Formatting.Compact.Reader;

namespace LogViewer.Engine.Tests;

/// <summary>The same expectations as LogViewer.Server.Tests/ServerTests.cs, against the same sample log.</summary>
public class ParityTests
{
    private const string LogFileName = "UmbracoTraceLog.UNITTEST.20181112.json";
    private string _path = null!;
    private LogIndex _index = null!;

    [OneTimeSetUp]
    public void Open()
    {
        _path = Path.Combine(TestContext.CurrentContext.TestDirectory, LogFileName);
        _index = LogIndex.Open(_path);
    }

    [OneTimeTearDown]
    public void Close() => _index.Dispose();

    [Test]
    public void Contains_Correct_Error_Count()
    {
        Assert.AreEqual(2, _index.CountErrors(_index.All()));
    }

    [Test]
    public void Contains_Correct_Level_Counts()
    {
        var counts = _index.CountLevels(_index.All());

        Assert.AreEqual(385, counts.Verbose);
        Assert.AreEqual(1954, counts.Debug);
        Assert.AreEqual(62, counts.Information);
        Assert.AreEqual(7, counts.Warning);
        Assert.AreEqual(2, counts.Error);
        Assert.AreEqual(0, counts.Fatal);
    }

    [Test]
    public void Contains_Correct_Message_Templates()
    {
        var patterns = _index.GetPatterns(_index.All());

        Assert.AreEqual(43, patterns.Count);
        CollectionAssert.AllItemsAreUnique(patterns.Select(p => p.Template));
        Assert.AreEqual("{LogPrefix} Task added {TaskType}", patterns[0].Template);
        Assert.AreEqual(689, patterns[0].Count);
    }

    [TestCase("", 2410)]
    [TestCase("Has(@Exception)", 2)]
    [TestCase("IsDefined(@x)", 2)]
    [TestCase("IsDefined(Duration) and Duration > 1000", 13)]
    [TestCase("Has(@x)", 2)]
    [TestCase("Has(Duration) and Duration > 1000", 13)]
    [TestCase("Duration > 1000", 13)]
    [TestCase("Not(@Level = 'Verbose') and Not(@Level = 'Debug')", 71)]
    [TestCase("Not(@l = 'Verbose') and Not(@l = 'Debug')", 71)]
    [TestCase("StartsWith(SourceContext, 'Umbraco.Core')", 1183)]
    [TestCase("@MessageTemplate = '{EndMessage} ({Duration}ms) [Timing {TimingId}]'", 622)]
    [TestCase("@mt = '{EndMessage} ({Duration}ms) [Timing {TimingId}]'", 622)]
    [TestCase("SortedComponentTypes[?] = 'Umbraco.Web.Search.ExamineComponent'", 1)]
    [TestCase("Contains(SortedComponentTypes[?], 'DatabaseServer')", 1)]
    [TestCase("@Message like '%localhost%'", 388)]
    [TestCase("@m like '%localhost%'", 388)]
    [TestCase("runtime", 7)]
    public void Can_Query_With_Expressions(string filter, int expected)
    {
        var rows = _index.Query(new LogQuery { Filter = filter });

        Assert.AreEqual(expected, rows.Count);
    }

    [Test]
    public void Every_Indexed_Row_Matches_What_Serilog_Reads()
    {
        // Compare the index (timestamp, level, template, exception flag) with a plain sequential read of the same file
        var expected = new List<LogEvent>();
        using (var reader = new LogEventReader(new StreamReader(_path)))
            while (reader.TryRead(out var evt))
                expected.Add(evt);

        Assert.AreEqual(expected.Count, _index.Count);
        for (var row = 0; row < expected.Count; row++)
        {
            var info = _index.GetRowInfo(row);
            Assert.AreEqual(expected[row].Timestamp.UtcTicks, info.Timestamp.UtcTicks, $"timestamp, row {row}");
            Assert.AreEqual(expected[row].Level, info.Level, $"level, row {row}");
            Assert.AreEqual(expected[row].MessageTemplate.Text, info.MessageTemplate, $"template, row {row}");
            Assert.AreEqual(expected[row].Exception != null, info.HasException, $"exception, row {row}");
        }
    }

    [Test]
    public void Pages_Match_The_Old_Ordering()
    {
        var rows = _index.All();

        var newest = _index.ReadPage(rows, 1, 100, SortOrder.Descending);
        var oldest = _index.ReadPage(rows, 1, 100, SortOrder.Ascending);

        Assert.AreEqual(100, newest.Count);
        Assert.AreEqual(2409, newest[0].RowId);
        Assert.AreEqual(0, oldest[0].RowId);
        Assert.IsTrue(newest.Zip(newest.Skip(1)).All(p => p.First.Timestamp >= p.Second.Timestamp));
        Assert.AreEqual(10, _index.ReadPage(rows, 25, 100, SortOrder.Ascending).Count, "last page holds the remainder");
    }

    [Test]
    public void Index_Filters_Agree_With_Brute_Force()
    {
        var all = _index.All();
        var warningsAndAbove = _index.Query(new LogQuery { Levels = LogLevelMask.Warning | LogLevelMask.Error | LogLevelMask.Fatal });
        Assert.AreEqual(9, warningsAndAbove.Count);

        var mid = _index.GetRowInfo(1200).Timestamp;
        var after = _index.Query(new LogQuery { From = mid });
        var before = _index.Query(new LogQuery { To = mid });
        Assert.AreEqual(all.Count, after.Count + before.Count, "From is inclusive, To is exclusive, so they partition the file");

        var expectedAfter = Enumerable.Range(0, all.Count).Count(r => _index.GetRowInfo(r).Timestamp >= mid);
        Assert.AreEqual(expectedAfter, after.Count);
    }

    [Test]
    public void Same_Request_Finds_Every_Row_Of_That_Request()
    {
        var request = _index.FindCorrelation("HttpRequestId", "557f45ba-0888-4216-8723-e226d795a2f7");
        Assert.IsNotNull(request);

        var rows = _index.Query(new LogQuery { Correlation = request });
        Assert.Greater(rows.Count, 1);

        var viaFilter = _index.Query(new LogQuery { Filter = "HttpRequestId = '557f45ba-0888-4216-8723-e226d795a2f7'" });
        Assert.AreEqual(viaFilter.Count, rows.Count);
    }

    [Test]
    public void Histogram_Accounts_For_Every_Row()
    {
        var histogram = _index.GetHistogram(_index.All(), 50);

        Assert.LessOrEqual(histogram.Buckets.Count, 50);
        Assert.AreEqual(2410, histogram.Buckets.Sum(b => b.Counts.Sum()));
        Assert.AreEqual(2, histogram.Buckets.Sum(b => b.Counts[(int)LogEventLevel.Error]));
    }
}
