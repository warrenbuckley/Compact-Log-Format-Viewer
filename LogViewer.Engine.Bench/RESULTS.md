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
| `@l = 'Error'` expression | 1,594 ms | 7,356 ms | 1,595 - 1,635 ms |
| `Duration > 1000` expression | 3,975 ms | 7,404 ms | 1,517 - 1,667 ms |
| Plain word `timeout` | 7,734 ms | 7,998 ms | 1,146 - 1,435 ms |
| Error level + word `timeout` | n/a | 157 ms | 73 - 75 ms |
| 5 minute time range | n/a | 0.1 ms | 0.1 ms |
| Histogram, 100 buckets | n/a | 9.6 ms | 6 ms |
| Same request | n/a | 3.9 ms | 4 ms |
| Live tail, +2,000 lines | full reload | 326 ms | 2 - 3 ms |

"Index now" runs with `DOTNET_TC_CallCountingDelayMs=0` (see below) and with exact server semantics (see "Exactness"). Without the setting, open is
1.1 s cold and the first filter after open is slower.

Exactness has a price. An earlier profiling pass reached 772 - 885 ms on these three filters by (a) replacing Serilog's `like` with a direct
case-insensitive contains and (b) building only the properties an expression names. Both were removed so every search matches the server exactly:
typed expressions now build every property, and a plain word runs the real `@m like '%word%' ci` expression. The remaining speed-up over the current
parser comes from the faster line reader, parallel scanning, and the index narrowing candidates by level and time first.

| Open time | 100 MB | 500 MB | 1 GB |
|---|---|---|---|
| Current parser | 11.1 s | 50.7 s | 104.8 s |
| Index now | 0.39 s | 0.6 s | 0.7 - 1.1 s |

## Exactness: the engine returns what the server returns

Every search runs through Serilog.Expressions with the rules `LogParser` uses today (a plain word is `@m like '%word%' ci`, an expression that does not
compile falls back to it, typed expressions see every property). `ExactnessTests` runs the **real** `LogViewer.Server` `LogParser` as the oracle over
about 140 filter strings on two logs and compares the matching rows in order, the template list, level totals, error count and newest-first pages.
It found two differences that a count comparison would have missed, both now matched:

- rows that share a timestamp stay in file order in a newest-first page (the server's sort is stable);
- templates with equal counts are listed by where they first appear among the matching rows.

Heavy-tie logs, in and out of time order, are checked page by page in both directions.

## Live files (a log that is still being written)

`LiveFileTests` runs a writer thread (share-read, flushing after batches, with lines that are visible half written) against concurrent
`Refresh`, `Query`, `ReadPage` and `GetHistogram` calls, in and out of time order, and then compares the finished file with the server's parser.

Out-of-order timestamps are normal in real logs (threads log at slightly different moments), so the benchmark has a `jitter` option
(about 3% of lines up to 60 ms early). Before and after the incremental time-order fix:

| 1 GB, ~3% of lines slightly out of order | Before | After |
|---|---|---|
| Each refresh (+2,000 lines) | ~50 ms (re-sorted every row) | 2 - 4 ms |
| First refresh after open | 53 ms | 2 - 4 ms |
| 5 minute time range | 2.7 ms (linear scan) | 0.1 ms (binary search over the time order) |
| Open | 0.52 s | 0.84 s (includes sorting the order once) |

Also covered: a half-written last line is left for the next refresh; a truncated file is re-indexed; a file replaced by a larger, different file
(or one that starts the same and differs later) is detected from fingerprints of its head and of the bytes before the indexed end, and
`LogIndex.Generation` increases so the app knows earlier row ids are void; result sets taken before a refresh stay readable afterwards.

Not covered here: Windows file sharing (this ran on Linux; the file is opened read-only with share read/write/delete, the same flags the current parser uses),
and the file-system watcher that would trigger `Refresh` in the server.

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
- A plain-word search builds only the properties in each line's own template (all `@m` reads). Typed expressions build every property, because nothing is assumed about what they read. (An earlier version also pruned properties for typed expressions: ~2x faster, removed in favour of exactness.)
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
- Filters that must parse lines now cost about 1.1 - 1.7 s per GB on 4 cores. They scale with file size; index-backed filters (level, time, template, request) do not.
- `@m`-only lines and a few edge cases follow Serilog's reader exactly by falling back to it; they are slower but correct.
