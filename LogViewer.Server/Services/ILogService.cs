using System;
using LogViewer.Server.Models;

namespace LogViewer.Server.Services
{
    /// <summary>
    /// What the API controller needs from whatever is reading the log. Two implementations exist so the original in-memory parser stays
    /// available as a fallback: <see cref="IndexedLogService"/> (the default) and <see cref="LegacyLogService"/>.
    /// </summary>
    public interface ILogService
    {
        bool LogIsOpen { get; }

        string LogFilePath { get; }

        /// <returns>The number of log entries.</returns>
        /// <exception cref="System.IO.InvalidDataException">The file is not a readable compact JSON log.</exception>
        int Open(string filePath);

        /// <summary>Picks up what changed in the open file. Null when no file is open.</summary>
        int? Reload();

        LogLevelCounts TotalCounts();

        int TotalErrors();

        void ExportTextFile(string messageTemplate, string newFileName);

        /// <param name="from">Inclusive. Only the indexed implementation supports a time range.</param>
        /// <param name="to">Exclusive.</param>
        LogResults Search(int pageNumber = 1, int pageSize = 100, string? filterExpression = null, SortOrder sort = SortOrder.Descending, DateTimeOffset? from = null, DateTimeOffset? to = null);
    }
}
