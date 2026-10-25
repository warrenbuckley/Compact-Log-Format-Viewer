using System.Text.RegularExpressions;
using Serilog.Events;
using Serilog.Expressions;

namespace LogViewer.Engine.Expressions;

/// <summary>
/// A compiled search box entry. <see cref="Properties"/> lists the property names the predicate can look at, so the reader can skip
/// building the rest; null means it may look at any of them. Properties that appear in a line's own message template are always kept,
/// because rendering the message needs them.
/// </summary>
internal sealed record CompiledFilter(Func<LogEvent, bool> Matches, IReadOnlySet<string>? Properties);

/// <summary>
/// Turns the search box text into a predicate, with the same rules the server has always used:
/// one plain word is a case-insensitive message search, anything else is tried as a Serilog expression,
/// and an expression that does not compile falls back to a message search.
/// </summary>
internal static partial class FilterCompiler
{
    private static readonly char[] ExpressionOperators = "()+=*<>%-".ToCharArray();
    private static readonly LegacyNameResolver Resolver = new(typeof(SerilogExtensions));
    private static readonly IReadOnlySet<string> NoProperties = new HashSet<string>();

    /// <returns>null when the text means "no filtering".</returns>
    public static CompiledFilter? Compile(string? filter)
    {
        if (string.IsNullOrEmpty(filter))
            return null;

        if (!filter.Contains(' ') && filter.IndexOfAny(ExpressionOperators) < 0)
            return MessageLike(filter);

        if (SerilogExpression.TryCompile(filter, null, Resolver, out var compiled, out _))
            return new CompiledFilter(evt => ExpressionResult.IsTrue(compiled(evt)), ReferencedProperties(filter));

        return MessageLike(filter);
    }

    /// <summary>
    /// A case-insensitive "contains" on the rendered message, which is what <c>@m like '%text%' ci</c> means.
    /// Done directly because the regular expression behind <c>like</c> costs several times more per event than everything else in the scan.
    /// </summary>
    private static CompiledFilter MessageLike(string text)
        => new(evt => evt.RenderMessage().Contains(text, StringComparison.OrdinalIgnoreCase), NoProperties);

    /// <summary>
    /// Every identifier in the expression text is treated as a possible property name, which over-includes (function names, words in
    /// string literals) but never misses one. An expression that reaches the whole property bag (@p or @Properties) needs everything.
    /// </summary>
    private static IReadOnlySet<string>? ReferencedProperties(string expression)
    {
        if (WholePropertyBag().IsMatch(expression))
            return null;

        return Identifier().Matches(expression).Select(m => m.Value).ToHashSet();
    }

    [GeneratedRegex(@"@p(?![A-Za-z0-9_])|@?Properties(?![A-Za-z0-9_])", RegexOptions.IgnoreCase)]
    private static partial Regex WholePropertyBag();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Identifier();
}
