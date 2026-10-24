using System.Diagnostics.CodeAnalysis;
using Serilog.Expressions;

namespace LogViewer.Engine.Expressions;

/// <summary>
/// Same behaviour as LogViewer.Server's resolver: static methods on the supplied type become
/// filter functions (Has() etc.) and the legacy @Level/@Message style aliases keep working.
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
