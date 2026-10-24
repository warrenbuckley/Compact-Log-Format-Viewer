using Serilog.Events;
using Serilog.Expressions;

namespace LogViewer.Engine.Expressions;

/// <summary>
/// Turns the search box text into a predicate, with the same rules the server has always used:
/// one plain word is a case-insensitive message search, anything else is tried as a Serilog expression,
/// and an expression that does not compile falls back to a message search.
/// </summary>
internal static class FilterCompiler
{
    private static readonly char[] ExpressionOperators = "()+=*<>%-".ToCharArray();
    private static readonly LegacyNameResolver Resolver = new(typeof(SerilogExtensions));

    /// <returns>null when the text means "no filtering".</returns>
    public static Func<LogEvent, bool>? Compile(string? filter)
    {
        if (string.IsNullOrEmpty(filter))
            return null;

        if (!filter.Contains(' ') && filter.IndexOfAny(ExpressionOperators) < 0)
            return MessageLike(filter);

        if (SerilogExpression.TryCompile(filter, null, Resolver, out var compiled, out _))
            return evt => ExpressionResult.IsTrue(compiled(evt));

        return MessageLike(filter);
    }

    private static Func<LogEvent, bool>? MessageLike(string text)
    {
        var expression = $"@m like '%{SerilogExpression.EscapeLikeExpressionContent(text)}%' ci";
        if (!SerilogExpression.TryCompile(expression, out var compiled, out _))
            return null;

        return evt => ExpressionResult.IsTrue(compiled(evt));
    }
}
