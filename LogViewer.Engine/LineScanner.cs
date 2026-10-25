using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace LogViewer.Engine;

/// <summary>
/// Walks a byte range of a CLEF file once, pulling out only the handful of fields the index needs
/// (@t, @l, @mt, @x and the request id) with Utf8JsonReader, so no LogEvent or string is built per line.
/// </summary>
internal static class LineScanner
{
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

    private enum Field { Other, Timestamp, Level, Template, Message, Exception, Trace }

    public static void ScanRange(SafeFileHandle handle, long start, long end, ChunkResult result, byte[][] correlationNames, CancellationToken ct, Action<int>? onBytesRead)
    {
        var buf = new byte[1 << 20];
        var have = 0;
        var bufOffset = start; // file offset of buf[0]
        var filePos = start;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (filePos < end)
            {
                var want = (int)Math.Min(buf.Length - have, end - filePos);
                var read = RandomAccess.Read(handle, buf.AsSpan(have, want), filePos);
                if (read == 0)
                {
                    end = filePos; // file shrank underneath us
                }
                else
                {
                    have += read;
                    filePos += read;
                    onBytesRead?.Invoke(read);
                }
            }

            var pos = 0;
            while (true)
            {
                var nl = buf.AsSpan(pos, have - pos).IndexOf((byte)'\n');
                if (nl < 0)
                    break;

                AddRaw(buf.AsSpan(pos, nl), bufOffset + pos, result, correlationNames);
                pos += nl + 1;
            }

            if (filePos >= end)
            {
                if (have - pos > 0)
                    AddRaw(buf.AsSpan(pos, have - pos), bufOffset + pos, result, correlationNames);

                break;
            }

            bufOffset += pos;
            have -= pos;
            if (have > 0 && pos > 0)
                Buffer.BlockCopy(buf, pos, buf, 0, have);

            if (have == buf.Length) // a single line longer than the buffer
                Array.Resize(ref buf, buf.Length * 2);
        }
    }

    private static void AddRaw(ReadOnlySpan<byte> line, long offset, ChunkResult r, byte[][] correlationNames)
    {
        if (line.Length > 0 && line[^1] == (byte)'\r')
            line = line[..^1];

        if (offset == 0 && line.StartsWith(Bom))
        {
            line = line[Bom.Length..];
            offset = Bom.Length;
        }

        if (line.IndexOfAnyExcept((byte)' ', (byte)'\t') < 0)
            return; // blank line

        AddLine(line, offset, r, correlationNames);
    }

    private static void AddLine(ReadOnlySpan<byte> line, long offset, ChunkResult r, byte[][] correlationNames)
    {
        long ticks = 0;
        var hasTicks = false;
        byte level = 2; // CLEF omits @l for Information
        var unknownLevel = false;
        var hasException = false;
        var template = -1;
        var trace = -1;
        Span<int> correlation = stackalloc int[correlationNames.Length];
        correlation.Fill(-1);
        byte[]? message = null;

        try
        {
            var reader = new Utf8JsonReader(line);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                r.Malformed++;
                return;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                var field = Classify(reader.ValueSpan, reader.ValueIsEscaped);
                var column = field == Field.Other && !reader.ValueIsEscaped ? ColumnOf(correlationNames, reader.ValueSpan) : -1;
                reader.Read();

                switch (field)
                {
                    case Field.Timestamp when reader.TokenType == JsonTokenType.String:
                        if (!reader.TryGetDateTimeOffset(out var stamp)
                            && !DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out stamp))
                        {
                            r.Malformed++;
                            return;
                        }

                        ticks = stamp.UtcTicks;
                        hasTicks = true;
                        break;

                    case Field.Level when reader.TokenType == JsonTokenType.String:
                        level = ParseLevel(reader.ValueIsEscaped ? Encoding.UTF8.GetBytes(reader.GetString()!) : reader.ValueSpan, out var recognised);
                        unknownLevel = !recognised;
                        break;

                    case Field.Template when reader.TokenType == JsonTokenType.String:
                        template = r.TemplateTable.Intern(reader.ValueIsEscaped ? Encoding.UTF8.GetBytes(reader.GetString()!) : reader.ValueSpan);
                        break;

                    case Field.Message when reader.TokenType == JsonTokenType.String && template < 0:
                        message = reader.ValueIsEscaped ? Encoding.UTF8.GetBytes(reader.GetString()!) : reader.ValueSpan.ToArray();
                        break;

                    case Field.Exception when reader.TokenType != JsonTokenType.Null:
                        hasException = true;
                        break;

                    case Field.Trace when reader.TokenType == JsonTokenType.String:
                        trace = r.TraceTable.Intern(reader.ValueIsEscaped ? Encoding.UTF8.GetBytes(reader.GetString()!) : reader.ValueSpan);
                        break;

                    case Field.Other when column >= 0 && reader.TokenType is JsonTokenType.String or JsonTokenType.Number:
                        // a correlation property: its own column and its own set of values, never merged with another property's
                        correlation[column] = r.CorrelationTables[column].Intern(
                            reader.TokenType == JsonTokenType.String && reader.ValueIsEscaped ? Encoding.UTF8.GetBytes(reader.GetString()!) : reader.ValueSpan);
                        break;
                }

                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    reader.Skip();
            }
        }
        catch (JsonException)
        {
            r.Malformed++;
            return;
        }

        if (!hasTicks)
        {
            r.Malformed++;
            return;
        }

        if (template < 0)
            template = r.TemplateTable.Intern(message is null ? ReadOnlySpan<byte>.Empty : EscapeBraces(message));

        if (r.Ticks.Count > 0 && ticks < r.Ticks[^1])
            r.Chronological = false;

        r.Offsets.Add(offset);
        r.Lengths.Add(line.Length);
        r.Ticks.Add(ticks);
        r.Levels.Add((byte)(level | (hasException ? LevelByte.HasException : 0) | (unknownLevel ? LevelByte.UnknownLevel : 0)));
        r.Templates.Add(template);
        r.Traces.Add(trace);
        for (var i = 0; i < correlation.Length; i++)
            r.Correlations[i].Add(correlation[i]);
    }

    /// <summary>A line with only @m has no template; Serilog turns the rendered text into a literal template by doubling braces.</summary>
    private static byte[] EscapeBraces(byte[] message)
        => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(message).Replace("{", "{{").Replace("}", "}}"));

    private static Field Classify(ReadOnlySpan<byte> name, bool escaped)
    {
        if (escaped)
            return Field.Other;

        if (name.SequenceEqual("@t"u8)) return Field.Timestamp;
        if (name.SequenceEqual("@l"u8)) return Field.Level;
        if (name.SequenceEqual("@mt"u8)) return Field.Template;
        if (name.SequenceEqual("@m"u8)) return Field.Message;
        if (name.SequenceEqual("@x"u8)) return Field.Exception;
        if (name.SequenceEqual("@tr"u8)) return Field.Trace;
        return Field.Other;
    }

    private static int ColumnOf(byte[][] names, ReadOnlySpan<byte> name)
    {
        for (var i = 0; i < names.Length; i++)
            if (names[i].Length == name.Length && name.SequenceEqual(names[i]))
                return i;

        return -1;
    }

    /// <summary>The six level names, case-insensitively, as Serilog's reader accepts them. Anything else is flagged so the row is never excluded by its level.</summary>
    private static byte ParseLevel(ReadOnlySpan<byte> value, out bool recognised)
    {
        recognised = true;
        if (System.Text.Ascii.EqualsIgnoreCase(value, "Information"u8)) return 2;
        if (System.Text.Ascii.EqualsIgnoreCase(value, "Debug"u8)) return 1;
        if (System.Text.Ascii.EqualsIgnoreCase(value, "Warning"u8)) return 3;
        if (System.Text.Ascii.EqualsIgnoreCase(value, "Error"u8)) return 4;
        if (System.Text.Ascii.EqualsIgnoreCase(value, "Verbose"u8)) return 0;
        if (System.Text.Ascii.EqualsIgnoreCase(value, "Fatal"u8)) return 5;

        recognised = false;
        return 2;
    }
}
