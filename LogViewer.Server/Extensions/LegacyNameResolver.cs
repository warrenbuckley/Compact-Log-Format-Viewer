using Serilog.Expressions;
using System;
using System.Diagnostics.CodeAnalysis;

namespace LogViewer.Server.Extensions
{

    /// <summary>
    /// Uses Serilog's StaticMemberNameResolver to ensure we get same functionality
    /// Of easily allowing any static methods definied in the passed in class/type
    /// To extend as functions to use for filtering logs such as Has() and any other custom ones
    /// </summary>
    public class LegacyNameResolver : StaticMemberNameResolver
    {
        public LegacyNameResolver(Type type) : base(type)
        {
        }

        public override bool TryResolveBuiltInPropertyName(string alias, [NotNullWhen(true)] out string? target)
        {
            target = alias switch
            {
                "Exception" => "x",
                "Level" => "l",
                "Message" => "m",
                "MessageTemplate" => "mt",
                "Properties" => "p",
                "Timestamp" => "t",
                _ => null
            };

            return target != null;
        }
    }
}
