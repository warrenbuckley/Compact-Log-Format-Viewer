using System.Text;
using System.Text.RegularExpressions;
using Serilog.Events;

namespace LogViewer.Engine;

/// <summary>
/// Reads the text of a Serilog expression just far enough to say which rows could possibly match it (see <see cref="RowCondition"/>).
/// It never decides what the expression means, only what it cannot match; the real compiled expression always runs on the rows it lets through.
/// </summary>
/// <remarks>
/// What is understood: <c>@Level = 'X'</c>, <c>Has(@Exception)</c> (and the <c>@l</c>, <c>@x</c>, <c>IsDefined</c> spellings),
/// <c>@MessageTemplate = '...'</c> (and <c>@mt</c>), joined by <c>or</c>, <c>and</c> and parentheses.
/// Every other piece means "any row", which is always safe. A piece with a keyword whose precedence this does not model
/// (<c>not</c>, <c>if</c>) at its top level means "any row" for that whole piece, so a mistake about precedence cannot exclude a row.
/// </remarks>
internal static partial class ExpressionPlanner
{
    private static readonly Dictionary<string, LogEventLevel> Levels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Verbose"] = LogEventLevel.Verbose, ["Debug"] = LogEventLevel.Debug, ["Information"] = LogEventLevel.Information,
        ["Warning"] = LogEventLevel.Warning, ["Error"] = LogEventLevel.Error, ["Fatal"] = LogEventLevel.Fatal,
    };

    /// <summary>
    /// The condition, and whether every piece of the text was understood. When it was, the text is made only of the pieces above, none of which
    /// reads an ordinary property, so evaluating it needs none of them built.
    /// </summary>
    public sealed record Planned(RowCondition Condition, bool ReadsNoProperties);

    public static Planned PlanFor(string expression)
    {
        var understood = true;
        var condition = PlanPiece(expression.Trim(), ref understood);
        return new Planned(condition, understood);
    }

    public static RowCondition Plan(string expression) => PlanFor(expression).Condition;

    private static RowCondition PlanPiece(string e, ref bool understood)
    {
        if (e.Length == 0)
        {
            understood = false;
            return RowCondition.All;
        }

        var scan = TopLevel(e);

        // Keywords whose precedence relative to and/or is not modelled here: give up on this piece entirely
        if (scan.HasUnmodelledKeyword)
        {
            understood = false;
            return RowCondition.All;
        }

        if (scan.OrAt.Count > 0)
        {
            var any = new List<RowCondition>();
            foreach (var piece in Split(e, scan.OrAt, 2))
                any.Add(PlanPiece(piece, ref understood));

            return RowCondition.Any(any);
        }

        if (scan.AndAt.Count > 0)
        {
            var every = new List<RowCondition>();
            foreach (var piece in Split(e, scan.AndAt, 3))
                every.Add(PlanPiece(piece, ref understood));

            return RowCondition.Every(every);
        }

        if (e[0] == '(' && scan.CloseOfFirstParen == e.Length - 1)
            return PlanPiece(e[1..^1].Trim(), ref understood);

        var atom = Atom(e);
        if (atom is RowCondition.AllRows)
            understood = false;

        return atom;
    }

    private static RowCondition Atom(string e)
    {
        var level = LevelAtom().Match(e);
        if (level.Success)
            return Levels.TryGetValue(level.Groups[2].Value, out var known) ? new RowCondition.LevelIs(known) : RowCondition.All;

        if (ExceptionAtom().IsMatch(e))
            return new RowCondition.HasException();

        var template = TemplateAtom().Match(e);
        if (template.Success)
            return new RowCondition.TemplateIs(template.Groups[2].Value.Replace("''", "'"));

        return RowCondition.All;
    }

    [GeneratedRegex(@"^(@l|@Level)\s*=\s*'([A-Za-z]+)'$")]
    private static partial Regex LevelAtom();

    [GeneratedRegex(@"^(Has|IsDefined)\(\s*(@x|@Exception)\s*\)$")]
    private static partial Regex ExceptionAtom();

    [GeneratedRegex(@"^(@mt|@MessageTemplate)\s*=\s*'((?:[^']|'')*)'$")]
    private static partial Regex TemplateAtom();

    private static IEnumerable<string> Split(string e, List<int> at, int keywordLength)
    {
        var start = 0;
        foreach (var position in at)
        {
            yield return e[start..position].Trim();
            start = position + keywordLength;
        }

        yield return e[start..].Trim();
    }

    private sealed record TopLevelScan(List<int> OrAt, List<int> AndAt, bool HasUnmodelledKeyword, int CloseOfFirstParen);

    /// <summary>
    /// Finds the keywords <c>or</c> and <c>and</c> at the top level (outside quotes, parentheses, brackets and braces, standing alone as words),
    /// and notes whether <c>not</c> or <c>if</c> appear there. In any capitalisation: Serilog accepts <c>OR</c>, and missing a keyword is NOT safe,
    /// because splitting at <c>and</c> while an unseen lower-precedence <c>OR</c> is sitting between the pieces excludes rows that should match.
    /// (If Serilog did not treat a capitalised word as a keyword the text would not compile, and no narrowing is done for text that does not compile.)
    /// </summary>
    private static TopLevelScan TopLevel(string e)
    {
        var or = new List<int>();
        var and = new List<int>();
        var unmodelled = false;
        var depth = 0;
        var inQuote = false;
        var firstParenClose = -1;
        var firstParenDepth0 = e[0] == '(';

        for (var i = 0; i < e.Length; i++)
        {
            var c = e[i];
            if (c == '\'')
            {
                inQuote = !inQuote; // a doubled '' inside a string toggles out and straight back in
                continue;
            }

            if (inQuote)
                continue;

            if (c is '(' or '[' or '{')
            {
                depth++;
                continue;
            }

            if (c is ')' or ']' or '}')
            {
                depth--;
                if (depth == 0 && c == ')' && firstParenDepth0 && firstParenClose < 0)
                    firstParenClose = i;

                continue;
            }

            if (depth != 0 || !IsWordStart(e, i))
                continue;

            if (Word(e, i, "or")) or.Add(i);
            else if (Word(e, i, "and")) and.Add(i);
            else if (Word(e, i, "not") || Word(e, i, "if")) unmodelled = true;
        }

        // An unbalanced or unterminated expression is not something to be clever about
        if (inQuote || depth != 0)
            return new TopLevelScan(new List<int>(), new List<int>(), true, -1);

        return new TopLevelScan(or, and, unmodelled, firstParenClose);
    }

    private static bool IsWordStart(string e, int i) => i == 0 || !IsWordChar(e[i - 1]);

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '@' or '.' or '$' or '#';

    private static bool Word(string e, int i, string word)
        => string.Compare(e, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0 && (i + word.Length == e.Length || !IsWordChar(e[i + word.Length]));
}
