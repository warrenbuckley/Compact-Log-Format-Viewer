namespace LogViewer.Engine;

/// <summary>The rows found in one byte range of the file, using ids local to that range.</summary>
internal sealed class ChunkResult
{
    public readonly List<long> Offsets = new();
    public readonly List<int> Lengths = new();
    public readonly List<long> Ticks = new();
    public readonly List<byte> Levels = new();
    public readonly List<int> Templates = new();
    public readonly List<int> Requests = new();

    public readonly Utf8Interner TemplateTable = new();
    public readonly Utf8Interner RequestTable = new();

    public int Malformed;
    public bool Chronological = true;

    public int Count => Offsets.Count;
}
