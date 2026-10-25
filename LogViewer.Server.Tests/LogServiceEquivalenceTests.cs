using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogViewer.Server.Hubs;
using LogViewer.Server.Models;
using LogViewer.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;

namespace LogViewer.Server.Tests
{
    /// <summary>
    /// The Electron client must not be able to tell which reader is behind the API. The JSON each service produces is compared as text,
    /// serialised the way MVC does it (camelCase), over the sample log and real excerpts of logs written by Umbraco 12, 13, 17 and 18.
    /// </summary>
    public class LogServiceEquivalenceTests
    {
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };

        private static readonly string Dir = TestContext.CurrentContext.TestDirectory;

        public static IEnumerable<string> Logs => new[]
        {
            Path.Combine(Dir, "UmbracoTraceLog.UNITTEST.20181112.json"),
            Path.Combine(Dir, "fixtures", "umbraco-12.3.10.excerpt.json"),
            Path.Combine(Dir, "fixtures", "umbraco-13.16.2.excerpt.json"),
            Path.Combine(Dir, "fixtures", "umbraco-17.7.1.excerpt.json"),
            Path.Combine(Dir, "fixtures", "umbraco-18.2.1.excerpt.json"),
        };

        public static IEnumerable<string?> Filters => new string?[]
        {
            null, "", "Has(@x)", "Has(@Exception)", "Duration > 1000", "runtime", "RUNTIME", "@m like '%localhost%'",
            "@Level='Error' or @Level='Fatal' or Has(@Exception)", "StartsWith(SourceContext, 'Umbraco.Core')", "Has(@tr)", "@tr is not null",
            "Request starting", "50%", "(", "x=y", " ",
        };

        private static (LegacyLogService Legacy, IndexedLogService Indexed) Open(string path)
        {
            var legacy = new LegacyLogService(new LogParser(new Mock<IHubContext<LogHub>>().Object));
            var indexed = new IndexedLogService(new Mock<IHubContext<LogHub>>().Object);
            Assert.AreEqual(legacy.Open(path), indexed.Open(path), "number of entries");
            return (legacy, indexed);
        }

        private static string Serialise(object value) => JsonConvert.SerializeObject(value, Json);

        [TestCaseSource(nameof(Logs))]
        public void Totals_And_Errors_Serialise_Identically(string path)
        {
            var (legacy, indexed) = Open(path);
            using (indexed)
            {
                Assert.AreEqual(Serialise(legacy.TotalCounts()), Serialise(indexed.TotalCounts()));
                Assert.AreEqual(legacy.TotalErrors(), indexed.TotalErrors());
            }
        }

        [TestCaseSource(nameof(Logs))]
        public void Every_Search_Page_Serialises_Identically(string path)
        {
            var (legacy, indexed) = Open(path);
            using (indexed)
            {
                var compared = 0;
                foreach (var filter in Filters)
                    foreach (var sort in new[] { SortOrder.Descending, SortOrder.Ascending })
                        foreach (var (page, size) in new[] { (1, 100), (2, 7), (5, 7), (1, 1000) })
                        {
                            var expected = Serialise(legacy.Search(page, size, filter, sort));
                            var actual = Serialise(indexed.Search(page, size, filter, sort));
                            Assert.AreEqual(expected, actual, $"{Path.GetFileName(path)} filter={filter ?? "<null>"} sort={sort} page={page} size={size}");
                            compared++;
                        }

                Assert.Greater(compared, 100);
            }
        }

        [Test]
        public void Reload_Picks_Up_Appended_Lines_And_Search_Sees_Them()
        {
            var path = Path.Combine(Path.GetTempPath(), "clef-svc-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllLines(path, Enumerable.Range(0, 50).Select(n => $"{{\"@t\":\"2024-01-01T00:00:{n:00}Z\",\"@mt\":\"line {{N}}\",\"N\":{n}}}"));
                using var indexed = new IndexedLogService(new Mock<IHubContext<LogHub>>().Object);
                Assert.AreEqual(50, indexed.Open(path));

                File.AppendAllLines(path, new[] { "{\"@t\":\"2024-01-01T00:01:00Z\",\"@mt\":\"line {N}\",\"N\":50,\"@l\":\"Error\"}" });
                Assert.AreEqual(51, indexed.Reload());
                Assert.AreEqual(1, indexed.TotalErrors());
                Assert.AreEqual(51, indexed.Search().Logs.TotalItems);
                Assert.AreEqual("line 50", indexed.Search(1, 1, null, SortOrder.Descending).Logs.Items!.Single().RenderedMessage);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void Opening_A_Second_File_Replaces_The_First()
        {
            using var indexed = new IndexedLogService(new Mock<IHubContext<LogHub>>().Object);
            var first = Logs.First();
            var second = Logs.Last();

            var firstCount = indexed.Open(first);
            var secondCount = indexed.Open(second);

            Assert.AreNotEqual(firstCount, secondCount);
            Assert.AreEqual(second, indexed.LogFilePath);
            Assert.AreEqual(secondCount, indexed.Search(1, 1).Logs.TotalItems);
        }

        [Test]
        public void A_File_Without_Any_Log_Lines_Is_Refused_And_Does_Not_Replace_The_Open_One()
        {
            var bad = Path.Combine(Path.GetTempPath(), "clef-bad-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(bad, "{\"no\":\"timestamp\"}\n{\"still\":\"none\"}\n");
            try
            {
                using var indexed = new IndexedLogService(new Mock<IHubContext<LogHub>>().Object);
                var count = indexed.Open(Logs.First());

                Assert.Throws<InvalidDataException>(() => indexed.Open(bad));
                Assert.AreEqual(Logs.First(), indexed.LogFilePath, "the file that was open stays open");
                Assert.AreEqual(count, indexed.Search(1, 1).Logs.TotalItems);
            }
            finally
            {
                File.Delete(bad);
            }
        }

        [Test]
        public void Search_Before_Anything_Is_Open_Says_So_And_Legacy_Refuses_A_Time_Range()
        {
            using var indexed = new IndexedLogService(new Mock<IHubContext<LogHub>>().Object);
            Assert.IsFalse(indexed.LogIsOpen);
            Assert.Throws<InvalidOperationException>(() => indexed.Search());

            var (legacy, opened) = Open(Logs.First());
            using (opened)
                Assert.Throws<NotSupportedException>(() => legacy.Search(1, 10, null, SortOrder.Descending, DateTimeOffset.UtcNow, null));
        }

        [Test]
        public void A_Time_Range_Narrows_The_Indexed_Results()
        {
            var (_, indexed) = Open(Logs.First());
            using (indexed)
            {
                var all = indexed.Search(1, 1).Logs.TotalItems;
                var from = new DateTimeOffset(2018, 11, 12, 8, 40, 0, TimeSpan.Zero);
                var to = new DateTimeOffset(2018, 11, 12, 8, 50, 0, TimeSpan.Zero);
                var inRange = indexed.Search(1, 5000, null, SortOrder.Ascending, from, to);

                Assert.Greater(inRange.Logs.TotalItems, 0);
                Assert.Less(inRange.Logs.TotalItems, all);
                Assert.IsTrue(inRange.Logs.Items!.All(m => m.Timestamp >= from && m.Timestamp < to));
            }
        }

        [Test]
        public void An_Application_That_Logs_Continuously_Does_Not_Freeze_The_Viewer()
        {
            // A write every few milliseconds for several seconds: never quiet for a quarter of a second. The viewer must be told
            // and kept up to date during the stream, not only after it ends (a debounce here never fires and the viewer freezes).
            var path = Path.Combine(Path.GetTempPath(), "clef-stream-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllLines(path, Enumerable.Range(0, 20).Select(n => $"{{\"@t\":\"2024-01-01T00:00:{n:00}Z\",\"@mt\":\"line {{N}}\",\"N\":{n}}}"));

                var notifications = 0;
                var proxy = new Mock<IClientProxy>();
                proxy.Setup(p => p.SendCoreAsync("NotifyNewLogEntries", It.IsAny<object?[]>(), It.IsAny<System.Threading.CancellationToken>()))
                    .Callback(() => System.Threading.Interlocked.Increment(ref notifications))
                    .Returns(System.Threading.Tasks.Task.CompletedTask);
                var clients = new Mock<IHubClients>();
                clients.Setup(c => c.All).Returns(proxy.Object);
                var hub = new Mock<IHubContext<LogHub>>();
                hub.Setup(h => h.Clients).Returns(clients.Object);

                using var indexed = new IndexedLogService(hub.Object);
                Assert.AreEqual(20, indexed.Open(path));

                var stop = false;
                var written = 0;
                var writer = new System.Threading.Thread(() =>
                {
                    using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                    while (!stop)
                    {
                        var line = System.Text.Encoding.UTF8.GetBytes($"{{\"@t\":\"2024-02-01T00:00:00.{written % 1000:000}Z\",\"@mt\":\"stream {{N}}\",\"N\":{written}}}\n");
                        fs.Write(line, 0, line.Length);
                        fs.Flush(true);
                        written++;
                        System.Threading.Thread.Sleep(5);
                    }
                });
                writer.Start();

                var seenWhileWriting = new System.Collections.Generic.List<int>();
                for (var i = 0; i < 6; i++)
                {
                    System.Threading.Thread.Sleep(500);
                    seenWhileWriting.Add((int)indexed.Search(1, 1).Logs.TotalItems - 20);
                }

                var notificationsDuringStream = notifications;
                stop = true;
                writer.Join();

                Assert.GreaterOrEqual(notificationsDuringStream, 4, "notified repeatedly during the stream, not once at the end");
                Assert.Greater(seenWhileWriting[2], 0, "rows are visible while the writer is still going");
                CollectionAssert.AreEqual(seenWhileWriting.OrderBy(x => x), seenWhileWriting, "and the count only grows");
                Assert.Greater(seenWhileWriting[5], seenWhileWriting[2]);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
