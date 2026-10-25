using System.Collections.Concurrent;
using System.Text;
using LogViewer.Server;
using LogViewer.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Moq;
using EngineSort = LogViewer.Engine.SortOrder;
using ServerSort = LogViewer.Server.Models.SortOrder;

namespace LogViewer.Engine.Tests;

/// <summary>A log that is open in the viewer while the application that owns it is still writing to it.</summary>
public class LiveFileTests
{
    private static readonly DateTimeOffset T0 = new(2024, 5, 1, 12, 0, 0, TimeSpan.Zero);
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "clef-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, true);

    private static string Line(int n, DateTimeOffset t, string? message = null)
    {
        var level = n % 17 == 0 ? "Error" : n % 4 == 0 ? "Warning" : "Debug";
        var exception = level == "Error" ? $",\"@x\":\"System.Exception: failure {n}\"" : "";
        return $"{{\"@t\":\"{t:O}\",\"@mt\":\"{message ?? "Hello"} {{N}}\",\"@l\":\"{level}\"{exception},\"N\":{n},\"Duration\":{n % 50}}}";
    }

    /// <summary>Rows in time order: (ticks, then row id), which is how the engine and the server both order them.</summary>
    private static int[] ExpectedOrder(IReadOnlyList<long> ticks) => Enumerable.Range(0, ticks.Count).OrderBy(i => ticks[i]).ThenBy(i => i).ToArray();

    [TestCase(false)]
    [TestCase(true)]
    public void A_Log_Being_Written_While_It_Is_Read_Stays_Consistent(bool outOfOrder)
    {
        var path = Path.Combine(_dir, "live.json");
        var rng = new Random(outOfOrder ? 5 : 3);
        var written = new List<string>();
        var writtenTicks = new List<long>();
        var writtenLock = new object();

        string NextLine()
        {
            var n = written.Count;
            var t = T0.AddMilliseconds(n * 7);
            if (outOfOrder && rng.Next(100) < 6)
                t = t.AddMilliseconds(-rng.Next(1, 80));

            var line = Line(n, t);
            lock (writtenLock)
            {
                written.Add(line);
                writtenTicks.Add(t.UtcTicks);
            }

            return line;
        }

        var initial = Enumerable.Range(0, 200).Select(_ => NextLine()).ToList();
        File.WriteAllText(path, string.Join("\n", initial) + "\n");
        using var index = LogIndex.Open(path);
        const int TotalLines = 8000;
        var errors = new ConcurrentQueue<Exception>();
        var writerDone = false;

        // Like a logging sink: share-read, flush after each batch, and sometimes a line is visible half written
        var writer = new Thread(() =>
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                while (written.Count < TotalLines)
                {
                    for (var i = 0; i < rng.Next(1, 40) && written.Count < TotalLines; i++)
                    {
                        var bytes = Encoding.UTF8.GetBytes(NextLine() + "\n");
                        if (rng.Next(10) == 0)
                        {
                            var half = rng.Next(1, bytes.Length - 1);
                            fs.Write(bytes, 0, half);
                            fs.Flush(true);
                            Thread.Yield();
                            fs.Write(bytes, half, bytes.Length - half);
                        }
                        else
                        {
                            fs.Write(bytes, 0, bytes.Length);
                        }
                    }

                    fs.Flush(true);
                    Thread.Sleep(rng.Next(0, 3));
                }
            }
            catch (Exception ex) { errors.Enqueue(ex); }
            finally { writerDone = true; }
        });

        var readers = Enumerable.Range(0, 2).Select(r => new Thread(() =>
        {
            try
            {
                var lastCount = 0;
                while (!Volatile.Read(ref writerDone))
                {
                    var all = index.All();
                    Assert.GreaterOrEqual(all.Count, lastCount, "the index never shrinks while the file only grows");
                    lastCount = all.Count;

                    // A set taken before a refresh must stay readable and ordered after it
                    var page = index.ReadPage(all, 1, 40, EngineSort.Descending);
                    Assert.IsTrue(page.Zip(page.Skip(1)).All(p => p.First.Timestamp >= p.Second.Timestamp), "newest first");

                    var warnings = index.Query(new LogQuery { Levels = LogLevelMask.Warning | LogLevelMask.Error });
                    Assert.LessOrEqual(index.CountErrors(warnings), warnings.Count);
                    Assert.DoesNotThrow(() => index.Query(new LogQuery { Filter = r == 0 ? "Hello" : "Duration > 40" }));
                    index.GetHistogram(index.All(), 20);
                }
            }
            catch (Exception ex) { errors.Enqueue(ex); }
        })).ToList();

        writer.Start();
        readers.ForEach(t => t.Start());

        var previous = 0;
        while (!Volatile.Read(ref writerDone))
        {
            index.Refresh();
            Assert.GreaterOrEqual(index.Count, previous);
            previous = index.Count;
            Thread.Sleep(1);
        }

        writer.Join();
        readers.ForEach(t => t.Join());
        CollectionAssert.IsEmpty(errors, string.Join("\n", errors.Select(e => e.ToString())));

        // Once the writer stops, one more refresh must reach every line
        index.Refresh();
        Assert.AreEqual(TotalLines, index.Count);
        Assert.AreEqual(outOfOrder, !index.GetStats().Chronological);

        for (var row = 0; row < TotalLines; row += 13)
            Assert.AreEqual(writtenTicks[row], index.GetRowInfo(row).Timestamp.UtcTicks, $"row {row}");

        var order = ExpectedOrder(writtenTicks);
        var all2 = index.All();
        CollectionAssert.AreEqual(order, Enumerable.Range(0, all2.Count).Select(all2.RowAt), "time order after live appends");

        // And the file as it ended up gives the same answers as the server's own parser
        var old = new LogParser(new Mock<IHubContext<LogHub>>().Object);
        old.ReadLogs(path);
        foreach (var filter in new[] { "", "Hello", "@l = 'Warning'", "Duration > 40", "Has(@x)" })
        {
            var expected = old.Search(1, int.MaxValue / 2, filter, ServerSort.Descending);
            var rows = index.Query(new LogQuery { Filter = filter });
            Assert.AreEqual(expected.Logs.TotalItems, rows.Count, filter);
            CollectionAssert.AreEqual(
                expected.Logs.Items!.Select(m => (m.Timestamp.UtcTicks, m.RenderedMessage)),
                index.ReadPage(rows, 1, int.MaxValue / 2, EngineSort.Descending).Select(e => (e.Timestamp.UtcTicks, e.RenderedMessage)),
                filter);
        }

        old.Dispose();
    }

    [Test]
    public void Time_Order_Stays_Exact_After_Every_Refresh_Even_With_Rows_That_Belong_Inside_Earlier_Ones()
    {
        var path = Path.Combine(_dir, "order.json");
        var rng = new Random(21);
        var ticks = new List<long>();
        var lines = new List<string>();

        void Add(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var n = lines.Count;
                // mostly moving forward; 20% land anywhere earlier, including exactly on an existing timestamp
                var t = rng.Next(5) == 0 && ticks.Count > 0
                    ? (rng.Next(2) == 0 ? new DateTimeOffset(ticks[rng.Next(ticks.Count)], TimeSpan.Zero) : T0.AddMilliseconds(rng.Next(0, Math.Max(1, n * 10))))
                    : T0.AddMilliseconds(n * 10);
                lines.Add(Line(n, t));
                ticks.Add(t.UtcTicks);
            }
        }

        Add(50);
        File.WriteAllLines(path, lines);
        using var index = LogIndex.Open(path);

        for (var round = 0; round < 60; round++)
        {
            var before = lines.Count;
            Add(rng.Next(1, 30));
            File.AppendAllLines(path, lines.Skip(before));
            index.Refresh();

            Assert.AreEqual(lines.Count, index.Count);
            var all = index.All();
            CollectionAssert.AreEqual(ExpectedOrder(ticks), Enumerable.Range(0, all.Count).Select(all.RowAt), $"order after round {round}");

            var from = T0.AddMilliseconds(rng.Next(0, lines.Count * 10));
            var to = from.AddMilliseconds(rng.Next(1, 800));
            var expected = ExpectedOrder(ticks).Where(r => ticks[r] >= from.UtcTicks && ticks[r] < to.UtcTicks).ToArray();
            var found = index.Query(new LogQuery { From = from, To = to });
            CollectionAssert.AreEqual(expected, Enumerable.Range(0, found.Count).Select(found.RowAt), $"time range after round {round}");
        }
    }

    [Test]
    public void A_Set_Taken_Before_A_Refresh_Still_Reads_The_Same_Rows_Afterwards()
    {
        foreach (var outOfOrder in new[] { false, true })
        {
            var path = Path.Combine(_dir, $"snapshot-{outOfOrder}.json");
            var lines = Enumerable.Range(0, 300).Select(n => Line(n, T0.AddMilliseconds(n * 10 + (outOfOrder && n % 9 == 0 ? -25 : 0)))).ToList();
            File.WriteAllLines(path, lines);
            using var index = LogIndex.Open(path);

            var snapshot = index.Query(new LogQuery { Levels = LogLevelMask.Warning });
            var all = index.All();
            var before = index.ReadPage(all, 1, 300, EngineSort.Descending).Select(e => e.RowId).ToList();
            var beforeWarnings = index.ReadPage(snapshot, 1, 300, EngineSort.Ascending).Select(e => e.RowId).ToList();

            // new rows that sort after, and (when out of order) rows that sort inside the old ones
            File.AppendAllLines(path, Enumerable.Range(300, 200).Select(n => Line(n, T0.AddMilliseconds(n * 10 - (outOfOrder ? 1500 : 0)))));
            Assert.IsTrue(index.Refresh());

            CollectionAssert.AreEqual(before, index.ReadPage(all, 1, 300, EngineSort.Descending).Select(e => e.RowId), $"all, outOfOrder={outOfOrder}");
            CollectionAssert.AreEqual(beforeWarnings, index.ReadPage(snapshot, 1, 300, EngineSort.Ascending).Select(e => e.RowId), $"warnings, outOfOrder={outOfOrder}");
        }
    }

    [Test]
    public void Plain_Growth_Does_Not_Reset_The_Index()
    {
        var path = Path.Combine(_dir, "grow.json");
        File.WriteAllLines(path, Enumerable.Range(0, 100).Select(n => Line(n, T0.AddSeconds(n))));
        using var index = LogIndex.Open(path);
        var generation = index.Generation;

        File.AppendAllLines(path, Enumerable.Range(100, 50).Select(n => Line(n, T0.AddSeconds(n))));
        Assert.IsTrue(index.Refresh());

        Assert.AreEqual(150, index.Count);
        Assert.AreEqual(generation, index.Generation);
    }

    [Test]
    public void A_File_Truncated_And_Started_Again_Is_Reindexed()
    {
        var path = Path.Combine(_dir, "rolled.json");
        File.WriteAllLines(path, Enumerable.Range(0, 200).Select(n => Line(n, T0.AddSeconds(n), "old")));
        using var index = LogIndex.Open(path);
        var generation = index.Generation;

        File.WriteAllLines(path, Enumerable.Range(0, 20).Select(n => Line(n, T0.AddDays(1).AddSeconds(n), "new")));
        Assert.IsTrue(index.Refresh());

        Assert.AreEqual(20, index.Count);
        Assert.AreEqual(generation + 1, index.Generation);
        Assert.IsTrue(index.GetRowInfo(0).MessageTemplate.StartsWith("new"));
    }

    [Test]
    public void A_File_Replaced_By_A_Larger_Different_File_Is_Reindexed_Not_Read_From_The_Middle()
    {
        var path = Path.Combine(_dir, "replaced.json");
        File.WriteAllLines(path, Enumerable.Range(0, 100).Select(n => Line(n, T0.AddSeconds(n), "alpha")));
        using var index = LogIndex.Open(path);
        var generation = index.Generation;

        // Bigger than before, so a length check alone would carry on from the old offset
        File.WriteAllLines(path, Enumerable.Range(0, 400).Select(n => Line(n, T0.AddDays(2).AddSeconds(n), "beta")));
        index.Refresh();

        Assert.AreEqual(400, index.Count);
        Assert.AreEqual(generation + 1, index.Generation);
        Assert.IsTrue(Enumerable.Range(0, 400).All(r => index.GetRowInfo(r).MessageTemplate.StartsWith("beta")));
    }

    [Test]
    public void A_File_That_Starts_The_Same_But_Differs_Later_Is_Still_Detected()
    {
        var path = Path.Combine(_dir, "samestart.json");
        var common = Enumerable.Range(0, 100).Select(n => Line(n, T0.AddSeconds(n), "same")).ToList();
        File.WriteAllLines(path, common);
        using var index = LogIndex.Open(path);
        var generation = index.Generation;

        // Same first 50 lines, then different content, and longer overall
        File.WriteAllLines(path, common.Take(50).Concat(Enumerable.Range(50, 300).Select(n => Line(n, T0.AddSeconds(n), "different"))));
        index.Refresh();

        Assert.AreEqual(350, index.Count);
        Assert.AreEqual(generation + 1, index.Generation);
        Assert.IsTrue(index.GetRowInfo(349).MessageTemplate.StartsWith("different"));
    }
}
