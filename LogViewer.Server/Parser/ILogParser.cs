using LogViewer.Server.Models;
using Serilog.Core;
using Serilog.Events;
using System.Collections.Generic;

namespace LogViewer.Server
{
    public interface ILogParser
    {
        bool LogIsOpen { get; set; }

        List<LogEvent> ReadLogs(string filePath, Logger logger = null);

        LogLevelCounts TotalCounts();

        int TotalErrors();

        void ExportTextFile(string messageTemplate, string newFileName);
    }
}
