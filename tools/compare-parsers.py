#!/usr/bin/env python3
"""
Asks a running LogViewer.Server the same questions the Electron client asks and saves every raw response, so two servers
(for example LOGVIEWER_PARSER=legacy and the default indexed reader) can be compared byte for byte with `diff -r`.

    python3 tools/compare-parsers.py <server dir> <log dir> <out dir> [legacy]

The server is the published binary in <server dir>; it listens on a fixed port, so run the two modes one after the other.
Every file in <log dir> is opened and queried, and the export file is written next to the responses so it can be compared too.
"""
import json, os, re, signal, subprocess, sys, time, urllib.error, urllib.parse, urllib.request

server_dir, log_dir, out_dir = sys.argv[1:4]
legacy = len(sys.argv) > 4 and sys.argv[4] == "legacy"
BASE = "http://127.0.0.1:45678/api/Viewer"
os.makedirs(out_dir, exist_ok=True)

FILTERS = [None, "", "Has(@x)", "Has(@Exception)", "Duration > 1000", "runtime", "RUNTIME", "@m like '%localhost%'",
           "@Level='Error' or @Level='Fatal' or Has(@Exception)",  # what the client's error button sends
           "StartsWith(SourceContext, 'Umbraco.Core')", "Not(@l = 'Verbose') and Not(@l = 'Debug')", "Has(@tr)", "@tr is not null",
           "Request starting", "Request", "50%", "(", "x=y", " ", "Duration >"]
SORTS = ["Descending", "Ascending"]
PAGE_SIZES = [100, 7]

env = dict(os.environ)
env.pop("LOGVIEWER_PARSER", None)
if legacy:
    env["LOGVIEWER_PARSER"] = "legacy"
proc = subprocess.Popen(["./LogViewer.Server"], cwd=server_dir, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

def get(path, **params):
    url = BASE + path + ("?" + urllib.parse.urlencode({k: v for k, v in params.items() if v is not None}) if params else "")
    try:
        with urllib.request.urlopen(url, timeout=300) as r:
            return r.status, r.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8")

try:
    for _ in range(60):
        try:
            urllib.request.urlopen("http://127.0.0.1:45678/", timeout=2)
            break
        except urllib.error.HTTPError:
            break
        except Exception:
            time.sleep(0.5)

    log = {}
    def record(name, result):
        status, body = result
        body = re.sub(r'"traceId":"[^"]*"', '"traceId":"(random per request)"', body)  # ASP.NET adds a fresh one to every validation error
        log[name] = {"status": status, "body": body}

    record("search-before-open", get("/search"))
    record("totals-before-open", get("/totals"))
    record("open-missing-file", get("/open", filePath=os.path.join(log_dir, "does-not-exist.json")))

    for name in sorted(os.listdir(log_dir)):
        path = os.path.join(log_dir, name)
        record(f"{name} open", get("/open", filePath=path))
        record(f"{name} totals", get("/totals"))
        record(f"{name} errors", get("/errors"))
        for filt in FILTERS:
            for sort in SORTS:
                for size in PAGE_SIZES:
                    for page in (1, 2, 5):
                        record(f"{name} search filter={filt!r} sort={sort} size={size} page={page}", get("/search", pageNumber=page, pageSize=size, filterExpression=filt, sort=sort))
        export_path = os.path.join(out_dir, f"export-{name}.txt")
        record(f"{name} export-custom-template", get("/export", messageTemplate="{Timestamp:O} {Level:u3} {Message:lj} {Properties:j}{NewLine}{Exception}", newFileName=export_path))
        record(f"{name} export-no-file-name", get("/export", messageTemplate="{Message}", newFileName=""))
        record(f"{name} reload", get("/reload"))
        record(f"{name} totals-after-reload", get("/totals"))
finally:
    proc.send_signal(signal.SIGTERM)
    try:
        proc.wait(10)
    except Exception:
        proc.kill()

# one file per request so a diff points at exactly what differs
for i, (name, value) in enumerate(log.items()):
    with open(os.path.join(out_dir, f"{i:05d}.json"), "w", encoding="utf-8") as f:
        json.dump({"request": name, **value}, f, indent=1, ensure_ascii=False)
print(f"{len(log)} requests recorded in {out_dir}")
