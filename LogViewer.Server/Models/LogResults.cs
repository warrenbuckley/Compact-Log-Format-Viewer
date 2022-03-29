using System.Collections.Generic;

namespace LogViewer.Server.Models
{
    public class LogResults
    {
        public PagedResult<LogMessage> Logs { get; set; }

        public List<LogTemplate> MessageTemplates { get; set; }
    }
}
