using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LogViewer.Engine.Tests;

/// <summary>
/// Recorded results of every search in <see cref="ExactnessTests.Filters"/> over two logs: how many rows, and a hash of the rows
/// (time, level, template, rendered message, exception, every property) and of the template list. These catch a change that comes from
/// a dependency, such as a newer Serilog.Expressions reading a filter differently, which comparing the engine with the server parser
/// cannot see because both use the same library.
/// Regenerate on purpose with UPDATE_GOLDEN=1 after deciding that a difference is acceptable.
/// </summary>
public class GoldenTests
{
    private static string GoldenFile([CallerFilePath] string source = "") => Path.Combine(Path.GetDirectoryName(source)!, "golden", "search-results.json");

    private static string Hash(IEnumerable<string> lines)
    {
        using var sha = SHA256.Create();
        foreach (var line in lines)
        {
            sha.TransformBlock(Encoding.UTF8.GetBytes(line + "\n"), 0, Encoding.UTF8.GetByteCount(line) + 1, null, 0);
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!)[..16];
    }

    [Test]
    public void Search_Results_Have_Not_Changed_Since_They_Were_Recorded()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clef-golden-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var mixed = Path.Combine(dir, "mixed.json");
            TestData.WriteMixedLog(mixed, 6000);
            var files = new[] { Path.Combine(TestContext.CurrentContext.TestDirectory, "UmbracoTraceLog.UNITTEST.20181112.json"), mixed };

            var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in files)
            {
                using var index = LogIndex.Open(path);
                foreach (var filter in ExactnessTests.Filters)
                {
                    var rows = index.Query(new LogQuery { Filter = filter });
                    var entries = index.ReadPage(rows, 1, int.MaxValue / 2, SortOrder.Ascending);
                    var rowHash = Hash(entries.Select(e =>
                        $"{e.Timestamp.UtcTicks}|{e.Level}|{e.MessageTemplate}|{e.RenderedMessage}|{e.Exception}|" +
                        string.Join(",", e.Properties.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value))));
                    var templateHash = Hash(index.GetPatterns(rows).Select(p => $"{p.Template}|{p.Count}"));
                    actual[$"{Path.GetFileName(path)} :: {filter}"] = $"{rows.Count}:{rowHash}:{templateHash}";
                }
            }

            if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
            {
                File.WriteAllText(GoldenFile(), JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true }) + "\n");
                Assert.Pass($"recorded {actual.Count} results");
            }

            var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(GoldenFile()))!;
            var differences = actual.Where(kv => !expected.TryGetValue(kv.Key, out var e) || e != kv.Value)
                .Select(kv => $"{kv.Key}\n    recorded {(expected.TryGetValue(kv.Key, out var e) ? e : "(none)")}\n    now      {kv.Value}")
                .Concat(expected.Keys.Except(actual.Keys).Select(k => $"{k}\n    recorded but no longer produced"))
                .ToList();

            Assert.IsEmpty(differences, $"{differences.Count} of {expected.Count} results changed:\n" + string.Join("\n", differences.Take(25)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
