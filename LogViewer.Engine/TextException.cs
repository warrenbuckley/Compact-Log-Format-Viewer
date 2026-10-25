namespace LogViewer.Engine;

/// <summary>Carries the @x text of a CLEF line; ToString() returns exactly that text, as the Serilog reader's own exception does.</summary>
internal sealed class TextException : Exception
{
    private readonly string _text;

    public TextException(string text) : base(text) => _text = text;

    public override string ToString() => _text;
}
