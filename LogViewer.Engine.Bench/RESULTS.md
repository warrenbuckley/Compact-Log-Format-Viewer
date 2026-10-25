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

## .NET 10 and the latest Serilog packages

Everything now targets net10.0 (the server was on net7.0, out of support) with Serilog 4.4.0, Serilog.Expressions 5.0.0 (was 4.0.0),
Serilog.Formatting.Compact.Reader 4.0.0 (was 3.0.0) and Serilog.Sinks.File 7.0.0 (was 5.0.0). It was done in two steps so a change could be pinned on its cause:

1. Runtime only (net10.0, old packages): all tests and the recorded results unchanged.
2. Packages: no code change was needed, and all 182 recorded search results are identical (row counts, hashes of every row's time, level, template, rendered
   message, exception and properties, and of the template list), as are the engine tests and the server's own tests.

Performance is unchanged within noise (1 GB, warm file): `@l = 'Error'` 1.4 - 1.6 s, `Duration > 1000` 1.6 s, plain word 1.1 - 1.2 s, refresh 2 ms.
Serilog's own reader got ~30% faster (10.4 -> 7.4 us per line) and the engine's reader is still ~3x faster than that (2.6 us).

**Trimming had to be turned off.** `PublishTrimmed` was already on in the server project. Since .NET 8 its default mode is "full", which also trims the app's own assembly:
the published server started, then answered 404 to every request because MVC could not find the controllers. Partial trimming got past that but ASP.NET Core 10 MVC then
failed at startup (`IsEnhancedModelMetadataSupported`), and the documented feature switch did not take effect. Neither problem is visible in `dotnet test`; both were
found by running the published binary. The server is now published untrimmed (97 MB single file instead of 34 MB), and the published build was checked over HTTP:
`open`, `search` with `Has()`, `IsDefined()`, `like`, and numeric and string expressions, `totals` and `errors` all return the expected values.

## Trace and span ids: files with them, without them, or both

Loggers that write from inside an ASP.NET Core request add `@tr` (trace id) and `@sp` (span id) to the line; older loggers and background work do not,
and one file can hold both kinds. The fast reader first declined every line with them and left those to Serilog's slower reader.
500 MB, ~75% of requests carrying ids on every line (the `traces` option of the generator), parse-bound filters:

| | File without trace ids | File with trace ids, before | File with trace ids, after |
|---|---|---|---|
| `@l = 'Error'` expression | 619 ms | 3,009 ms | 656 ms |
| `Duration > 1000` expression | 626 ms | 3,060 ms | 638 ms |
| Plain word `timeout` | 498 ms | 3,253 ms | 538 ms |

The file without ids is unchanged by the work (620 / 633 / 487 ms after). Cost: one more 4-byte column per row (~4 MB per million rows).

What was added: `@tr`/`@sp` in the fast reader (same `LogEvent` trace and span ids as Serilog's reader, strict about what Serilog accepts: 32 or 16 lowercase hex digits,
not all zeros), a trace id column in the index with `LogQuery.TraceId`, `FindTraceId`, `GetTraceText`, `GetRequestText`, `RowInfo.TraceId`, and `LogEntry.TraceId`/`SpanId`.
The index filter and a typed `@tr = '...'` expression return identical rows (tested), so both ways of asking for a trace agree.
Tested for: no ids anywhere, ids on every line, `@tr` alone, `@sp` alone, `null`, escaped digits, ids Serilog rejects, a live file that gains or loses ids, a file replaced by one with none,
and about 160 filters including 26 on `@tr`/`@sp` compared against the real server parser.

One difference from the old parser, in the more forgiving direction: a line with an id Serilog rejects (or other values its reader throws on) used to make the whole file fail to open;
the engine reports that row as unreadable and carries on.

## The clicks the Electron client sends

The client builds three kinds of filter from clicks: the error badge (`@Level='Error' or @Level='Fatal' or Has(@Exception)`), a level (`@Level = 'Debug'`),
and a template (`@MessageTemplate = '...'`). All three used to read every line. The index already knows level, exception and template per row,
so it now says which rows could possibly match and only those are read; **the real Serilog expression still decides**, so the result is exact by construction
(the index can only get speed wrong, never the answer, as long as it never excludes a matching row).

In-process benchmark, ms (500 MB / 1 GB), before and after:

| Click | Reading every line | Now |
|---|---|---|
| Error badge | 681 / 1,559 | **47 / 76** |
| A level: Warning (4% of rows) | 694 / 1,361 | **39 / 77** |
| A template | 663 / 1,268 | **50 / 94** |
| A level: Debug (60% of rows) | 637 / 1,448 | 225 / 384 |

Over HTTP on the published server (500 MB): error badge 1,514 -> 295 ms (includes first-call warm-up), Warning 627 -> 63, template 932 -> 75, Debug 1,107 -> 688.

How it is kept exact:
- A piece of the text is only turned into an index condition if it matches exactly `@Level = 'X'`, `Has(@Exception)` / `IsDefined(@x)`, or `@MessageTemplate = '...'`. Everything else means "any row".
- `and` narrows to the pieces that were understood, `or` only narrows if every piece was understood, and a piece containing `not` or `if` at its top level is "any row".
- Rows whose `@l` text is not one of the six level names carry a flag and are never excluded by level (Serilog accepts `"3"` as Warning).
- A generated-expression test builds thousands of expressions (nested and/or, not, if, capitals, quotes in templates) over logs with deliberately awkward levels, exceptions and templates and
  compares the result with the fully unoptimised path: 9,000 expressions, 0 differences after one real bug was found and fixed (it ignored capital `OR` and excluded rows).
  Mutation tests: dropping case-insensitivity, the `if` guard, the unknown-level flag, quote unescaping, or turning `or` into `and` each fail it. Dropping the `not` guard does **not**
  fail it: Serilog's `not` binds tighter than `and`, as assumed, so that guard is a safety margin, not a necessity.
- When every piece was understood the expression cannot read an ordinary property, so the reader skips building them (the Debug row above, about twice as fast).
- Over HTTP, original and new server returned byte-identical responses for 3,707 requests (27 MB of search JSON), including these filters, an unknown level, an unescaped quote that breaks the expression, and capital `OR`.

## What real Umbraco sites write, and following a request in any application

Checked by building actual Umbraco sites from the published NuGet packages (SQLite, unattended install), sending requests, and reading the JSON log each one wrote
(`UmbracoTraceLog.<machine>.<date>.json`, written through a plain `CompactJsonFormatter`):

| Umbraco | Lines | With `@tr`/`@sp` | Notes |
|---|---|---|---|
| 12.3.10 | 3,929 | **0** | negative control: package versions predicted none, and there were none |
| 13.16.2 | 868 | 174 | first release that writes them (Compact 2.0.0 + Serilog 3.1.1) |
| 17.7.1 | 1,149 | 350 | |
| 18.2.1 | 1,165 | 351 | |

- Trace ids appear only on lines logged inside a request; startup and background lines have none, so one file is always a mix.
- In these logs every line that has `HttpRequestId` also has `@tr`, a trace was one request, and each trace had **one** span: all lines of a request shared one span id, so a
  span-per-lane view would draw a single bar. Spans only carry structure where an app creates child activities.
- Umbraco's `HttpRequestId` is its own GUID and is **missing from the first and last line of a request** ("Request starting", "Request finished"); ASP.NET Core's `RequestId`
  and `@tr` cover every line.
- The engine knows nothing about Umbraco. `@tr`/`@sp` are standard Serilog compact fields and are read from any application's log. Other applications correlate with other names,
  so the index tracks a configurable list of ordinary properties (`LogIndexOptions.CorrelationProperties`, default `HttpRequestId`, `RequestId`, `CorrelationId`, `TraceId`),
  each in its own column, and `IndexStats.Correlations` says how many lines each one covers so the UI can offer the best one or let the user choose.

**A bug this found.** The first version put `HttpRequestId` and `RequestId` into one column. On real Umbraco 13, 17 and 18 logs every request was split across two id spaces and
"same request" returned 3 rows of a 38-line request. Synthetic test data had only one such property, so tests missed it. Fixed by one column per property; the regression is covered by
real excerpts of the logs above (`LogViewer.Engine.Tests/fixtures`) checked against an independent `System.Text.Json` count, and reintroducing the merge fails 20 of 26 tests.
A column is only allocated once a row has a value for it, so unused properties cost nothing (open 438 - 457 ms and the same memory as before on 500 MB).

## Time ranges

**Serilog.Expressions 5.0.0 cannot filter by time.** Tested against the sample log with positive controls: `@t < Now()`, `@t > '2018-11-12T08:35:00Z'`, `UtcDateTime(@t) < UtcDateTime(Now())`
and `@t - Now() < 0` all compile and silently match 0 rows (comparison operators are numeric only); `DateTime(...)` and `TimeSpan(...)` throw "function name not recognized". Only text
tricks work, for ranges aligned to a time prefix: `StartsWith(ToString(@t, 'yyyy-MM-ddTHH:mm'), '2018-11-12T08:3')` (401 rows).

**Seq's query language can**, per its documentation: duration literals (`30d`, `1h30m`, `100ms`), `Now()`, `DateTime('2021-03-16T14:37:45.001')`, date arithmetic and comparison
(`@Timestamp >= Now() - 1h`), `OffsetIn()` for time zones, and `group by time(...)`. Its UI also has a separate time range selector. Serilog itself is a writer; its only time concept is file rolling.

So here a time range is not part of the Serilog expression. It is chosen in the UI or typed as tokens, and both become `LogQuery.From`/`To`, answered from the index (0.1 ms):

- `last:15m` (units s, m, h, d, w) counts back from the **newest entry in the file**, so an old file still works; the newest entry is included.
- `from:` is inclusive and `to:` exclusive. No offset means **UTC**; `Z` and `+01:00` are honoured. A date alone is midnight for `from:` and the whole day for `to:`;
  `14:00` means that time on the newest entry's date.
- Recognised only at the start or end of the text, outside single quotes, and only when the whole value is understood. Text without tokens comes back byte for byte unchanged,
  so existing searches behave exactly as before. `LogIndex.QueryText` applies this; `LogIndex.Query` is untouched.
- Tested in four time zones (UTC, +5:30, -7, +13): the UTC rule holds wherever the app runs.

This is our own convention, not Serilog or Seq syntax, and is a thin layer over `SearchText.Parse`; dropping it does not affect anything else.

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

- Synthetic data, one 4-core VM. **Cold files are disk-bound.** On this VM a file not read recently comes off disk at 40 - 82 MB/s (500 MB in 12.5 s with plain `dd`) and
  a cached one at ~4,000 MB/s. Opens of 1 GB files that had gone cold took 22 - 25 s, which is the time to read the file, not engine time. With the file cached the open is
  0.7 - 1.1 s. The numbers in this document are cached opens unless stated.
- Filters that must parse lines now cost about 1.1 - 1.7 s per GB on 4 cores. They scale with file size; index-backed filters (level, time, template, request) do not.
- `@m`-only lines and a few edge cases follow Serilog's reader exactly by falling back to it; they are slower but correct.
