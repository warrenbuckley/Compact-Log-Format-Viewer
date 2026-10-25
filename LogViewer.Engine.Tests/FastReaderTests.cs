using System.Text;
using LogViewer.Engine.Expressions;
using Serilog.Events;
using Serilog.Expressions;
using Serilog.Formatting.Compact.Reader;
using Serilog.Parsing;

namespace LogViewer.Engine.Tests;

/// <summary>
/// The fast reader must produce exactly what Serilog's reader produces, or decline the line so the caller falls back.
/// Serilog's LogEventReader is the oracle for every check here.
/// </summary>
public class FastReaderTests
{
    private static readonly MessageTemplateParser Parser = new();

    internal static LogEvent? Reference(string line)
    {
        try { return LogEventReader.ReadFromString(line); }
        catch (Exception) { return null; }
    }

    internal static void AssertSame(LogEvent expected, LogEvent actual, string context)
    {
        Assert.AreEqual(expected.Timestamp, actual.Timestamp, context + " timestamp");
        Assert.AreEqual(expected.Timestamp.Offset, actual.Timestamp.Offset, context + " offset");
        Assert.AreEqual(expected.Level, actual.Level, context + " level");
        Assert.AreEqual(expected.TraceId, actual.TraceId, context + " trace id");
        Assert.AreEqual(expected.SpanId, actual.SpanId, context + " span id");
        Assert.AreEqual(expected.MessageTemplate.Text, actual.MessageTemplate.Text, context + " template");
        Assert.AreEqual(expected.Exception?.ToString(), actual.Exception?.ToString(), context + " exception");
        Assert.AreEqual(expected.RenderMessage(), actual.RenderMessage(), context + " rendered message");
        CollectionAssert.AreEquivalent(expected.Properties.Keys, actual.Properties.Keys, context + " property names");
        foreach (var (name, value) in expected.Properties)
            AssertSame(value, actual.Properties[name], $"{context} / {name}");
    }

    private static void AssertSame(LogEventPropertyValue expected, LogEventPropertyValue actual, string context)
    {
        Assert.AreEqual(expected.GetType(), actual.GetType(), context + " value kind");
        switch (expected)
        {
            case ScalarValue s:
                var a = (ScalarValue)actual;
                Assert.AreEqual(s.Value?.GetType(), a.Value?.GetType(), context + " scalar type");
                Assert.AreEqual(s.Value, a.Value, context + " scalar");
                break;
            case SequenceValue seq:
                var aseq = (SequenceValue)actual;
                Assert.AreEqual(seq.Elements.Count, aseq.Elements.Count, context + " length");
                for (var i = 0; i < seq.Elements.Count; i++)
                    AssertSame(seq.Elements[i], aseq.Elements[i], $"{context}[{i}]");
                break;
            case StructureValue st:
                var ast = (StructureValue)actual;
                Assert.AreEqual(st.TypeTag, ast.TypeTag, context + " type tag");
                Assert.AreEqual(st.Properties.Count, ast.Properties.Count, context + " member count");
                for (var i = 0; i < st.Properties.Count; i++)
                {
                    Assert.AreEqual(st.Properties[i].Name, ast.Properties[i].Name, context + " member name");
                    AssertSame(st.Properties[i].Value, ast.Properties[i].Value, $"{context}.{st.Properties[i].Name}");
                }
                break;
        }

        Assert.AreEqual(expected.ToString(), actual.ToString(), context + " text");
    }

    internal static bool FastPath(string line, out LogEvent evt)
    {
        var reference = Reference(line);
        var template = reference?.MessageTemplate ?? Parser.Parse("(unused)");
        return new FastEventReader().TryRead(Encoding.UTF8.GetBytes(line), template, out evt);
    }

    [Test]
    public void Every_Line_Of_The_Sample_Log_Takes_The_Fast_Path_And_Matches_Serilog()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "UmbracoTraceLog.UNITTEST.20181112.json");
        var fast = new FastEventReader();
        var lines = 0;
        foreach (var line in File.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var expected = LogEventReader.ReadFromString(line);
            Assert.IsTrue(fast.TryRead(Encoding.UTF8.GetBytes(line), expected.MessageTemplate, out var actual), $"line {lines + 1} fell back");
            AssertSame(expected, actual, $"line {lines + 1}");
            lines++;
        }

        Assert.AreEqual(2410, lines);
    }

    private const string T = "\"@t\":\"2024-01-01T00:00:00Z\"";

    public static IEnumerable<TestCaseData> TrickyLines()
    {
        TestCaseData Case(string name, string line) => new TestCaseData(line).SetName(name);

        yield return Case("nested structures, arrays, numbers, escapes", "{" + T + ",\"@mt\":\"Hi {A} {B}\",\"A\":{\"$type\":\"Foo\",\"x\":1,\"y\":[1,2.5,null,true,\"s\"],\"\":\"e\"},\"B\":[{\"k\":\"v\"},[]],\"@@lit\":1,\"@weird\":2,\"\":3,\"Neg\":-0,\"Big\":9223372036854775808,\"Huge\":18446744073709551615,\"Float\":1e3,\"Frac\":0.1,\"Exp\":1.5E-3,\"Uni\":\"\\u00e9 \\ud83d\\ude00 \\\"q\\\" \\n\",\"Empty\":{},\"T\":true,\"F\":false,\"N\":null}");
        yield return Case("event id string", "{" + T + ",\"@mt\":\"x\",\"@i\":\"a1b2c3d4\"}");
        yield return Case("event id number", "{" + T + ",\"@mt\":\"x\",\"@i\":3735928559}");
        yield return Case("event id negative (Serilog throws)", "{" + T + ",\"@mt\":\"x\",\"@i\":-1}");
        yield return Case("exception text", "{" + T + ",\"@mt\":\"x\",\"@l\":\"Error\",\"@x\":\"System.Exception: boom\\n   at A.B()\"}");
        yield return Case("renderings", "{" + T + ",\"@mt\":\"Took {Elapsed:0.00}ms\",\"Elapsed\":3.14159,\"@r\":[\"3.14\"]}");
        yield return Case("@m only", "{" + T + ",\"@m\":\"Hello {world} }\"}");
        yield return Case("trace and span ids", "{" + T + ",\"@mt\":\"x\",\"@tr\":\"0123456789abcdef0123456789abcdef\",\"@sp\":\"0123456789abcdef\"}");
        yield return Case("level numeric", "{" + T + ",\"@mt\":\"x\",\"@l\":\"3\"}");
        yield return Case("level lower case", "{" + T + ",\"@mt\":\"x\",\"@l\":\"warning\"}");
        yield return Case("level invalid (Serilog throws)", "{" + T + ",\"@mt\":\"x\",\"@l\":\"Nope\"}");
        yield return Case("level null", "{" + T + ",\"@mt\":\"x\",\"@l\":null}");
        yield return Case("timestamp with offset", "{\"@t\":\"2024-01-01T10:00:00.1234567+01:00\",\"@mt\":\"x\"}");
        yield return Case("timestamp without offset", "{\"@t\":\"2024-01-01T10:00:00\",\"@mt\":\"x\"}");
        yield return Case("timestamp nine fractional digits", "{\"@t\":\"2024-01-01T10:00:00.123456789Z\",\"@mt\":\"x\"}");
        yield return Case("timestamp rfc1123", "{\"@t\":\"Mon, 01 Jan 2024 10:00:00 GMT\",\"@mt\":\"x\"}");
        yield return Case("timestamp null (Serilog throws)", "{\"@t\":null,\"@mt\":\"x\"}");
        yield return Case("timestamp number (Serilog throws)", "{\"@t\":1700000000,\"@mt\":\"x\"}");
        yield return Case("no template and no message", "{" + T + "}");
        yield return Case("json comment", "{" + T + ",\"@mt\":\"x\" /* c */}");
        yield return Case("duplicate property", "{" + T + ",\"@mt\":\"x\",\"A\":1,\"A\":2}");
        yield return Case("trailing text after the object", "{" + T + ",\"@mt\":\"x\"} trailing");
        yield return Case("type tag as top level property", "{" + T + ",\"@mt\":\"x\",\"$type\":\"T\"}");
        yield return Case("type tag not a string", "{" + T + ",\"@mt\":\"x\",\"S\":{\"$type\":5,\"a\":1}}");
        yield return Case("escaped template", "{" + T + ",\"@mt\":\"Quote \\\"{N}\\\" \\u00e9 {{literal}}\",\"N\":1}");
        yield return Case("escaped property name", "{" + T + ",\"@mt\":\"x\",\"na\\u006De\":1}");
        yield return Case("depth beyond the default limit", "{" + T + ",\"@mt\":\"x\",\"D\":" + string.Concat(Enumerable.Repeat("[", 80)) + string.Concat(Enumerable.Repeat("]", 80)) + "}");
        yield return Case("huge double (Serilog throws or infinity)", "{" + T + ",\"@mt\":\"x\",\"D\":1e999}");
        yield return Case("not an object", "[1,2,3]");
        yield return Case("not json", "this is not json");
    }

    [TestCaseSource(nameof(TrickyLines))]
    public void Fast_Reader_Matches_Serilog_Or_Declines(string line)
    {
        var reference = Reference(line);
        if (FastPath(line, out var fast))
        {
            Assert.IsNotNull(reference, "the fast reader accepted a line Serilog rejects");
            AssertSame(reference!, fast, "fast reader");
        }
    }

    [Test]
    public void The_Tricky_Lines_Mostly_Stay_On_The_Fast_Path()
    {
        // Guard against "everything quietly falls back": the ordinary shapes must be handled by the fast reader itself
        var ordinary = new[]
        {
            TrickyLines().First().Arguments[0]!.ToString()!,
            "{" + T + ",\"@mt\":\"x\",\"@i\":\"a1b2c3d4\"}",
            "{" + T + ",\"@mt\":\"x\",\"@l\":\"Error\",\"@x\":\"System.Exception: boom\"}",
            "{\"@t\":\"2024-01-01T10:00:00.1234567+01:00\",\"@mt\":\"x\"}",
        };

        foreach (var line in ordinary)
            Assert.IsTrue(FastPath(line, out _), line);
    }

    [Test]
    public void Index_Reads_Tricky_Lines_Like_Serilog_And_Never_Crashes_A_Query()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clef-fast-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var lines = TrickyLines().Select(c => (string)c.Arguments[0]!).Where(l => l.StartsWith("{")).ToList();
            var path = Path.Combine(dir, "tricky.json");
            File.WriteAllLines(path, lines);
            using var index = LogIndex.Open(path);

            // Rows are only the lines with an @t; match each row to its source line
            for (var row = 0; row < index.Count; row++)
            {
                var entry = TryReadRow(index, row);
                var reference = Reference(index.ReadRawLine(row));
                if (reference is null)
                    Assert.IsNull(entry, $"row {row}: Serilog rejects it, so must we");
                else
                {
                    Assert.IsNotNull(entry, $"row {row}");
                    Assert.AreEqual(reference.RenderMessage(), entry!.RenderedMessage, $"row {row} message");
                    Assert.AreEqual(reference.MessageTemplate.Text, entry.MessageTemplate, $"row {row} template");
                    Assert.AreEqual(reference.Level, entry.Level, $"row {row} level");
                    Assert.AreEqual(reference.Exception?.ToString(), entry.Exception, $"row {row} exception");
                }
            }

            Assert.DoesNotThrow(() => index.Query(new LogQuery { Filter = "Big > 0" }));
            Assert.DoesNotThrow(() => index.Query(new LogQuery { Filter = "tricky" }));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static LogEntry? TryReadRow(LogIndex index, int row)
    {
        try { return index.ReadRow(row); }
        catch (InvalidDataException) { return null; }
    }

    [TestCase("localhost")]
    [TestCase("RUNTIME")]
    [TestCase("umbraco")]
    [TestCase("Timing")]
    [TestCase("50%")]
    [TestCase("a_b")]
    [TestCase("{")]
    [TestCase("'")]
    [TestCase("zzzznotfound")]
    public void Plain_Word_Search_Matches_The_Like_Expression_It_Replaces(string word)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "UmbracoTraceLog.UNITTEST.20181112.json");
        var events = File.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => LogEventReader.ReadFromString(l)).ToList();

        Assert.IsTrue(SerilogExpression.TryCompile($"@m like '%{SerilogExpression.EscapeLikeExpressionContent(word)}%' ci", out var like, out _));
        var oracle = events.Count(e => ExpressionResult.IsTrue(like!(e)));

        var predicate = FilterCompiler.Compile(word)!.Matches;
        Assert.AreEqual(oracle, events.Count(predicate), $"word '{word}'");
    }

    [Test]
    public void A_Line_With_Only_A_Message_Gets_The_Same_Template_As_Serilog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clef-m-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var line = "{" + T + ",\"@m\":\"Hello {world} }\"}";
            var path = Path.Combine(dir, "m.json");
            File.WriteAllText(path, line + "\n");
            using var index = LogIndex.Open(path);

            Assert.AreEqual(LogEventReader.ReadFromString(line).MessageTemplate.Text, index.GetRowInfo(0).MessageTemplate);
            Assert.AreEqual("Hello {world} }", index.ReadRow(0).RenderedMessage);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
