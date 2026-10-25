namespace LogViewer.Engine;

/// <summary>The rows found in one byte range of the file, using ids local to that range.</summary>
internal sealed class ChunkResult
{
    public readonly List<long> Offsets = new();
    public readonly List<int> Lengths = new();
    public readonly List<long> Ticks = new();
    public readonly List<byte> Levels = new();
    public readonly List<int> Templates = new();
    public readonly List<int>[] Correlations;
    public readonly List<int> Traces = new();

    public readonly Utf8Interner TemplateTable = new();
    public readonly Utf8Interner[] CorrelationTables;
    public readonly Utf8Interner TraceTable = new();

    public ChunkResult(int correlationProperties)
    {
        Correlations = new List<int>[correlationProperties];
        CorrelationTables = new Utf8Interner[correlationProperties];
        for (var i = 0; i < correlationProperties; i++)
        {
            Correlations[i] = new List<int>();
            CorrelationTables[i] = new Utf8Interner();
        }
    }

    public int Malformed;
    public bool Chronological = true;

    public int Count => Offsets.Count;
}
