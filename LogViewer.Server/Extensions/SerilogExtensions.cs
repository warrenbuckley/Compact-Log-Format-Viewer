using Serilog.Events;

namespace LogViewer.Server.Extensions
{
    public class SerilogExtensions
    {
        static readonly LogEventPropertyValue ConstantTrue = new ScalarValue(true);
        static readonly LogEventPropertyValue ConstantFalse = new ScalarValue(false);

        // This Has() code is the same as the renamed IsDefined()
        public static LogEventPropertyValue? Has(LogEventPropertyValue? value)
        {
            return ScalarBoolean(value != null);
        }

        private static LogEventPropertyValue ScalarBoolean(bool value)
        {
            return value ? ConstantTrue : ConstantFalse;
        }
    }
}
