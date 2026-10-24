using Serilog.Events;

namespace LogViewer.Engine.Expressions;

public class SerilogExtensions
{
    // Same as the renamed IsDefined()
    public static LogEventPropertyValue? Has(LogEventPropertyValue? value)
    {
        return new ScalarValue(value != null);
    }
}
