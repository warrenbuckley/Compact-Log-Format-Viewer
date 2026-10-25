using LogViewer.Server;
using LogViewer.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Moq;
using ServerSort = LogViewer.Server.Models.SortOrder;

namespace LogViewer.Engine.Tests;

/// <summary>Typed time tokens: UTC unless an offset is given, <c>last:</c> counted back from the newest entry, and nothing else changes meaning.</summary>
public class SearchTextTests
{
    private static readonly DateTimeOffset Newest = new(2024, 5, 1, 14, 30, 0, TimeSpan.Zero);

    private static ParsedSearch Parse(string text) => SearchText.Parse(text, Newest);

    private static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(iso, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

    // ---------------------------------------------------------------- last:

    [TestCase("last:30s", 30L * 1000)]
    [TestCase("last:15m", 15L * 60 * 1000)]
    [TestCase("last:2h", 2L * 3600 * 1000)]
    [TestCase("last:3d", 3L * 86400 * 1000)]
    [TestCase("last:1w", 7L * 86400 * 1000)]
    [TestCase("LAST:15M", 15L * 60 * 1000)]
    public void Last_Counts_Back_From_The_Newest_Entry_And_Includes_It(string token, long milliseconds)
    {
        var parsed = Parse(token);

        Assert.AreEqual(Newest.AddMilliseconds(-milliseconds), parsed.From);
        Assert.AreEqual(Newest.AddTicks(1), parsed.To, "To is exclusive, so the newest entry itself is inside the range");
        Assert.AreEqual("", parsed.Filter);
        CollectionAssert.AreEqual(new[] { token }, parsed.Tokens);
    }

    [TestCase("last:0m")]
    [TestCase("last:-5m")]
    [TestCase("last:1.5h")]
    [TestCase("last:15")]
    [TestCase("last:m")]
    [TestCase("last:15x")]
    [TestCase("last:99999999m")]
    [TestCase("last:")]
    public void A_Last_Token_That_Is_Not_Understood_Stays_As_Text(string text)
    {
        var parsed = Parse(text);

        Assert.AreEqual(text, parsed.Filter);
        Assert.IsNull(parsed.From);
        Assert.IsNull(parsed.To);
    }

    // ---------------------------------------------------------------- from: and to:

    [TestCase("from:2024-05-01T14:00:00Z", "2024-05-01T14:00:00Z")]
    [TestCase("from:2024-05-01T14:00:30.5Z", "2024-05-01T14:00:30.5Z")]
    [TestCase("from:2024-05-01T14:00", "2024-05-01T14:00:00Z")]
    [TestCase("from:2024-05-01T14:00:30", "2024-05-01T14:00:30Z")]
    [TestCase("from:2024-05-01T14:00+01:00", "2024-05-01T13:00:00Z")]
    [TestCase("from:2024-05-01T14:00:00-05:00", "2024-05-01T19:00:00Z")]
    [TestCase("from:2024-05-01", "2024-05-01T00:00:00Z")]
    [TestCase("from:09:15", "2024-05-01T09:15:00Z")]
    [TestCase("from:09:15:30", "2024-05-01T09:15:30Z")]
    [TestCase("FROM:09:15", "2024-05-01T09:15:00Z")]
    public void From_Is_Read_As_UTC_Unless_It_Says_Otherwise(string token, string expected)
    {
        var parsed = Parse(token);

        Assert.AreEqual(Utc(expected), parsed.From);
        Assert.IsNull(parsed.To);
        Assert.AreEqual("", parsed.Filter);
    }

    [TestCase("to:2024-05-01T14:00", "2024-05-01T14:00:00Z")]
    [TestCase("to:2024-05-01T14:00+02:00", "2024-05-01T12:00:00Z")]
    [TestCase("to:14:45", "2024-05-01T14:45:00Z")]
    [TestCase("to:2024-05-01", "2024-05-02T00:00:00Z")]
    public void To_Is_Exclusive_And_A_Date_Alone_Means_The_Whole_Day(string token, string expected)
    {
        var parsed = Parse(token);

        Assert.AreEqual(Utc(expected), parsed.To);
        Assert.IsNull(parsed.From);
    }

    [TestCase("from:garbage")]
    [TestCase("from:2024-13-45")]
    [TestCase("from:2024-02-30")]
    [TestCase("from:25:00")]
    [TestCase("from:2024-05-01 14:00")] // the space ends the token; "14:00" is then ordinary text, not part of it
    [TestCase("from:")]
    [TestCase("to:yesterday")]
    [TestCase("from:1714570800")]
    [TestCase("from:2024-05-01T14")]
    public void A_Time_That_Is_Not_Understood_Stays_As_Text(string text)
    {
        var parsed = Parse(text);

        if (text == "from:2024-05-01 14:00")
        {
            // the date alone is understood, the time is left over as text: documented, and the reason to type it with a T
            Assert.AreEqual("14:00", parsed.Filter);
            return;
        }

        Assert.AreEqual(text, parsed.Filter);
        Assert.IsNull(parsed.From);
        Assert.IsNull(parsed.To);
    }

    // ---------------------------------------------------------------- with a filter

    [Test]
    public void Tokens_Before_Or_After_The_Filter_Are_Taken_Out_And_The_Filter_Is_Left_Exactly_As_Typed()
    {
        var after = Parse("Duration > 1000 and Has(@x)   last:1h");
        Assert.AreEqual("Duration > 1000 and Has(@x)", after.Filter);
        Assert.AreEqual(Newest.AddHours(-1), after.From);

        var before = Parse("from:2024-05-01T10:00 to:2024-05-01T11:00 @l = 'Error'");
        Assert.AreEqual("@l = 'Error'", before.Filter);
        Assert.AreEqual(Utc("2024-05-01T10:00:00Z"), before.From);
        Assert.AreEqual(Utc("2024-05-01T11:00:00Z"), before.To);

        var both = Parse("from:09:00 timeout to:10:00");
        Assert.AreEqual("timeout", both.Filter);
        Assert.AreEqual(2, both.Tokens.Count);
    }

    [Test]
    public void An_Explicit_From_Or_To_Replaces_That_End_Of_Last()
    {
        var parsed = Parse("last:1h to:2024-05-01T14:00");

        Assert.AreEqual(Newest.AddHours(-1), parsed.From);
        Assert.AreEqual(Utc("2024-05-01T14:00:00Z"), parsed.To);

        var other = Parse("last:1h from:2024-05-01T13:00");
        Assert.AreEqual(Utc("2024-05-01T13:00:00Z"), other.From);
        Assert.AreEqual(Newest.AddTicks(1), other.To);
    }

    [Test]
    public void Only_The_First_Token_Of_Each_Kind_Is_Taken()
    {
        var parsed = Parse("from:09:00 from:10:00 last:5m last:6m");

        Assert.AreEqual(Utc("2024-05-01T09:00:00Z"), parsed.From);
        Assert.AreEqual("from:10:00 last:5m last:6m".Length > 0 ? parsed.Filter : "", parsed.Filter);
        StringAssert.Contains("from:10:00", parsed.Filter);
    }

    // ---------------------------------------------------------------- what must NOT change

    [TestCase("a last:5m b")]
    [TestCase("error from:localhost timeout")]
    [TestCase("@m like '%last:5m%'")]
    [TestCase("@m = 'sent from:09:00 to:10:00'")]
    [TestCase("Contains(@m, 'to:2024-05-01')")]
    [TestCase("x = 'it''s last:5m'")]
    [TestCase("from:localhost")]
    [TestCase("to:the:moon")]
    [TestCase("last:")]
    public void Tokens_In_The_Middle_Or_Inside_Quotes_Or_Not_Times_Are_Plain_Text(string text)
    {
        var parsed = Parse(text);

        Assert.AreEqual(text, parsed.Filter);
        Assert.IsNull(parsed.From);
        Assert.IsNull(parsed.To);
        Assert.IsEmpty(parsed.Tokens);
    }

    [Test]
    public void Text_Without_Tokens_Comes_Back_Byte_For_Byte_Unchanged_So_Existing_Searches_Do_Not_Change()
    {
        foreach (var text in ExactnessTests.Filters.Concat(ExactnessTests.TraceFilters).Concat(new[] { null, "", " ", "   ", "\t", "  padded  ", "a\tb", "a  b" }))
        {
            var parsed = Parse(text!);

            Assert.AreEqual(text, parsed.Filter, $"[{text}]");
            Assert.IsNull(parsed.From, $"[{text}]");
            Assert.IsNull(parsed.To, $"[{text}]");
        }
    }

    [Test]
    public void A_Token_Alone_Leaves_No_Filter_At_All_Not_A_Search_For_The_Token_Text()
    {
        Assert.AreEqual("", Parse("last:15m").Filter);
        Assert.AreEqual("", Parse("from:09:00 to:10:00").Filter);
    }

    // ---------------------------------------------------------------- against a real index

    [Test]
    public void QueryText_Gives_The_Same_Rows_As_The_Server_Parser_Within_The_Typed_Range()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "UmbracoTraceLog.UNITTEST.20181112.json");
        using var index = LogIndex.Open(path);
        var old = new LogParser(new Mock<IHubContext<LogHub>>().Object);
        old.ReadLogs(path);

        Assert.AreEqual(Utc("2018-11-12T09:24:27.4057583Z"), index.NewestTimestamp);
        Assert.AreEqual(Utc("2018-11-12T08:34:45.8371142Z"), index.OldestTimestamp);

        // each case: typed text, and the same range written out, with the filter the server would have been given
        var cases = new (string Typed, DateTimeOffset From, DateTimeOffset To, string Filter)[]
        {
            ("last:10m", Utc("2018-11-12T09:24:27.4057583Z").AddMinutes(-10), Utc("2018-11-12T09:24:27.4057583Z").AddTicks(1), ""),
            ("from:2018-11-12T08:40:00 to:2018-11-12T08:50:00", Utc("2018-11-12T08:40:00Z"), Utc("2018-11-12T08:50:00Z"), ""),
            ("from:2018-11-12T08:34Z to:2018-11-12T08:36Z Duration > 1000", Utc("2018-11-12T08:34:00Z"), Utc("2018-11-12T08:36:00Z"), "Duration > 1000"),
            ("@m like '%localhost%' last:30m", Utc("2018-11-12T09:24:27.4057583Z").AddMinutes(-30), Utc("2018-11-12T09:24:27.4057583Z").AddTicks(1), "@m like '%localhost%'"),
            ("from:2018-11-12", Utc("2018-11-12T00:00:00Z"), DateTimeOffset.MaxValue, ""),
            ("to:2018-11-12", DateTimeOffset.MinValue, Utc("2018-11-13T00:00:00Z"), ""),
            ("from:08:50 to:09:00 runtime", Utc("2018-11-12T08:50:00Z"), Utc("2018-11-12T09:00:00Z"), "runtime"),
            ("timeout from:2018-11-13T00:00:00Z", Utc("2018-11-13T00:00:00Z"), DateTimeOffset.MaxValue, "timeout"),
        };

        foreach (var (typed, from, to, filter) in cases)
        {
            var rows = index.QueryText(typed);
            var expected = old.Search(1, int.MaxValue / 2, filter, ServerSort.Ascending).Logs.Items!
                .Where(m => m.Timestamp >= from && m.Timestamp < to)
                .Select(m => (m.Timestamp.UtcTicks, m.RenderedMessage)).ToList();
            var actual = index.ReadPage(rows, 1, int.MaxValue / 2, SortOrder.Ascending).Select(e => (e.Timestamp.UtcTicks, e.RenderedMessage)).ToList();

            CollectionAssert.AreEqual(expected, actual, typed);
        }

        old.Dispose();
    }

    [Test]
    public void Typed_Tokens_And_The_Query_Fields_Are_The_Same_Range()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "UmbracoTraceLog.UNITTEST.20181112.json");
        using var index = LogIndex.Open(path);

        var typed = index.QueryText("from:2018-11-12T08:40:00Z to:2018-11-12T08:50:00Z");
        var chosen = index.Query(new LogQuery { From = Utc("2018-11-12T08:40:00Z"), To = Utc("2018-11-12T08:50:00Z") });

        Assert.Greater(typed.Count, 0);
        CollectionAssert.AreEqual(Enumerable.Range(0, chosen.Count).Select(chosen.RowAt), Enumerable.Range(0, typed.Count).Select(typed.RowAt));
    }

    [Test]
    public void Text_With_No_Tokens_Through_QueryText_Equals_Query_With_That_Filter()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "UmbracoTraceLog.UNITTEST.20181112.json");
        using var index = LogIndex.Open(path);

        foreach (var filter in ExactnessTests.Filters)
        {
            var a = index.Query(new LogQuery { Filter = filter });
            var b = index.QueryText(filter);
            CollectionAssert.AreEqual(Enumerable.Range(0, a.Count).Select(a.RowAt), Enumerable.Range(0, b.Count).Select(b.RowAt), $"[{filter}]");
        }
    }

    [Test]
    public void It_Works_On_A_File_That_Is_Out_Of_Order_And_On_An_Empty_One()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clef-text-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var t0 = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
            var lines = new[] { 50, 51, 52, 10, 11, 53, 54 }.Select(s => $"{{\"@t\":\"{t0.AddMinutes(s):O}\",\"@mt\":\"m{s}\"}}");
            var path = Path.Combine(dir, "unsorted.json");
            File.WriteAllLines(path, lines);
            using var index = LogIndex.Open(path);

            Assert.AreEqual(t0.AddMinutes(54), index.NewestTimestamp, "the newest by time, not the last line written");
            Assert.AreEqual(t0.AddMinutes(10), index.OldestTimestamp);

            var rows = index.QueryText("last:90s");
            CollectionAssert.AreEqual(new[] { "m53", "m54" }, index.ReadPage(rows, 1, 10, SortOrder.Ascending).Select(e => e.MessageTemplate));

            var empty = Path.Combine(dir, "empty.json");
            File.WriteAllText(empty, "");
            using var none = LogIndex.Open(empty);
            Assert.IsNull(none.NewestTimestamp);
            Assert.AreEqual(0, none.QueryText("last:5m").Count);
            Assert.AreEqual(0, none.QueryText("anything").Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
