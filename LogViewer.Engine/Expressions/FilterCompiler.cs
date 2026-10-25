using Serilog.Events;
using Serilog.Expressions;

namespace LogViewer.Engine.Expressions;

/// <summary>
/// A compiled search box entry. <see cref="Properties"/> lists the only properties the predicate can look at, so the reader can skip
/// building the rest; null means it may look at any of them. Properties that appear in a line's own message template are always kept,
/// because rendering the message needs them. <see cref="Condition"/>, when there is one, is what the index can say about which rows could possibly match,
/// so most rows need not be read at all; the predicate still decides, so the result does not depend on it being a good guess.
/// </summary>
internal sealed record CompiledFilter(Func<LogEvent, bool> Matches, IReadOnlySet<string>? Properties, RowCondition? Condition = null);

/// <summary>
/// Turns the search box text into a predicate with exactly the rules LogViewer.Server's LogParser has always used, and runs it through
/// Serilog.Expressions itself, so a search returns the same rows it always did:
/// one plain word is the expression <c>@m like '%word%' ci</c>, anything else is tried as a Serilog expression (with the same
/// legacy aliases and Has()), and an expression that does not compile is treated as a plain message search.
/// </summary>
internal static class FilterCompiler
{
    private const string ExpressionOperators = "()+=*<>%-";
    private static readonly LegacyNameResolver Resolver = new(typeof(SerilogExtensions));

    /// <returns>null when the text means "no filtering", including when even the fallback search cannot be compiled, as before.</returns>
    public static CompiledFilter? Compile(string? filter)
    {
        if (string.IsNullOrEmpty(filter))
            return null;

        if (!filter.Contains(" ") && !ContainsAny(filter, ExpressionOperators))
            return MessageLike(filter);

        if (SerilogExpression.TryCompile(filter, null, Resolver, out var compiled, out _))
        {
            // A typed expression can reach any property, so every property is built, unless every piece of it was understood
            // by the planner (level, exception and template tests only), in which case it cannot read an ordinary property at all.
            var planned = ExpressionPlanner.PlanFor(filter);
            return new CompiledFilter(
                evt => ExpressionResult.IsTrue(compiled(evt)),
                planned.ReadsNoProperties ? NoProperties : null,
                planned.Condition is RowCondition.AllRows ? null : planned.Condition);
        }

        return MessageLike(filter);
    }

    private static CompiledFilter? MessageLike(string text)
    {
        var expression = $"@m like '%{SerilogExpression.EscapeLikeExpressionContent(text)}%' ci";
        if (!SerilogExpression.TryCompile(expression, out var compiled, out _))
            return null;

        // This expression only reads @m, which is the message template rendered with the template's own properties,
        // so no other property can change its result.
        return new CompiledFilter(evt => ExpressionResult.IsTrue(compiled(evt)), NoProperties);
    }

    private static readonly IReadOnlySet<string> NoProperties = new HashSet<string>();

    private static bool ContainsAny(string text, string characters) => text.IndexOfAny(characters.ToCharArray()) >= 0;
}
