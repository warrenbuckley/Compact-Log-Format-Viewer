using System.Text;
using LogViewer.Engine.Expressions;
using Serilog.Events;
using static LogViewer.Engine.RowCondition;

namespace LogViewer.Engine.Tests;

/// <summary>
/// The index narrows which rows a filter has to read, using a condition read from the filter text. It may only ever remove rows that cannot match
/// (it can let extra rows through, the real expression removes them), so the result is identical with or without it. These tests pin what the planner
/// understands, what it refuses to be clever about, and then try to break the guarantee with generated expressions.
/// </summary>
public class NarrowingTests
{
    // ------------------------------------------------------------ what the planner reads

    private static RowCondition Plan(string text) => ExpressionPlanner.Plan(text);

    [Test]
    public void The_Clients_Own_Filters_Are_Understood()
    {
        Assert.AreEqual(
            new AnyOf(new RowCondition[] { new LevelIs(LogEventLevel.Error), new LevelIs(LogEventLevel.Fatal), new HasException() }).Parts.Select(p => p.ToString()),
            ((AnyOf)Plan("@Level='Error' or @Level='Fatal' or Has(@Exception)")).Parts.Select(p => p.ToString()), "the error badge");

        Assert.AreEqual(new LevelIs(LogEventLevel.Debug), Plan("@Level = 'Debug'"), "clicking a level");
        Assert.AreEqual(new TemplateIs("{EndMessage} ({Duration}ms) [Timing {TimingId}]"), Plan("@MessageTemplate = '{EndMessage} ({Duration}ms) [Timing {TimingId}]'"), "clicking a template");
    }

    [TestCase("@l = 'Error'", LogEventLevel.Error)]
    [TestCase("@Level='Verbose'", LogEventLevel.Verbose)]
    [TestCase("@Level = 'error'", LogEventLevel.Error)] // the spelling does not matter for a condition that only has to be a superset
    [TestCase("(@Level = 'Warning')", LogEventLevel.Warning)]
    [TestCase("((  @l='Fatal' ))", LogEventLevel.Fatal)]
    public void A_Level_Comparison_Is_Understood(string text, LogEventLevel level) => Assert.AreEqual(new LevelIs(level), Plan(text));

    [TestCase("Has(@x)")]
    [TestCase("Has(@Exception)")]
    [TestCase("IsDefined(@x)")]
    [TestCase("IsDefined( @Exception )")]
    public void An_Exception_Test_Is_Understood(string text) => Assert.AreEqual(new HasException(), Plan(text));

    [Test]
    public void A_Template_Literal_Is_Read_With_Doubled_Quotes_Undone()
        => Assert.AreEqual(new TemplateIs("it's {N}"), Plan("@mt = 'it''s {N}'"));

    [Test]
    public void And_Keeps_What_It_Knows_And_Ignores_What_It_Does_Not()
    {
        Assert.AreEqual(new LevelIs(LogEventLevel.Error), Plan("@l = 'Error' and Duration > 1000"));
        Assert.AreEqual(new LevelIs(LogEventLevel.Error), Plan("Duration > 1000 and @l = 'Error'"));
        Assert.AreEqual(new LevelIs(LogEventLevel.Error), Plan("@m like '%a and b%' and @l = 'Error'"));
        Assert.IsInstanceOf<AllOf>(Plan("@l = 'Error' and Has(@x)"));
    }

    [TestCase("@l = 'Error' OR Has(@x)")]
    [TestCase("@l = 'Error' Or Has(@x)")]
    [TestCase("@l = 'Error' oR Has(@x)")]
    public void Keywords_Are_Recognised_In_Any_Capitalisation(string text)
        => Assert.IsInstanceOf<AnyOf>(Plan(text));

    [Test]
    public void A_Capital_Or_Between_Pieces_Is_Not_Hidden_By_A_Lowercase_And()
    {
        // Found by the generated-expression test: this used to be planned as "(Debug or exception) and Information" and lost every plain-template row
        var plan = Plan("@mt = 'plain' OR (@Level = 'Debug' or Has(@x)) and (@l='Information')");

        Assert.IsInstanceOf<AnyOf>(plan);
        Assert.AreEqual(new TemplateIs("plain"), ((AnyOf)plan).Parts[0]);
    }

    [Test]
    public void An_If_Inside_Parentheses_Is_Its_Own_Piece_And_The_Other_Part_Of_An_And_Still_Narrows()
        => Assert.AreEqual(new LevelIs(LogEventLevel.Error), Plan("@l = 'Error' and (if Has(@x) then true else false)"));

    [Test]
    public void Or_With_Anything_Unknown_Is_Every_Row()
    {
        Assert.AreEqual(All, Plan("@l = 'Error' or Duration > 1000"));
        Assert.AreEqual(All, Plan("Duration > 1000 or @l = 'Error'"));
        Assert.IsInstanceOf<AnyOf>(Plan("@l = 'Error' or Has(@x)"));
    }

    [Test]
    public void Precedence_Is_And_Over_Or()
    {
        // a or b and c  ==  a or (b and c)
        var plan = (AnyOf)Plan("@l = 'Fatal' or @l = 'Error' and Has(@x)");
        Assert.AreEqual(2, plan.Parts.Count);
        Assert.AreEqual(new LevelIs(LogEventLevel.Fatal), plan.Parts[0]);
        Assert.IsInstanceOf<AllOf>(plan.Parts[1]);
    }

    // ------------------------------------------------------------ what it refuses to guess about

    [TestCase("not @l = 'Debug' and @l = 'Error'", TestName = "not before and (its precedence is not modelled)")]
    [TestCase("@l = 'Error' and not Has(@x)")]
    [TestCase("@l = 'Debug' or not Has(@x)")]
    [TestCase("if @l = 'Error' then true else Has(@x)")]
    [TestCase("if @l = 'Error' then Has(@x) else @l = 'Debug' and Has(@x)")]
    [TestCase("Not(@l = 'Debug')")]
    [TestCase("@l <> 'Debug'")]
    [TestCase("@l = 'Nope'")]
    [TestCase("@l = '3'")]
    [TestCase("@l in ['Error', 'Fatal']")]
    [TestCase("@l is not null")]
    [TestCase("Contains(@m, 'x or y')")]
    [TestCase("@m like '% and %'")]
    [TestCase("@l = 'Error' and (")]
    [TestCase("@l = 'Error' and 'unterminated")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("color = 'red'", TestName = "or inside a word is not a keyword")]
    [TestCase("@l = 'Error' or")]
    public void Anything_Not_Understood_Means_Every_Row_Or_At_Least_Never_Fewer_Than_The_Truth(string text)
    {
        var plan = Plan(text);

        // For these, no row may be excluded that could match, so the condition must be "any row" unless it is a part we do understand
        // (the "and" case with a known part may narrow, which is fine because every row that matches has that part)
        if (text.Contains("not ") || text.Contains("if ") || text.Contains("Not(") || text.Contains("<>") || text.Contains(" in ") || text.Contains(" is "))
            Assert.AreEqual(All, plan, text);
        else if (text.Contains(" and (") || text.Contains("'unterminated"))
            Assert.AreEqual(All, plan, text);
        else
            Assert.AreNotEqual(null, plan);
    }

    // ------------------------------------------------------------ the guarantee, tried hard

    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "clef-narrow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, true);

    /// <summary>A log with every awkward combination of level text, exception text and template text the index has to cope with.</summary>
    private string WriteAwkwardLog()
    {
        var levels = new string?[] { null, "\"Verbose\"", "\"Debug\"", "\"Information\"", "\"Warning\"", "\"Error\"", "\"Fatal\"", "\"error\"", "\"ERROR\"", "\"eRRor\"", "\"WARNING\"", "\"3\"", "\"4\"", "\"Nope\"", "\"\"", "null", "2", "\"Err\"", "\"Information \"" };
        var exceptions = new string?[] { null, "\"System.Exception: boom\"", "\"\"", "null", "7", "{\"a\":1}" };
        var templates = new[] { "Hello {Name}", "it's quoted {N}", "{EndMessage} ({Duration}ms) [Timing {TimingId}]", "plain", "braces {{literal}} {N}", "Error occurred", "a or b and c" };
        var lines = new List<string>();
        var n = 0;
        foreach (var level in levels)
            foreach (var exception in exceptions)
                for (var t = 0; t < templates.Length; t++)
                {
                    var l = level is null ? "" : $",\"@l\":{level}";
                    var x = exception is null ? "" : $",\"@x\":{exception}";
                    lines.Add($"{{\"@t\":\"2024-01-01T00:{n / 60 % 60:00}:{n % 60:00}Z\",\"@mt\":{System.Text.Json.JsonSerializer.Serialize(templates[t])}{l}{x},\"N\":{n},\"Name\":\"p{n % 5}\",\"Duration\":{n % 2000}}}");
                    n++;
                }

        // and a few lines with only @m, and one with nothing but a timestamp
        lines.Add("{\"@t\":\"2024-01-01T01:00:00Z\",\"@m\":\"only a message {x}\",\"@l\":\"Error\"}");
        lines.Add("{\"@t\":\"2024-01-01T01:00:01Z\"}");
        var path = Path.Combine(_dir, "awkward.json");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static readonly string[] Atoms =
    {
        "@Level='Verbose'", "@Level = 'Debug'", "@l='Information'", "@Level='Warning'", "@Level='Error'", "@l = 'Fatal'", "@Level='error'", "@Level='WARNING'", "@Level='Nope'", "@Level='3'", "@Level='Err'",
        "Has(@Exception)", "Has(@x)", "IsDefined(@x)", "IsDefined(@Exception)",
        "@MessageTemplate = 'Hello {Name}'", "@mt = 'it''s quoted {N}'", "@MessageTemplate = 'plain'", "@mt = 'not a template in the file'", "@MessageTemplate = 'braces {{literal}} {N}'", "@mt = 'a or b and c'",
        "Duration > 1000", "Duration < 50", "Name = 'p3'", "Has(@tr)", "@m like '%Hello%'", "Contains(@m, 'or')", "@m like '% and %'", "@l <> 'Error'",
        "true", "false", "1 = 1", "N > 10 and N < 5000", "@Level = 'Error' ci",
    };

    private static string Generate(Random rng, int depth)
    {
        if (depth == 0 || rng.Next(5) == 0)
            return Atoms[rng.Next(Atoms.Length)];

        string Sub() => Generate(rng, depth - 1);
        var sp = rng.Next(3) == 0 ? "  " : " ";
        return rng.Next(14) switch
        {
            0 or 1 or 2 => $"{Sub()}{sp}or{sp}{Sub()}",
            3 or 4 or 5 => $"{Sub()}{sp}and{sp}{Sub()}",
            6 => $"({Sub()})",
            7 => $"not {Sub()}",
            8 => $"Not({Sub()})",
            9 => $"if {Sub()} then {Sub()} else {Sub()}",
            10 => $"{Sub()} OR {Sub()}",
            11 => $"({Sub()}){sp}and{sp}({Sub()})",
            12 => $"{Sub()} or {Sub()} and {Sub()}",
            _ => $"{Sub()} and {Sub()} or {Sub()}",
        };
    }

    private static int[] Rows(RowSet set) => Enumerable.Range(0, set.Count).Select(set.RowAt).ToArray();

    [TestCase(11, 1200)]
    [TestCase(23, 1200)]
    [TestCase(37, 1200)]
    public void Narrowing_Never_Changes_A_Result_On_An_Awkward_Log(int seed, int expressions)
    {
        using var index = LogIndex.Open(WriteAwkwardLog());
        var rng = new Random(seed);
        int compiled = 0, narrowed = 0, nonEmpty = 0;

        for (var i = 0; i < expressions; i++)
        {
            var text = Generate(rng, 3);
            var filter = FilterCompiler.Compile(text);
            if (filter is null)
                continue;

            compiled++;
            if (filter.Condition is not null)
                narrowed++;

            var plain = Rows(index.Query(new LogQuery { Filter = text, NarrowWithIndex = false }));
            var fast = Rows(index.Query(new LogQuery { Filter = text }));

            if (plain.Length > 0)
                nonEmpty++;

            CollectionAssert.AreEqual(plain, fast, $"expression: {text}");
        }

        // the test must actually exercise the feature, and on filters that match something
        Assert.Greater(compiled, expressions * 0.7, "most generated expressions are valid");
        Assert.Greater(narrowed, compiled / 8, "a good share of them were narrowed");
        Assert.Greater(nonEmpty, compiled / 4, "and a good share of them match rows, so a wrongly excluded row would show");
    }

    [TestCase("umbraco-12.3.10.excerpt.json")]
    [TestCase("umbraco-17.7.1.excerpt.json")]
    public void Narrowing_Never_Changes_A_Result_On_Real_Umbraco_Logs(string fixture)
    {
        using var index = LogIndex.Open(Path.Combine(TestContext.CurrentContext.TestDirectory, "fixtures", fixture));
        var rng = new Random(5);
        var templates = Enumerable.Range(0, index.Count).Select(r => index.GetRowInfo(r).MessageTemplate).Distinct().Take(30).ToList();

        var texts = new List<string>();
        foreach (var level in new[] { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" })
        {
            texts.Add($"@Level = '{level}'");
            texts.Add($"@Level='{level}' or Has(@Exception)");
        }

        foreach (var t in templates)
        {
            texts.Add($"@MessageTemplate = '{t.Replace("'", "''")}'");
            texts.Add($"@MessageTemplate = '{t.Replace("'", "''")}' and @Level = 'Debug'");
        }

        texts.Add("@Level='Error' or @Level='Fatal' or Has(@Exception)");
        for (var i = 0; i < 300; i++)
            texts.Add(Generate(rng, 3));

        foreach (var text in texts)
        {
            var plain = Rows(index.Query(new LogQuery { Filter = text, NarrowWithIndex = false }));
            var fast = Rows(index.Query(new LogQuery { Filter = text }));
            CollectionAssert.AreEqual(plain, fast, text);
        }
    }

    [Test]
    public void Narrowing_Combines_With_The_Other_Index_Filters_And_Keeps_Row_Order()
    {
        using var index = LogIndex.Open(WriteAwkwardLog());

        var plain = Rows(index.Query(new LogQuery { Levels = LogLevelMask.Error | LogLevelMask.Fatal, From = index.OldestTimestamp, Filter = "Has(@x) or @Level = 'Error'", NarrowWithIndex = false }));
        var fast = Rows(index.Query(new LogQuery { Levels = LogLevelMask.Error | LogLevelMask.Fatal, From = index.OldestTimestamp, Filter = "Has(@x) or @Level = 'Error'" }));

        CollectionAssert.AreEqual(plain, fast);
        CollectionAssert.AreEqual(fast.OrderBy(r => r), fast, "rows stay in file order here");
    }

    [Test]
    public void A_Template_That_Is_Not_In_The_File_Matches_Nothing_Without_Reading_Anything()
    {
        using var index = LogIndex.Open(WriteAwkwardLog());

        Assert.AreEqual(0, index.Query(new LogQuery { Filter = "@MessageTemplate = 'not in this file at all'" }).Count);
    }

    [Test]
    public void The_Odd_Level_Rows_Really_Are_In_The_Log_And_Flagged()
    {
        // Guards the test data: if the awkward rows went missing the tests above would prove nothing
        using var index = LogIndex.Open(WriteAwkwardLog());

        var everyLevelText = new[] { "@Level='Warning'", "@Level='Error'", "@Level='Information'" };
        foreach (var text in everyLevelText)
        {
            var viaExpression = Rows(index.Query(new LogQuery { Filter = text, NarrowWithIndex = false }));
            Assert.Greater(viaExpression.Count(r => index.ReadRow(r).Level == LogEventLevel.Warning || index.ReadRow(r).Level == LogEventLevel.Error || index.ReadRow(r).Level == LogEventLevel.Information), 0);
        }

        // "3" is a Serilog Warning, "error" and "eRRor" are Errors, but the index does not recognise "3" and must not exclude it
        var warnings = Rows(index.Query(new LogQuery { Filter = "@Level='Warning'" }));
        Assert.IsTrue(warnings.Any(r => index.ReadRow(r).Level == LogEventLevel.Warning && index.GetRowInfo(r).Level != LogEventLevel.Warning),
            "a row Serilog reads as Warning but the index files under another level is still found");
    }
}

public class PlannedExpressionTests
{
    [TestCase("@Level='Error' or @Level='Fatal' or Has(@Exception)", true)]
    [TestCase("@Level = 'Debug'", true)]
    [TestCase("@MessageTemplate = 'plain'", true)]
    [TestCase("(@l = 'Error' or @l = 'Fatal') and Has(@x)", true)]
    [TestCase("@l = 'Error' and Duration > 1000", false)]
    [TestCase("@l = 'Error' or Name = 'p3'", false)]
    [TestCase("Duration > 1000", false)]
    [TestCase("@m like '%x%'", false)]
    [TestCase("Has(@tr)", false)]
    [TestCase("not @l = 'Debug'", false)]
    [TestCase("@l = 'Error' and not Has(@x)", false)]
    [TestCase("@l = 'Nope'", false)]
    [TestCase("if @l = 'Error' then true else false", false)]
    [TestCase("@l = 'Error' and (", false)]
    [TestCase("", false)]
    public void An_Expression_Made_Only_Of_Understood_Pieces_Is_Marked_As_Reading_No_Properties(string text, bool readsNone)
        => Assert.AreEqual(readsNone, ExpressionPlanner.PlanFor(text).ReadsNoProperties, text);

    [Test]
    public void The_Compiler_Hands_That_On_As_No_Properties_Needed()
    {
        Assert.IsEmpty(FilterCompiler.Compile("@Level = 'Debug'")!.Properties!);
        Assert.IsNull(FilterCompiler.Compile("@Level = 'Debug' and Duration > 5")!.Properties, "one piece that may read a property means all are built");
        Assert.IsNull(FilterCompiler.Compile("Duration > 5")!.Properties);
    }
}
