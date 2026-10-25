using System.Globalization;

namespace LogViewer.Engine;

/// <summary>What the search box turned out to mean: a time range taken from typed tokens, and the text left over for the Serilog filter.</summary>
/// <param name="Filter">The remaining text, exactly as typed when no token was recognised. Pass it to <see cref="LogQuery.Filter"/>.</param>
/// <param name="From">Inclusive.</param>
/// <param name="To">Exclusive.</param>
/// <param name="Tokens">The tokens that were taken out of the text, as typed.</param>
public sealed record ParsedSearch(string? Filter, DateTimeOffset? From, DateTimeOffset? To, IReadOnlyList<string> Tokens);

/// <summary>
/// Reads time tokens typed into the search box, so a time range can be typed as well as chosen with the histogram, the presets or the API.
/// Both end up as the same index time range (<see cref="LogQuery.From"/> and <see cref="LogQuery.To"/>).
/// <list type="bullet">
/// <item><c>last:15m</c> counts back from the <b>newest entry in the file</b> (not the clock, so an old file still works); units s, m, h, d, w.</item>
/// <item><c>from:</c> is inclusive and <c>to:</c> is exclusive. A time with no offset is <b>UTC</b>; an explicit <c>Z</c> or <c>+01:00</c> is honoured.
/// Accepted: <c>2024-05-01T14:00</c>, <c>2024-05-01T14:00:30.5Z</c>, <c>2024-05-01</c> (a date alone: <c>from:</c> is midnight, <c>to:</c> is the end of that day)
/// and <c>14:00</c> or <c>14:00:30</c> (on the date of the newest entry).</item>
/// </list>
/// Tokens are only recognised at the start or the end of the text, outside single quotes, and only when the whole value is understood. Anything else,
/// including a message that happens to contain "from:localhost", is left alone, so text that has no tokens searches exactly as it always did.
/// Only the first token of each kind is taken; a second <c>from:</c> stays in the text. An explicit <c>from:</c> or <c>to:</c> replaces that end of <c>last:</c>.
/// </summary>
public static class SearchText
{
    private static readonly string[] DateTimeFormats =
    {
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mmK",
    };

    private static readonly string[] TimeFormats = { "HH:mm", "HH:mm:ss", "HH:mm:ss.FFFFFFF" };

    public static ParsedSearch Parse(string? text, DateTimeOffset newestEntry)
    {
        if (string.IsNullOrEmpty(text))
            return new ParsedSearch(text, null, null, Array.Empty<string>());

        var words = Split(text);
        var state = new State(newestEntry.ToUniversalTime());
        int first = 0, last = words.Count; // words[first..last) are still text

        while (first < last && state.TryTake(text.Substring(words[first].Start, words[first].End - words[first].Start)))
            first++;

        while (last > first && state.TryTake(text.Substring(words[last - 1].Start, words[last - 1].End - words[last - 1].Start)))
            last--;

        if (state.Tokens.Count == 0)
            return new ParsedSearch(text, null, null, Array.Empty<string>());

        var filter = first < last ? text.Substring(words[first].Start, words[last - 1].End - words[first].Start) : "";
        return new ParsedSearch(filter, state.From, state.To, state.Tokens);
    }

    /// <summary>Whitespace separated words, where whitespace inside single quotes does not separate (Serilog expression strings).</summary>
    private static List<(int Start, int End)> Split(string text)
    {
        var words = new List<(int, int)>();
        var inQuote = false;
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\'')
            {
                inQuote = !inQuote;
                if (start < 0)
                    start = i;

                continue;
            }

            if (!inQuote && char.IsWhiteSpace(c))
            {
                if (start >= 0)
                {
                    words.Add((start, i));
                    start = -1;
                }

                continue;
            }

            if (start < 0)
                start = i;
        }

        if (start >= 0)
            words.Add((start, text.Length));

        return words;
    }

    private sealed class State
    {
        private readonly DateTimeOffset _newest;
        private TimeSpan? _last;
        private DateTimeOffset? _from, _to;
        private bool _gotLast, _gotFrom, _gotTo;

        public State(DateTimeOffset newest) => _newest = newest;

        public List<string> Tokens { get; } = new();

        public DateTimeOffset? From => _from ?? (_last is { } l ? Subtract(_newest, l) : null);

        // To is exclusive, so "up to the newest entry" ends one tick after it
        public DateTimeOffset? To => _to ?? (_last is not null ? _newest.AddTicks(1) : null);

        public bool TryTake(string word)
        {
            var colon = word.IndexOf(':');
            if (colon <= 0)
                return false;

            var key = word[..colon];
            var value = word[(colon + 1)..];

            if (key.Equals("last", StringComparison.OrdinalIgnoreCase) && !_gotLast && TryParseSpan(value, out var span))
            {
                _last = span;
                _gotLast = true;
            }
            else if (key.Equals("from", StringComparison.OrdinalIgnoreCase) && !_gotFrom && TryParseInstant(value, _newest, false, out var from))
            {
                _from = from;
                _gotFrom = true;
            }
            else if (key.Equals("to", StringComparison.OrdinalIgnoreCase) && !_gotTo && TryParseInstant(value, _newest, true, out var to))
            {
                _to = to;
                _gotTo = true;
            }
            else
            {
                return false;
            }

            Tokens.Add(word);
            return true;
        }

        private static DateTimeOffset Subtract(DateTimeOffset from, TimeSpan span)
            => from.UtcTicks - span.Ticks < DateTimeOffset.MinValue.UtcTicks ? DateTimeOffset.MinValue : from - span;
    }

    private static bool TryParseSpan(string value, out TimeSpan span)
    {
        span = default;
        if (value.Length < 2 || value.Length > 7 || !value[..^1].All(char.IsAsciiDigit))
            return false;

        var amount = long.Parse(value[..^1], CultureInfo.InvariantCulture);
        if (amount < 1)
            return false;

        var unit = char.ToLowerInvariant(value[^1]) switch
        {
            's' => TimeSpan.TicksPerSecond,
            'm' => TimeSpan.TicksPerMinute,
            'h' => TimeSpan.TicksPerHour,
            'd' => TimeSpan.TicksPerDay,
            'w' => TimeSpan.TicksPerDay * 7,
            _ => 0L
        };

        if (unit == 0 || amount > TimeSpan.MaxValue.Ticks / unit)
            return false;

        span = TimeSpan.FromTicks(amount * unit);
        return true;
    }

    private static bool TryParseInstant(string value, DateTimeOffset newest, bool isUpperBound, out DateTimeOffset instant)
    {
        const DateTimeStyles Styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        var culture = CultureInfo.InvariantCulture;

        if (DateTimeOffset.TryParseExact(value, DateTimeFormats, culture, Styles, out instant))
            return true;

        // A date alone: the start of that day, or for "to" the whole day (up to, not including, the next midnight)
        if (value.Length == 10 && DateTimeOffset.TryParseExact(value, "yyyy-MM-dd", culture, Styles, out instant))
        {
            if (isUpperBound)
                instant = instant.AddDays(1);

            return true;
        }

        // A time alone: on the date of the newest entry
        if (TimeOnly.TryParseExact(value, TimeFormats, culture, DateTimeStyles.None, out var time))
        {
            instant = new DateTimeOffset(DateOnly.FromDateTime(newest.UtcDateTime).ToDateTime(time), TimeSpan.Zero);
            return true;
        }

        instant = default;
        return false;
    }
}
