# Benchmark results

Synthetic Umbraco-shaped CLEF files (~500 byte lines, 16 templates, request ids shared across rows), 4 cores, 16 GB RAM, .NET 8, Release build.
Open is a single run. Queries are the median of three runs after one warm-up. Engine and baseline ran as separate processes.

Reproduce:

```
dotnet run -c Release --project LogViewer.Engine.Bench -- gen data.json 1000
dotnet run -c Release --project LogViewer.Engine.Bench -- engine data.json
dotnet run -c Release --project LogViewer.Engine.Bench -- baseline data.json
```

"Baseline" is a copy of the current `LogParser` read and search path (all lines parsed into a `List<LogEvent>`, every search sorts and scans the full list). It does not include the HTTP and JSON serialisation the real app adds on top, so it flatters the baseline.

| 1 GB, 2.0M rows | Current parser | LogIndex |
|---|---|---|
| Open | 104.8 s | 0.89 s |
| Managed memory after open | 5,979 MB | 62 MB |
| Peak working set | 6,911 MB | 551 MB |
| Level and error counts | 218 ms | 5 ms |
| Page 1 newest first (with template patterns) | 1,590 ms | 9 ms |
| Page 1000 | 1,654 ms | 6 ms |
| Level = Error | 1,594 ms | 2.4 ms (index) |
| `@l = 'Error'` expression | 1,594 ms | 7,356 ms |
| `Duration > 1000` expression | 3,975 ms | 7,404 ms |
| Plain word `timeout` | 7,734 ms | 7,998 ms |
| Error level + word `timeout` | n/a | 157 ms |
| 5 minute time range | n/a | 0.1 ms |
| Histogram, 100 buckets | n/a | 9.6 ms |
| Same request | n/a | 3.9 ms |
| Live tail, +2,000 lines | full reload | 326 ms |

| Open time | 100 MB | 500 MB | 1 GB |
|---|---|---|---|
| Current parser | 11.1 s | 50.7 s | 104.8 s |
| LogIndex | 0.39 s | 0.58 s | 0.89 s |

## Known weak spots

- Filters that must parse lines (expressions, plain words) are not faster than the in-memory parser, and `@l = 'Error'` as an expression is slower. The index-backed level filter answers the same question in milliseconds.
- Live tail cost grows with file size (119 ms at 500 MB, 326 ms at 1 GB) for 2,000 new lines. Not profiled yet. The first refresh after open reallocates every column because capacity is sized exactly at open.
