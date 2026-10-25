using System.Text;
using Serilog.Expressions;
using Serilog.Formatting.Compact.Reader;

namespace LogViewer.Engine.Tests;

/// <summary>
/// Filters only build the properties they can look at. Every result here is checked against a brute force run of Serilog's own
/// reader and expression engine over every line, which shares none of the engine's code.
/// </summary>
public class PruningTests
{
    private string _dir = null!;
    private string _path = null!;
    private LogIndex _index = null!;
    private List<Serilog.Events.LogEvent> _events = null!;

    [OneTimeSetUp]
    public void Build()
    {
        _dir = Path.Combine(Path.GetTempPath(), "clef-prune-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "pruning.json");

        TestData.WriteMixedLog(_path, 6000);
        var lines = File.ReadAllLines(_path);
        _index = LogIndex.Open(_path);
        _events = lines.Select(l => LogEventReader.ReadFromString(l)).ToList();
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        _index.Dispose();
        Directory.Delete(_dir, true);
    }

    private int Oracle(string expression)
    {
        Assert.IsTrue(SerilogExpression.TryCompile(expression, null, new Expressions.LegacyNameResolver(typeof(Expressions.SerilogExtensions)), out var compiled, out var error), error);
        return _events.Count(e => ExpressionResult.IsTrue(compiled(e)));
    }

    [TestCase("Duration > 1000")]
    [TestCase("Duration > 1000 and StartsWith(SourceContext, 'Umbraco.Core')")]
    [TestCase("Has(Duration)")]
    [TestCase("Has(Other) and Other > 100")]
    [TestCase("Not(Has(Duration))")]
    [TestCase("Info.Label = 'label3'")]
    [TestCase("Info.Id > 3000")]
    [TestCase("Tags[?] = 'b2'")]
    [TestCase("@l = 'Error' and Contains(@x, 'nope 4')")]
    [TestCase("@m like '%Person3%'")]
    [TestCase("@m like '%from Town2%'")]
    [TestCase("@Message like '%label%'")]
    [TestCase("@mt like 'Hello%'")]
    [TestCase("@p[?] = 'Umbraco.Core.Thing1'")]
    [TestCase("@p[?] = 2997")]
    [TestCase("@p[?] = 'Person3'")]
    [TestCase("@p[?] = 'Town2'")]
    [TestCase("Properties[?] = 'label3'")]
    [TestCase("@p['Duration'] > 2000")]
    [TestCase("Properties['Name'] = 'Person3'")]
    [TestCase("Has(@p['Other'])")]
    [TestCase("Duration > 100 or Name = 'Person1'")]
    [TestCase("@Level = 'Warning' and @p['@escaped'] = 1")]
    public void Expression_Results_Do_Not_Change_When_Unused_Properties_Are_Skipped(string expression)
    {
        Assert.AreEqual(Oracle(expression), _index.Query(new LogQuery { Filter = expression }).Count, expression);
    }

    [TestCase("person3")]
    [TestCase("town2")]
    [TestCase("Bob 5")]
    [TestCase("{literal}")]
    [TestCase("Failure for")]
    [TestCase("label")]
    public void Plain_Words_Still_See_Property_Values_That_Only_The_Message_Mentions(string word)
    {
        // The word is never a property name, but the rendered message contains it via {Name}/{City}; those must not be pruned
        var expected = _events.Count(e => e.RenderMessage().Contains(word, StringComparison.OrdinalIgnoreCase));
        Assert.Greater(expected, 0, "test data should contain the word: " + word);
        Assert.AreEqual(expected, _index.Query(new LogQuery { Filter = word }).Count, word);
    }

    [Test]
    public void Pages_Still_Carry_Every_Property()
    {
        // Pruning only applies while filtering; reading rows back for display builds everything
        var page = _index.ReadPage(_index.All(), 1, 10, SortOrder.Ascending);
        var first = page.First(e => e.RowId == 0);

        CollectionAssert.AreEquivalent(new[] { "Name", "City", "Duration", "SourceContext" }, first.Properties.Keys);
        Assert.AreEqual(_events[2].Properties["Info"].ToString(), page.First(e => e.RowId == 2).Properties["Info"].ToString());
    }
}
