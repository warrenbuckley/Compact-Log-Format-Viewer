using System.Diagnostics;
using System.Text;
using LogViewer.Engine.Expressions;
using Serilog.Events;
using Serilog.Formatting.Compact.Reader;
using Serilog.Parsing;

namespace LogViewer.Engine.Bench;

/// <summary>Per-line cost of reading a CLEF line into a LogEvent, so changes to the reader can be measured in isolation.</summary>
internal static class ParseProfile
{
    private static readonly MessageTemplateParser Parser = new();

    public static void Run(string path)
    {
        const int Lines = 200_000;
        var lines = File.ReadLines(path).Take(Lines).Select(Encoding.UTF8.GetBytes).ToArray();
        var text = lines.Select(Encoding.UTF8.GetString).ToArray();
        // One shared MessageTemplate per distinct template text, as LogIndex does (the reader caches per template instance)
        var shared = new Dictionary<string, MessageTemplate>();
        var templates = text.Select(l =>
        {
            var templateText = LogEventReader.ReadFromString(l).MessageTemplate.Text;
            if (!shared.TryGetValue(templateText, out var template))
                shared[templateText] = template = Parser.Parse(templateText);

            return template;
        }).ToArray();
        var events = text.Select(l => LogEventReader.ReadFromString(l)).ToArray();
        Console.WriteLine($"{lines.Length:N0} lines, avg {lines.Average(l => l.Length):N0} bytes, {Environment.ProcessorCount} cores");
        Console.WriteLine($"| {"stage",-46} | {"us/line",8} | {"B/line",7} |");

        Single("Serilog LogEventReader.ReadFromString", i => LogEventReader.ReadFromString(text[i]));
        var fast = new FastEventReader();
        Single("FastEventReader", i => fast.TryRead(lines[i], templates[i], out _));
        var error = FilterCompiler.Compile("@l = 'Error'")!.Matches;
        var duration = FilterCompiler.Compile("Duration > 1000")!.Matches;
        var word = FilterCompiler.Compile("timeout")!.Matches;
        Single("  + predicate @l = 'Error'", i => { fast.TryRead(lines[i], templates[i], out var e); error(e); });
        Single("  + predicate Duration > 1000", i => { fast.TryRead(lines[i], templates[i], out var e); duration(e); });
        Single("  + predicate word 'timeout' (render + contains)", i => { fast.TryRead(lines[i], templates[i], out var e); word(e); });
        Single("predicate alone: word 'timeout'", i => word(events[i]));

        foreach (var filter in new[] { "@l = 'Error'", "Duration > 1000", "timeout" })
        {
            var compiled = FilterCompiler.Compile(filter)!;
            var pruned = new FastEventReader();
            pruned.KeepOnly(compiled.Properties);
            Single($"  + predicate {filter}, as the engine runs it", i => { pruned.TryRead(lines[i], templates[i], out var e); compiled.Matches(e); });
        }

        Console.WriteLine();
        foreach (var threads in new[] { 1, 2, 4 })
        {
            var sw = Stopwatch.StartNew();
            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
            {
                var reader = new FastEventReader();
                for (var i = t; i < lines.Length * threads; i += threads)
                    reader.TryRead(lines[i % lines.Length], templates[i % lines.Length], out _);
            });
            var perLine = sw.Elapsed.TotalMilliseconds * 1000 * threads / (lines.Length * threads) / threads;
            Console.WriteLine($"scaling: {threads} thread(s) each reading all {lines.Length:N0} lines: {sw.Elapsed.TotalMilliseconds,6:N0} ms  (ideal = single-thread time)");
        }
    }

    private static void Single(string name, Action<int> body)
    {
        // Warm up by time, not count: the runtime waits ~100 ms before promoting hot code to its optimised tier
        var warm = Stopwatch.StartNew();
        for (var i = 0; warm.ElapsedMilliseconds < 600; i++) body(i % 200_000);
        GC.Collect();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        const int N = 200_000;
        for (var i = 0; i < N; i++) body(i);
        var us = sw.Elapsed.TotalMilliseconds * 1000 / N;
        Console.WriteLine($"| {name,-46} | {us,8:N2} | {(GC.GetAllocatedBytesForCurrentThread() - allocated) / N,7:N0} |");
    }
}
