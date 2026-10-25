namespace LogViewer.Engine;

/// <summary>Settings for <see cref="LogIndex"/> that depend on the application that wrote the log, not on the log format.</summary>
public sealed class LogIndexOptions
{
    /// <summary>Most correlation properties one index will track. Each costs 4 bytes per row, and only once a row actually has a value.</summary>
    public const int MaxCorrelationProperties = 8;

    /// <summary>
    /// Names of ordinary properties whose value identifies a request, operation or session, so the lines belonging together can be followed.
    /// Each name gets its own column and its own set of values, so they are never merged: lines of one request often carry different ones
    /// (in Umbraco, ASP.NET Core's <c>RequestId</c> is on every line of a request but <c>HttpRequestId</c> is missing from the first and last).
    /// The trace id written by Serilog (<c>@tr</c>) is built in and is not listed here. Names are exact and case-sensitive.
    /// Which of them to offer is a question for <see cref="IndexStats.Correlations"/>, which says how many lines each one covers.
    /// </summary>
    public IReadOnlyList<string> CorrelationProperties { get; init; } = new[] { "HttpRequestId", "RequestId", "CorrelationId", "TraceId" };

    internal void Validate()
    {
        if (CorrelationProperties.Count > MaxCorrelationProperties)
            throw new ArgumentException($"At most {MaxCorrelationProperties} correlation properties can be indexed.", nameof(CorrelationProperties));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in CorrelationProperties)
        {
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith('@'))
                throw new ArgumentException($"'{name}' is not an ordinary property name (the CLEF fields that start with @ are read as themselves).", nameof(CorrelationProperties));

            if (!seen.Add(name))
                throw new ArgumentException($"'{name}' is listed twice.", nameof(CorrelationProperties));
        }
    }
}
