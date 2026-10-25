using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Serilog.Events;

namespace LogViewer.Engine;

/// <summary>
/// Builds a <see cref="LogEvent"/> straight from the UTF-8 bytes of one CLEF line, producing the same event as
/// Serilog.Formatting.Compact.Reader but without the JObject tree and without re-parsing the message template
/// (the caller supplies the cached template). Anything outside the common shape returns false so the caller can
/// hand the line to Serilog's own reader, which stays the reference for odd input: renderings (@r), trace and span ids,
/// @m without @mt, malformed or non-standard JSON, and values Serilog would reject.
/// An instance keeps a small property-name cache, so use one per thread.
/// </summary>
internal sealed class FastEventReader
{
    private const string TypeTag = "$type";
    private const string UnnamedProperty = "(unnamed)";
    private const int MaxCachedNames = 4096;

    private readonly Dictionary<ulong, (byte[] Bytes, string Name, int Id)> _names = new();

    // When set, only these properties (plus the ones in the line's own template) are built; the rest are skipped without allocating.
    private IReadOnlySet<string>? _wanted;
    private readonly Dictionary<Serilog.Events.MessageTemplate, Needed> _neededByTemplate = new(ReferenceEqualityComparer.Instance);

    private sealed class Needed
    {
        public byte[] State = new byte[64]; // per cached name id: 0 undecided, 1 skip, 2 keep
        public HashSet<string> TemplateNames = new();
    }

    /// <summary>null keeps every property. Otherwise only the named ones, plus whatever each line's template mentions.</summary>
    public void KeepOnly(IReadOnlySet<string>? properties)
    {
        _wanted = properties;
        _neededByTemplate.Clear();
    }

    private enum Field { Property, Timestamp, MessageTemplate, Message, Level, Exception, EventId, Unsupported }

    public bool TryRead(ReadOnlySpan<byte> line, Serilog.Events.MessageTemplate template, out LogEvent evt)
    {
        try
        {
            evt = Read(line, template);
            return true;
        }
        catch (Exception)
        {
            evt = null!;
            return false;
        }
    }

    private LogEvent Read(ReadOnlySpan<byte> line, Serilog.Events.MessageTemplate template)
    {
        var reader = new Utf8JsonReader(line);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new FormatException("not an object");

        var timestamp = default(DateTimeOffset);
        var hasTimestamp = false;
        var hasTemplate = false;
        var level = LogEventLevel.Information;
        Exception? exception = null;
        object? eventId = null;
        var properties = new List<LogEventProperty>(24);
        var needed = _wanted is null ? null : NeededFor(template);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            var field = reader.ValueIsEscaped ? Classify(reader.GetString()!) : Classify(reader.ValueSpan);
            if (field == Field.Property)
            {
                var id = -1;
                var name = reader.ValueIsEscaped ? reader.GetString()! : NameOf(reader.ValueSpan, out id);
                if (name.StartsWith("@@", StringComparison.Ordinal))
                    name = name.Substring(1);

                reader.Read();
                if (needed is not null && !IsKept(needed, id, name))
                {
                    if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                        reader.Skip();

                    continue;
                }

                var value = ReadValue(ref reader);
                properties.Add(new LogEventProperty(LogEventProperty.IsValidName(name) ? name : UnnamedProperty, value));
                continue;
            }

            reader.Read();
            if (field == Field.Unsupported)
                throw new FormatException("needs the reference reader");

            if (reader.TokenType == JsonTokenType.Null)
                continue; // a null reserved field counts as absent

            switch (field)
            {
                case Field.Timestamp:
                    timestamp = ReadTimestamp(ref reader);
                    hasTimestamp = true;
                    break;

                case Field.MessageTemplate:
                    RequireString(ref reader);
                    hasTemplate = true; // the text is already in the supplied template
                    break;

                case Field.Message:
                    RequireString(ref reader);
                    break;

                case Field.Level:
                    level = ReadLevel(ref reader);
                    break;

                case Field.Exception:
                    RequireString(ref reader);
                    exception = new TextException(reader.GetString()!);
                    break;

                case Field.EventId:
                    eventId = reader.TokenType switch
                    {
                        JsonTokenType.String => reader.GetString(),
                        JsonTokenType.Number when reader.TryGetUInt32(out var id) => id,
                        _ => throw new FormatException("event id")
                    };
                    break;
            }
        }

        if (!hasTimestamp || !hasTemplate)
            throw new FormatException("missing @t or @mt");

        if (eventId is not null)
            properties.Add(new LogEventProperty("@i", new ScalarValue(eventId)));

        return new LogEvent(timestamp, level, exception, template, properties);
    }

    private LogEventPropertyValue ReadValue(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return new ScalarValue(null);

            case JsonTokenType.String:
                return new ScalarValue(reader.GetString());

            case JsonTokenType.True:
                return new ScalarValue(true);

            case JsonTokenType.False:
                return new ScalarValue(false);

            case JsonTokenType.Number:
                return new ScalarValue(ReadNumber(ref reader));

            case JsonTokenType.StartArray:
            {
                var items = new List<LogEventPropertyValue>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    items.Add(ReadValue(ref reader));

                return new SequenceValue(items);
            }

            case JsonTokenType.StartObject:
            {
                string? typeTag = null;
                var members = new List<LogEventProperty>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.ValueIsEscaped ? reader.GetString()! : NameOf(reader.ValueSpan, out _);
                    reader.Read();
                    if (name == TypeTag)
                    {
                        if (reader.TokenType == JsonTokenType.String)
                            typeTag = reader.GetString();
                        else if (reader.TokenType == JsonTokenType.Null)
                            typeTag = null;
                        else
                            throw new FormatException("type tag");

                        continue;
                    }

                    members.Add(new LogEventProperty(LogEventProperty.IsValidName(name) ? name : UnnamedProperty, ReadValue(ref reader)));
                }

                return new StructureValue(members, typeTag);
            }

            default:
                throw new FormatException("unexpected token");
        }
    }

    /// <summary>Same value types Newtonsoft produced for the Serilog reader: long (BigInteger when it does not fit) and double.</summary>
    private static object ReadNumber(ref Utf8JsonReader reader)
    {
        var text = reader.ValueSpan;
        if (text.IndexOfAny((byte)'.', (byte)'e', (byte)'E') >= 0)
            return reader.TryGetDouble(out var d) ? d : throw new FormatException("double");

        if (reader.TryGetInt64(out var l))
            return l;

        return BigInteger.Parse(Encoding.UTF8.GetString(text), CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ReadTimestamp(ref Utf8JsonReader reader)
    {
        RequireString(ref reader);

        // ISO 8601 with an explicit Z or numeric offset and at most 7 fractional digits (what Serilog writes) has one meaning
        // everywhere. Anything else goes through DateTimeOffset.TryParse exactly as the Serilog reader does: local time for a bare
        // timestamp, and rounding rather than truncation beyond 7 digits.
        var text = reader.ValueSpan;
        if (!reader.ValueIsEscaped && text.Length > 6
            && (text[^1] == (byte)'Z' || ((text[^6] == (byte)'+' || text[^6] == (byte)'-') && text[^3] == (byte)':'))
            && FractionDigits(text) <= 7
            && reader.TryGetDateTimeOffset(out var stamp))
        {
            return stamp;
        }

        return DateTimeOffset.TryParse(reader.GetString(), out stamp) ? stamp : throw new FormatException("timestamp");
    }

    private static int FractionDigits(ReadOnlySpan<byte> text)
    {
        var dot = text.IndexOf((byte)'.');
        if (dot < 0)
            return 0;

        var digits = 0;
        for (var i = dot + 1; i < text.Length && text[i] is >= (byte)'0' and <= (byte)'9'; i++)
            digits++;

        return digits;
    }

    private static LogEventLevel ReadLevel(ref Utf8JsonReader reader)
    {
        RequireString(ref reader);
        if (!reader.ValueIsEscaped)
        {
            var text = reader.ValueSpan;
            if (System.Text.Ascii.EqualsIgnoreCase(text, "Information"u8)) return LogEventLevel.Information;
            if (System.Text.Ascii.EqualsIgnoreCase(text, "Debug"u8)) return LogEventLevel.Debug;
            if (System.Text.Ascii.EqualsIgnoreCase(text, "Warning"u8)) return LogEventLevel.Warning;
            if (System.Text.Ascii.EqualsIgnoreCase(text, "Error"u8)) return LogEventLevel.Error;
            if (System.Text.Ascii.EqualsIgnoreCase(text, "Verbose"u8)) return LogEventLevel.Verbose;
            if (System.Text.Ascii.EqualsIgnoreCase(text, "Fatal"u8)) return LogEventLevel.Fatal;
        }

        return Enum.TryParse<LogEventLevel>(reader.GetString(), true, out var level) ? level : throw new FormatException("level");
    }

    private static void RequireString(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new FormatException("expected a string");
    }

    private static Field Classify(ReadOnlySpan<byte> name)
    {
        if (name.IsEmpty || name[0] != (byte)'@')
            return Field.Property;

        if (name.SequenceEqual("@t"u8)) return Field.Timestamp;
        if (name.SequenceEqual("@mt"u8)) return Field.MessageTemplate;
        if (name.SequenceEqual("@m"u8)) return Field.Message;
        if (name.SequenceEqual("@l"u8)) return Field.Level;
        if (name.SequenceEqual("@x"u8)) return Field.Exception;
        if (name.SequenceEqual("@i"u8)) return Field.EventId;
        if (name.SequenceEqual("@r"u8) || name.SequenceEqual("@tr"u8) || name.SequenceEqual("@sp"u8)) return Field.Unsupported;
        return Field.Property; // "@@x" and unknown "@x" names are ordinary properties
    }

    private static Field Classify(string name) => name.StartsWith('@') ? Classify(Encoding.UTF8.GetBytes(name)) : Field.Property;

    private Needed NeededFor(Serilog.Events.MessageTemplate template)
    {
        if (!_neededByTemplate.TryGetValue(template, out var needed))
        {
            needed = new Needed();
            foreach (var token in template.Tokens)
                if (token is Serilog.Parsing.PropertyToken property)
                    needed.TemplateNames.Add(property.PropertyName);

            _neededByTemplate.Add(template, needed);
        }

        return needed;
    }

    private bool IsKept(Needed needed, int nameId, string name)
    {
        if (nameId < 0) // not cached (escaped, or the cache is full): decide directly
            return _wanted!.Contains(name) || needed.TemplateNames.Contains(name);

        if (nameId >= needed.State.Length)
            Array.Resize(ref needed.State, Math.Max(nameId + 1, needed.State.Length * 2));

        var state = needed.State[nameId];
        if (state == 0)
        {
            state = _wanted!.Contains(name) || needed.TemplateNames.Contains(name) ? (byte)2 : (byte)1;
            needed.State[nameId] = state;
        }

        return state == 2;
    }

    private string NameOf(ReadOnlySpan<byte> utf8, out int id)
    {
        var key = 14695981039346656037UL;
        foreach (var b in utf8)
        {
            key ^= b;
            key *= 1099511628211UL;
        }

        while (_names.TryGetValue(key, out var hit))
        {
            if (hit.Bytes.AsSpan().SequenceEqual(utf8))
            {
                id = hit.Id;
                return hit.Name;
            }

            key++;
        }

        var name = Encoding.UTF8.GetString(utf8);
        id = -1;
        if (_names.Count < MaxCachedNames)
        {
            id = _names.Count;
            _names.Add(key, (utf8.ToArray(), name, id));
        }

        return name;
    }
}
