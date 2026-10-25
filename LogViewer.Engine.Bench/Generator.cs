using System.Text;

namespace LogViewer.Engine.Bench;

/// <summary>Writes a synthetic Umbraco-shaped CLEF file: ~450 byte lines, ~60 templates, request ids shared across rows, the odd stack trace.</summary>
internal static class Generator
{
    private static readonly string[] Templates =
    {
        "{LogPrefix} Task added {TaskType}",
        "{EndMessage} ({Duration}ms) [Timing {TimingId}]",
        "{StartMessage} [Timing {TimingId}]",
        "HTTP {Method} {Path} responded {StatusCode} in {Elapsed:0.0000} ms",
        "Connection timeout after {Duration}ms talking to {Host}",
        "Examine index {IndexName} rebuilt with {DocumentCount} documents",
        "Published content cache refreshed for {Count} items",
        "User {UserName} logged in from {IpAddress}",
        "Scheduled task {TaskName} completed",
        "Cache miss for key {CacheKey}",
        "Resolved {ServiceType} from the container",
        "Executing handler {HandlerName}",
        "Notification {NotificationType} published to {HandlerCount} handlers",
        "Database query took {Duration}ms: {Sql}",
        "Media {MediaId} moved to {FolderId}",
        "Content {ContentId} saved by {UserName}",
    };

    private static readonly string[] Sources =
    {
        "Umbraco.Core.Runtime.CoreRuntime", "Umbraco.Cms.Core.Scoping.ScopeProvider", "Umbraco.Cms.Infrastructure.Persistence.UmbracoDatabase",
        "Umbraco.Cms.Web.Common.Middleware.UmbracoRequestMiddleware", "Umbraco.Cms.Core.Services.ContentService", "Examine.Lucene.Providers.LuceneIndex",
        "Umbraco.Cms.Core.Cache.AppCaches", "Umbraco.Cms.Infrastructure.BackgroundJobs.RecurringHostedServiceBase", "Umbraco.Web.Search.ExamineComponent",
        "Microsoft.AspNetCore.Hosting.Diagnostics", "Umbraco.Cms.Core.Security.BackOfficeUserManager", "Umbraco.Cms.Core.Services.MediaService",
    };

    /// <param name="jitter">About 3% of lines get a timestamp up to 60 ms earlier than the line before, like threads that log slightly out of order.</param>
    public static void Write(string path, long targetBytes, bool jitter = false)
    {
        var rng = new Random(42);
        var stamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var request = Guid.NewGuid();
        var requestNumber = 1;
        var rowsLeftInRequest = 0;
        var sb = new StringBuilder(2048);

        using var writer = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 20);
        long written = 0;
        while (written < targetBytes)
        {
            if (rowsLeftInRequest-- <= 0)
            {
                request = new Guid(Enumerable.Range(0, 16).Select(_ => (byte)rng.Next(256)).ToArray());
                requestNumber++;
                rowsLeftInRequest = rng.Next(1, 60);
            }

            stamp = stamp.AddMilliseconds(rng.Next(1, 40));
            var template = Templates[rng.Next(Templates.Length)];
            var roll = rng.Next(1000);
            var level = roll switch { < 150 => "Verbose", < 750 => "Debug", < 950 => null, < 990 => "Warning", _ => "Error" };
            var duration = rng.Next(100) == 0 ? rng.Next(1000, 9000) : rng.Next(0, 300);

            var shown = jitter && rng.Next(100) < 3 ? stamp.AddMilliseconds(-rng.Next(1, 60)) : stamp;
            sb.Clear();
            sb.Append("{\"@t\":\"").Append(shown.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff")).Append("Z\",\"@mt\":\"").Append(template).Append('"');
            if (level is not null)
                sb.Append(",\"@l\":\"").Append(level).Append('"');
            if (level == "Error")
                sb.Append(",\"@x\":\"System.InvalidOperationException: Something went wrong\\n   at Umbraco.Cms.Core.Services.ContentService.Save(IContent content, Int32 userId)\\n   at Umbraco.Cms.Web.BackOffice.Controllers.ContentController.PostSave(ContentItemSave contentItem)\\n   at Microsoft.AspNetCore.Mvc.Infrastructure.ActionMethodExecutor.Execute(IActionResultTypeMapper mapper, ObjectMethodExecutor executor, Object controller, Object[] arguments)\"");

            sb.Append(",\"LogPrefix\":\"[Scheduler]\",\"TaskType\":\"Umbraco.Cms.Core.Sync.").Append(rng.Next(50)).Append("\",\"Duration\":").Append(duration)
              .Append(",\"TimingId\":\"").Append(rng.Next(0x1000000).ToString("x6")).Append("\",\"Host\":\"search-").Append(rng.Next(8)).Append(".internal\"")
              .Append(",\"UserName\":\"user").Append(rng.Next(200)).Append("@example.com\",\"StatusCode\":").Append(rng.Next(3) == 0 ? 404 : 200)
              .Append(",\"SourceContext\":\"").Append(Sources[rng.Next(Sources.Length)]).Append('"')
              .Append(",\"ProcessId\":27004,\"ProcessName\":\"dotnet\",\"ThreadId\":").Append(rng.Next(1, 40))
              .Append(",\"MachineName\":\"WEB-01\",\"Log4NetLevel\":\"DEBUG\",\"HttpRequestNumber\":").Append(requestNumber)
              .Append(",\"HttpRequestId\":\"").Append(request).Append("\"}\n");

            writer.Write(sb);
            written += sb.Length; // ASCII only, so chars == bytes
        }
    }
}
