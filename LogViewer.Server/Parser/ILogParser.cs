using LogViewer.Server.Models;
using Serilog.Core;
using Serilog.Events;
using System.Collections.Generic;

namespace LogViewer.Server
{
    public interface ILogParser
    {
        bool LogIsOpen { get; set; }

        string LogFilePath { get; set; }

        List<LogEvent> ReadLogs(string filePath, Logger? logger = null);

        LogLevelCounts TotalCounts();

        int TotalErrors();

        void ExportTextFile(string messageTemplate, string newFileName);

        LogResults Search(int pageNumber = 1, int pageSize = 100, string? filterExpression = null, SortOrder sort = SortOrder.Descending);

        List<LogTemplate> GetMessageTemplates(IEnumerable<LogEvent> logItems);
    }
}
