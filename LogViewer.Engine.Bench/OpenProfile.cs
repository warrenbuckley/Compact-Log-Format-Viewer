using System.Diagnostics;

namespace LogViewer.Engine.Bench;

/// <summary>Separates the cost of the scanner itself from JIT warm-up, thread count and the merge into the index columns.</summary>
internal static class OpenProfile
{
    public static void Run(string path)
    {
        var length = new FileInfo(path).Length;
        Console.WriteLine($"{length / 1024 / 1024} MB, {Environment.ProcessorCount} cores");

        for (var run = 1; run <= 4; run++)
        {
            var sw = Stopwatch.StartNew();
            using var index = LogIndex.Open(path);
            Console.WriteLine($"  LogIndex.Open run {run}: {sw.Elapsed.TotalMilliseconds,7:N0} ms  ({length / 1024.0 / 1024 / sw.Elapsed.TotalSeconds,6:N0} MB/s)");
        }

        // The scanner on its own, one thread, one 64 MB range
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        var range = Math.Min(length, 64L * 1024 * 1024);
        for (var run = 1; run <= 3; run++)
        {
            var result = new ChunkResult();
            var sw = Stopwatch.StartNew();
            LineScanner.ScanRange(handle, 0, range, result, CancellationToken.None, null);
            Console.WriteLine($"  scanner, 1 thread, {range / 1024 / 1024} MB, run {run}: {sw.Elapsed.TotalMilliseconds,6:N0} ms  ({range / 1024.0 / 1024 / sw.Elapsed.TotalSeconds,5:N0} MB/s, {sw.Elapsed.TotalMilliseconds * 1000 / result.Count:N2} us/line, {result.Count:N0} rows)");
        }
    }
}
