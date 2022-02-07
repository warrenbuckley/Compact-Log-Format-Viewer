using Serilog.Events;
using System;

namespace LogViewer.Server.Extensions
{
    public class SerilogExtensions
    {
        public static LogEventPropertyValue? Has(StringComparison comparison, LogEventPropertyValue? input)
        {
            // Comes in null if its not a property we have
            // The input in the Has() is checking to see if that property exists
            if(input != null)
            {
                // But if user does Has('SomeString')
                // It would be a weird use, as its not checking a property existance
                // but just some random string. It would still return as true though
                return new ScalarValue(true);
            }

            return null;
        }

    }
}
