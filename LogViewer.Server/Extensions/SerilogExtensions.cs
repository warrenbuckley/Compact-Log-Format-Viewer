using Serilog.Events;

namespace LogViewer.Server.Extensions
{
    public class SerilogExtensions
    {
        // This Has() code is the same as the renamed IsDefined()
        public static LogEventPropertyValue? Has(LogEventPropertyValue? value)
        {
            return new ScalarValue(value != null);
        }
    }
}
