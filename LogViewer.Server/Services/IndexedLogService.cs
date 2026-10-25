using System;
using System.IO;
using System.Linq;
using System.Threading;
using LogViewer.Engine;
using LogViewer.Server.Hubs;
using LogViewer.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using SortOrder = LogViewer.Server.Models.SortOrder;

namespace LogViewer.Server.Services
{
    /// <summary>
    /// Reads the log through <see cref="LogIndex"/>: the file is indexed once (about a second per gigabyte) and a page, a count or a
    /// search then reads only what it needs. While a file is open it is watched, and new lines are picked up incrementally in
    /// milliseconds instead of re-reading everything.
    /// </summary>
    public sealed class IndexedLogService : ILogService, IDisposable
    {
        private const string DefaultExportTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss,fff} [P{ProcessId}/D{AppDomainId}/T{ThreadId}] {Level:u3}  {SourceContext} - {Message:lj}{NewLine}{Exception}";
        private const int ChangeDelayMilliseconds = 250;

        private readonly IHubContext<LogHub> _hub;
        private readonly object _openLock = new object();
        private readonly FileSystemWatcher _watcher = new FileSystemWatcher();
        private readonly Timer _changeTimer;
        private volatile LogIndex? _index;

        public IndexedLogService(IHubContext<LogHub> hub)
        {
            _hub = hub;
            LogFilePath = string.Empty;

            // The handler is added once. (The original parser added another on every open, so a file opened five times notified five times.)
            _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
            _watcher.Changed += (_, _) => _changeTimer!.Change(ChangeDelayMilliseconds, Timeout.Infinite);
            _changeTimer = new Timer(_ => OnFileChanged(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public bool LogIsOpen => _index != null;

        public string LogFilePath { get; private set; }

        public int Open(string filePath)
        {
            var index = LogIndex.Open(filePath);

            if (index.Count == 0 && index.GetStats().MalformedLines > 0)
            {
                index.Dispose();
                throw new InvalidDataException("The file has no lines in the compact JSON log format (each line needs at least an @t timestamp).");
            }

            LogIndex? previous;
            lock (_openLock)
            {
                previous = _index;
                _index = index;
                LogFilePath = filePath;
                Watch(filePath);
            }

            previous?.Dispose();
            return index.Count;
        }

        public int? Reload()
        {
            var index = _index;
            if (index == null)
                return null;

            index.Refresh();
            return index.Count;
        }

        public LogLevelCounts TotalCounts() => With(index =>
        {
            var counts = index.CountLevels(index.All());
            return new LogLevelCounts
            {
                Verbose = (int)counts.Verbose,
                Information = (int)counts.Information,
                Debug = (int)counts.Debug,
                Warning = (int)counts.Warning,
                Error = (int)counts.Error,
                Fatal = (int)counts.Fatal
            };
        });

        public int TotalErrors() => With(index => (int)index.CountErrors(index.All()));

        public void ExportTextFile(string messageTemplate, string newFileName)
        {
            if (string.IsNullOrEmpty(messageTemplate))
                messageTemplate = DefaultExportTemplate;

            // Written in file order, like the original, one block at a time
            With(index =>
            {
                using (var logger = new LoggerConfiguration().WriteTo.File(newFileName, outputTemplate: messageTemplate).CreateLogger())
                {
                    foreach (var evt in index.ReadEvents())
                        logger.Write(evt);
                }

                return 0;
            });
        }

        public LogResults Search(int pageNumber = 1, int pageSize = 100, string? filterExpression = null, SortOrder sort = SortOrder.Descending, DateTimeOffset? from = null, DateTimeOffset? to = null)
            => With(index =>
            {
                var rows = index.Query(new LogQuery { Filter = filterExpression, From = from, To = to });
                var engineSort = sort == SortOrder.Ascending ? LogViewer.Engine.SortOrder.Ascending : LogViewer.Engine.SortOrder.Descending;

                var items = index.ReadPage(rows, pageNumber, pageSize, engineSort).Select(e => new LogMessage
                {
                    Timestamp = e.Timestamp,
                    Level = e.Level,
                    MessageTemplateText = e.MessageTemplate,
                    Exception = e.Exception,
                    Properties = e.Properties,
                    RenderedMessage = e.RenderedMessage
                }).ToList();

                return new LogResults
                {
                    Logs = new PagedResult<LogMessage>(rows.Count, pageNumber, pageSize) { Items = items },
                    MessageTemplates = index.GetPatterns(rows).Select(p => new LogTemplate { MessageTemplate = p.Template, Count = (int)p.Count }).ToList()
                };
            });

        public void Dispose()
        {
            _changeTimer.Dispose();
            _watcher.Dispose();
            _index?.Dispose();
        }

        /// <summary>Runs against the open index. If another file was opened in the meantime and this one was closed under us, run it again on the new one.</summary>
        private T With<T>(Func<LogIndex, T> action)
        {
            for (var attempt = 0; ; attempt++)
            {
                var index = _index ?? throw new InvalidOperationException("No logfile has been opened yet");
                try
                {
                    return action(index);
                }
                catch (ObjectDisposedException) when (attempt == 0 && !ReferenceEquals(index, _index))
                {
                }
            }
        }

        private void Watch(string filePath)
        {
            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Path = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
                _watcher.Filter = Path.GetFileName(filePath);
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception)
            {
                // Watching is a convenience (live updates). A folder that cannot be watched must not stop the file opening.
            }
        }

        private void OnFileChanged()
        {
            try
            {
                var index = _index;
                if (index == null)
                    return;

                var generation = index.Generation;

                // The index is brought up to date here, so by the time anyone reloads there is nothing left to read.
                // Tell the client when rows came or the file started over; it does not receive the lines, just the news.
                if (index.Refresh() || index.Generation != generation)
                    _hub.Clients.All.SendAsync("NotifyNewLogEntries");
            }
            catch (Exception)
            {
                // The file may be mid-rotation or briefly locked; the next change event tries again.
            }
        }
    }
}
