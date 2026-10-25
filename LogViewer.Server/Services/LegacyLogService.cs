using System;
using System.Linq;
using LogViewer.Server.Models;

namespace LogViewer.Server.Services
{
    /// <summary>The original behaviour: every line is parsed into memory on open, and every reload parses the whole file again.</summary>
    public class LegacyLogService : ILogService
    {
        private readonly ILogParser _parser;

        public LegacyLogService(ILogParser parser) => _parser = parser;

        public bool LogIsOpen => _parser.LogIsOpen;

        public string LogFilePath => _parser.LogFilePath;

        public int Open(string filePath) => _parser.ReadLogs(filePath).Count;

        public int? Reload() => string.IsNullOrEmpty(_parser.LogFilePath) ? null : _parser.ReadLogs(_parser.LogFilePath).Count;

        public LogLevelCounts TotalCounts() => _parser.TotalCounts();

        public int TotalErrors() => _parser.TotalErrors();

        public void ExportTextFile(string messageTemplate, string newFileName) => _parser.ExportTextFile(messageTemplate, newFileName);

        public LogResults Search(int pageNumber = 1, int pageSize = 100, string? filterExpression = null, SortOrder sort = SortOrder.Descending, DateTimeOffset? from = null, DateTimeOffset? to = null)
        {
            if (from is not null || to is not null)
                throw new NotSupportedException("A time range needs the indexed log reader. Unset the LOGVIEWER_PARSER=legacy setting to use it.");

            return _parser.Search(pageNumber, pageSize, filterExpression, sort);
        }
    }
}
