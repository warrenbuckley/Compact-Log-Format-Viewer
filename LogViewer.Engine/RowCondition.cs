using Serilog.Events;

namespace LogViewer.Engine;

/// <summary>
/// A condition the index can check on a row without reading its line, which every row matching a search expression is guaranteed to satisfy.
/// It is a necessary condition, never a sufficient one: it can let through rows that do not match (the real expression then removes them)
/// but must never exclude a row that does. That is what makes narrowing by it exact. Anything not understood is <see cref="AllRows"/>.
/// </summary>
internal abstract record RowCondition
{
    public static readonly RowCondition All = new AllRows();

    public sealed record AllRows : RowCondition;

    /// <summary>The row has this level. Rows whose @l text the index did not recognise are never excluded.</summary>
    public sealed record LevelIs(LogEventLevel Level) : RowCondition;

    public sealed record HasException : RowCondition;

    /// <summary>The row's message template text is exactly this.</summary>
    public sealed record TemplateIs(string Text) : RowCondition;

    public sealed record AnyOf(IReadOnlyList<RowCondition> Parts) : RowCondition;

    public sealed record AllOf(IReadOnlyList<RowCondition> Parts) : RowCondition;

    public static RowCondition Any(IEnumerable<RowCondition> parts)
    {
        var list = parts.ToList();
        return list.Count == 0 || list.Any(p => p is AllRows) ? All : list.Count == 1 ? list[0] : new AnyOf(list);
    }

    public static RowCondition Every(IEnumerable<RowCondition> parts)
    {
        var list = parts.Where(p => p is not AllRows).ToList();
        return list.Count == 0 ? All : list.Count == 1 ? list[0] : new AllOf(list);
    }
}
