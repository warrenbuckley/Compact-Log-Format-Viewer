// Live-log check against a PUBLISHED LogViewer.Server: it starts the server, opens a log, connects a real SignalR client and then plays the
// part of an application that is still writing the log (a few lines, a burst, a half-written line, a continuous stream, a truncation) and
// asserts that the viewer is told, already has the new rows without calling /reload, holds back the half line, and never freezes.
//
//     dotnet run --project tools/live-check -- <published server dir> <scratch log file path>
//
// The server listens on a fixed port, so nothing else may be using 45678. Exit code 0 means every check passed.
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.SignalR.Client;

var serverDir = args[0]; var logPath = args[1];
File.WriteAllLines(logPath, Enumerable.Range(0, 100).Select(n => Line(n, "Debug")));
var psi = new ProcessStartInfo(Path.Combine(serverDir, "LogViewer.Server")) { WorkingDirectory = serverDir, RedirectStandardOutput = true, RedirectStandardError = true };
using var server = Process.Start(psi)!;
using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:45678/api/Viewer/") };
for (var i = 0; i < 60; i++) { try { await http.GetAsync(""); break; } catch { await Task.Delay(500); } }

var notifications = new List<DateTime>();
var hub = new HubConnectionBuilder().WithUrl("http://127.0.0.1:45678/log").Build();
hub.On("NotifyNewLogEntries", () => { lock (notifications) notifications.Add(DateTime.UtcNow); });
await hub.StartAsync();

async Task<int> Total() => (await http.GetFromJsonAsync<Dictionary<string, int>>("totals"))!.Values.Sum();
int Seen() { lock (notifications) return notifications.Count; }
async Task<bool> Until(Func<Task<bool>> condition, int ms = 5000) { var sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < ms) { if (await condition()) return true; await Task.Delay(50); } return false; }
void Check(string what, bool ok, string detail = "") { Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {what} {detail}"); if (!ok) Environment.ExitCode = 1; }

Console.WriteLine("open -> " + await http.GetStringAsync("open?filePath=" + Uri.EscapeDataString(logPath)));
Check("opened with 100 rows", await Total() == 100);

// 1. the application writes a few more lines: the server should notice, refresh itself and tell the client, without anyone calling /reload
var before = Seen();
File.AppendAllLines(logPath, Enumerable.Range(100, 20).Select(n => Line(n, "Warning")));
Check("client is notified of new lines", await Until(() => Task.FromResult(Seen() > before)));
Check("server already has them (no reload called)", await Until(async () => await Total() == 120), $"total now {await Total()}");

// 2. a burst of writes, like a busy app: debounced into a few notifications, and nothing is lost
before = Seen();
for (var i = 0; i < 300; i++) { File.AppendAllText(logPath, Line(1000 + i, "Debug") + "\n"); if (i % 25 == 0) await Task.Delay(30); }
Check("burst of 300 appends all arrive", await Until(async () => await Total() == 420), $"total now {await Total()}");
await Task.Delay(800);
var burstNotifications = Seen() - before;
Check("burst was debounced (far fewer notifications than writes)", burstNotifications >= 1 && burstNotifications < 60, $"{burstNotifications} notifications for 300 writes");

// 3. a half-written line stays out until it is finished
before = Seen();
var partial = Line(5000, "Error");
await using (var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
{
    var bytes = Encoding.UTF8.GetBytes(partial);
    fs.Write(bytes, 0, bytes.Length / 2); fs.Flush(true);
    await Task.Delay(700);
    Check("half-written line is not counted", await Total() == 420, $"total {await Total()}");
    fs.Write(bytes, bytes.Length / 2, bytes.Length - bytes.Length / 2); fs.Write(Encoding.UTF8.GetBytes("\n")); fs.Flush(true);
}
Check("finished line appears", await Until(async () => await Total() == 421));
Check("and it is an error", (await http.GetFromJsonAsync<int>("errors")) == 1);

// 4. the log is rolled/truncated: the viewer starts over and is told
before = Seen();
File.WriteAllLines(logPath, Enumerable.Range(0, 7).Select(n => Line(n, "Debug")));
Check("truncation notifies", await Until(() => Task.FromResult(Seen() > before)));
Check("and the index starts over", await Until(async () => await Total() == 7), $"total {await Total()}");

// 4b. an application that logs CONTINUOUSLY (a write every ~10 ms for 4 seconds, never quiet for a quarter of a second)
{
    var baseline = await Total(); before = Seen();
    var stop = false; var written = 0;
    var writer = Task.Run(async () => { while (!stop) { File.AppendAllText(logPath, Line(20000 + written, "Debug") + "\n"); written++; await Task.Delay(10); } });
    var samples = new List<int>();
    for (var i = 0; i < 8; i++) { await Task.Delay(500); samples.Add(await Total() - baseline); }
    stop = true; await writer;
    Console.WriteLine($"      rows seen while the writer was still going, every 0.5 s: {string.Join(", ", samples)} (writer wrote {written})");
    Check("viewer keeps up WHILE the log is still being written", samples.Count(x => x > 0) >= 6 && samples[3] > 0, $"{Seen() - before} notifications during the stream");
    Check("and a steady stream gives repeated notifications, not one at the end", Seen() - before >= 4);
    Check("everything is there once it stops", await Until(async () => await Total() == baseline + written), $"{await Total() - baseline} of {written}");
    await http.GetStringAsync("open?filePath=" + Uri.EscapeDataString(logPath)); // start the next check from a clean count
}

// 5. reload when already up to date is instant
var sw = Stopwatch.StartNew(); await http.GetStringAsync("reload"); Check("reload is instant", sw.ElapsedMilliseconds < 200, $"{sw.ElapsedMilliseconds} ms");

await hub.StopAsync(); server.Kill(); File.Delete(logPath);
Console.WriteLine(Environment.ExitCode == 0 ? "ALL LIVE CHECKS PASSED" : "SOME LIVE CHECKS FAILED");
static string Line(int n, string level) => $"{{\"@t\":\"2024-05-01T10:00:00.{n % 1000:000}Z\",\"@mt\":\"event {{N}}\",\"@l\":\"{level}\",\"N\":{n}}}";
