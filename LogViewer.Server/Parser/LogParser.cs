using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LogViewer.Server.Extensions;
using LogViewer.Server.Hubs;
using LogViewer.Server.Models;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Expressions;
using Serilog.Formatting.Compact.Reader;


namespace LogViewer.Server
{
    public class LogParser : ILogParser
    {
        private List<LogEvent> _logItems;
        private readonly IHubContext<LogHub> _hubContext;
        public string LogFilePath { get; set; }
        public bool LogIsOpen { get; set; }

        private const string ExpressionOperators = "()+=*<>%-";

        public LogParser(IHubContext<LogHub> hubContext)
        {
            _logItems = new List<LogEvent>();
            _hubContext = hubContext;

            LogFilePath = string.Empty;
            LogIsOpen = false;
        }
        
        public List<LogEvent> ReadLogs(string filePath, Logger? logger = null)
        {
            var logItems = new List<LogEvent>();

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (var stream = new StreamReader(fs))
                {
                    using (var reader = new LogEventReader(stream))
                    {
                        while (TryRead(reader, out var evt))
                        {
                            if (evt == null)
                                continue;

                            if (logger != null)
                            {
                                //We can persist the log item (using the passed in Serilog config)
                                //In this case a Logger with File Sink setup
                                logger.Write(evt);
                            }

                            logItems.Add(evt);
                        }
                    }
                }
            }

            _logItems = logItems;
            LogFilePath = filePath;
            LogIsOpen = true;
            
            var fileWatcher = new FileSystemWatcher();
            fileWatcher.Path =  Path.GetDirectoryName(LogFilePath);
            fileWatcher.Filter = Path.GetFileName(LogFilePath);
            fileWatcher.EnableRaisingEvents = true;
        
            
            fileWatcher.Changed += async (sender, args) =>
            {
                // Notify user that new entries has occured
                // We don't pass back the new log lines, but rather just notify the client that new lines has been added
                //await Clients.All.SendAsync("NotifyNewLogEntries");
                _hubContext.Clients.All.SendAsync("NotifyNewLogEntries");
            };

            return _logItems;
        }

        public LogLevelCounts TotalCounts()
        {
            var counts = new LogLevelCounts
            {
                Verbose = _logItems.Count(log => log.Level == LogEventLevel.Verbose),
                Information = _logItems.Count(log => log.Level == LogEventLevel.Information),
                Debug = _logItems.Count(log => log.Level == LogEventLevel.Debug),
                Warning = _logItems.Count(log => log.Level == LogEventLevel.Warning),
                Error = _logItems.Count(log => log.Level == LogEventLevel.Error),
                Fatal = _logItems.Count(log => log.Level == LogEventLevel.Fatal)
            };

            return counts;
        }

        public int TotalErrors()
        {
            return _logItems.Count(log => log.Level == LogEventLevel.Fatal || log.Level == LogEventLevel.Error || log.Exception != null);
        }

        public void ExportTextFile(string messageTemplate, string newFileName)
        {
            if (string.IsNullOrEmpty(messageTemplate))
                messageTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss,fff} [P{ProcessId}/D{AppDomainId}/T{ThreadId}] {Level:u3}  {SourceContext} - {Message:lj}{NewLine}{Exception}";

            var loggerConfig = new LoggerConfiguration()
                .WriteTo.File(newFileName, outputTemplate: messageTemplate);

            //We will need to re-read logs & pass in a Serilog that can persist to TXT file
            using (var logger = loggerConfig.CreateLogger())
            {
                ReadLogs(LogFilePath, logger);
            }
        }

        

        public LogResults Search(int pageNumber = 1, int pageSize = 100, string? filterExpression = null, SortOrder sort = SortOrder.Descending)
        {
            //If filter null - return a simple page of results
            if(filterExpression == null)
            {
                var totalRecords = _logItems.Count;
                var logMessages = _logItems
                    .OrderBy(x => x.Timestamp, sort)
                    .Skip(pageSize * (pageNumber - 1))
                    .Take(pageSize)
                    .Select(x => new LogMessage
                    {
                        Timestamp = x.Timestamp,
                        Level = x.Level,
                        MessageTemplateText = x.MessageTemplate.Text,
                        Exception = x.Exception?.ToString(),
                        Properties = x.Properties,
                        RenderedMessage = x.RenderMessage()
                    });

                return new LogResults()
                {
                    Logs = new PagedResult<LogMessage>(totalRecords, pageNumber, pageSize)
                    {
                        Items = logMessages
                    },
                    MessageTemplates = GetMessageTemplates(_logItems)
                };
            }


            Func<LogEvent, bool> ?filter;

            // Our custom Serilog Functions in this case plugging the gap for missing Has() function
            var customSerilogFunctions = new LegacyNameResolver(typeof(SerilogExtensions));

            // With an empty expression - ensure all logs are sent back
            if (filterExpression == string.Empty){
                filter = evt =>
                {
                    // Return true/matches
                    return true;
                };
            }
            // If the expression is one word and doesn't contain a serilog operator then we can perform a like search
            else if (!filterExpression.Contains(" ") && !filterExpression.ContainsAny(ExpressionOperators))
            {
                filter = PerformMessageLikeFilter(filterExpression);
            }
            else 
            {
                // Check if it's a valid expression
                // If the expression evaluates then make it into a filter
                if (SerilogExpression.TryCompile(filterExpression, null, customSerilogFunctions, out var compiled, out var error))
                {
                    // `compiled` is a function that can be executed against `LogEvent`s:
                    filter = evt =>
                    {
                        var result = compiled(evt);
                        return ExpressionResult.IsTrue(result);
                    };
                }
                else
                {
                    // `error` describes a syntax error
                    // Couldn't compile an expression

                    //Assume the expression was a search string and make a Like filter from that
                    filter = PerformMessageLikeFilter(filterExpression);
                }
            }

            //Apply the filter to the collection
            var filteredLogs = filter is not null ? _logItems.Where(filter) : _logItems;
            var filteredTotal = filteredLogs.Count();
            var logItems = filteredLogs
                    .OrderBy(x => x.Timestamp, sort)
                    .Skip(pageSize * (pageNumber - 1))
                    .Take(pageSize)
                    .Select(x => new LogMessage
                    {
                        Timestamp = x.Timestamp,
                        Level = x.Level,
                        MessageTemplateText = x.MessageTemplate.Text,
                        Exception = x.Exception?.ToString(),
                        Properties = x.Properties,
                        RenderedMessage = x.RenderMessage()
                    });

            return new LogResults()
            {
                Logs = new PagedResult<LogMessage>(filteredTotal, pageNumber, pageSize)
                {
                    Items = logItems
                },
                MessageTemplates = GetMessageTemplates(filteredLogs)
            };
        }

        public List<LogTemplate> GetMessageTemplates(IEnumerable<LogEvent> logItems)
        {
            var templates = logItems
                .GroupBy(log => log.MessageTemplate.Text)
                .Select(x => new LogTemplate
                {
                    MessageTemplate = x.Key,
                    Count = x.Count()
                })
                .OrderByDescending(x => x.Count);

            return templates.ToList();
        }

        private Func<LogEvent, bool>? PerformMessageLikeFilter(string filterExpression)
        {
            var filterSearch = $"@m like '%{SerilogExpression.EscapeLikeExpressionContent(filterExpression)}%' ci";
            if (SerilogExpression.TryCompile(filterSearch, out var compiled, out var error))
            {
                // `compiled` is a function that can be executed against `LogEvent`s:
                return evt =>
                {
                    var result = compiled(evt);
                    return ExpressionResult.IsTrue(result);
                };
            }

            return null;
        }

        private bool TryRead(LogEventReader reader, out LogEvent? evt)
        {
            try
            {
                return reader.TryRead(out evt);
            }
            catch (JsonReaderException)
            {
                evt = null;
                return true;
            }
        }
    }
}
