# Benchmark results

Synthetic Umbraco-shaped CLEF files (~500 byte lines, 16 templates, request ids shared across rows), 4 cores, 16 GB RAM, .NET 8, Release build.
Queries are the median of three runs after one warm-up. Open is a single run, so it carries real run-to-run variance (see the notes).
Engine and baseline ran as separate processes.

Reproduce:

```
dotnet run -c Release --project LogViewer.Engine.Bench -- gen data.json 1000
dotnet run -c Release --project LogViewer.Engine.Bench -- engine data.json      # the index
dotnet run -c Release --project LogViewer.Engine.Bench -- baseline data.json    # the current parser
dotnet run -c Release --project LogViewer.Engine.Bench -- parse data.json       # per-line cost of reading a line
dotnet run -c Release --project LogViewer.Engine.Bench -- open data.json        # cold vs warm open, scanner alone
```

"Baseline" is a copy of the current `LogParser` read and search path: every line parsed into a `List<LogEvent>`, every search sorts and
scans the full list. It does not include the HTTP and JSON serialisation the real app adds on top, so it flatters the baseline.

## 1 GB file, 2.0M rows

| | Current parser | Index, first version | Index now |
|---|---|---|---|
| Open | 104.8 s | 0.89 s | 0.7 - 1.1 s |
| Managed memory after open | 5,979 MB | 62 MB | 76 MB |
| Peak working set | 6,911 MB | 551 MB | ~460 MB |
| Level and error counts | 218 ms | 5 ms | 4 ms |
| Page 1, newest first (with patterns) | 1,590 ms | 9 ms | 3 ms |
| Page 1000 | 1,654 ms | 6 ms | 1 ms |
| Level = Error (index) | 1,594 ms | 2.4 ms | 2 ms |
| `@l = 'Error'` expression | 1,594 ms | 7,356 ms | 805 ms |
| `Duration > 1000` expression | 3,975 ms | 7,404 ms | 832 - 885 ms |
| Plain word `timeout` | 7,734 ms | 7,998 ms | 772 - 813 ms |
| Error level + word `timeout` | n/a | 157 ms | 60 ms |
| 5 minute time range | n/a | 0.1 ms | 0.1 ms |
| Histogram, 100 buckets | n/a | 9.6 ms | 6 ms |
| Same request | n/a | 3.9 ms | 4 ms |
| Live tail, +2,000 lines | full reload | 326 ms | 2 - 3 ms |

"Index now" is the second profiling pass run with `DOTNET_TC_CallCountingDelayMs=0` (see below). Without it, open is 1.1 s cold and the first
filter after open is slower.

| Open time | 100 MB | 500 MB | 1 GB |
|---|---|---|---|
| Current parser | 11.1 s | 50.7 s | 104.8 s |
| Index now | 0.39 s | 0.6 s | 0.7 - 1.1 s |

## Where the time went

Per line, single thread, 523-byte lines (`parse` mode):

| Reader | us per line | allocated per line |
|---|---|---|
| Serilog `LogEventReader.ReadFromString` (first version) | 10.5 | 14.9 KB |
| `FastEventReader` (Utf8JsonReader, cached template) | 2.9 | 2.9 KB |
| `FastEventReader`, properties the filter does not use skipped | 1.4 - 1.7 | 0.65 - 0.8 KB |
| `@m like '%x%' ci` through Serilog.Expressions vs direct contains | 5.5 vs 0.7 | |

- Newtonsoft's `JObject` tree was ~95% of a parse-bound filter. Replaced by `FastEventReader`, which falls back to Serilog's own reader for any line outside the
  common shape (`@r`, `@tr`/`@sp`, `@m` without `@mt`, non-standard JSON, values Serilog rejects).
- Filters build only the properties their expression names plus the ones in the line's own template (needed to render `@m`). `@p` or `Properties` keeps everything.
- The index scanner itself runs at about 0.7 us per line and 730 MB/s per thread. A warm open of 500 MB takes ~290 ms; the rest of a cold open is JIT warm-up.
- Live tail: the first refresh after open used to reallocate every column. Columns now get 25% headroom.
- Scaling across 4 cores is about 3.1x on the parse path. GC settings (concurrent off, server GC, larger gen0) moved it by 10 - 25% at most and are not worth their memory cost.

## .NET startup settings (measured)

The runtime waits ~100 ms before promoting hot code to its optimised tier, which is a large share of a sub-second operation.
`DOTNET_TC_CallCountingDelayMs=0` set in the environment of the server process:

| | default | delay 0 |
|---|---|---|
| Cold open, 500 MB | ~575 ms | ~420 ms |
| First parse-bound filter after open, 100 MB | 340 - 415 ms | 86 - 104 ms |

No effect on steady state. Other settings tried and rejected: `TieredCompilation=0` (scanner 2x slower, no PGO), `TieredPGO=0`, OSR tuning (no change),
ReadyToRun (~12% on its own, nothing on top of the delay setting).

## Caveats

- Synthetic data, one 4-core VM. Open varies a lot run to run here: one 1 GB open took 2.1 s (cold page cache) against 0.7 - 1.1 s otherwise.
- Filters that must parse lines now cost about 0.8 s per GB on 4 cores. They scale with file size; index-backed filters (level, time, template, request) do not.
- `@m`-only lines and a few edge cases follow Serilog's reader exactly by falling back to it; they are slower but correct.
