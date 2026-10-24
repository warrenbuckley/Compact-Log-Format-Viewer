namespace LogViewer.Engine;

/// <summary>
/// Assigns small integer ids to repeated UTF-8 strings (message templates, request ids)
/// without allocating a string per log line.
/// </summary>
internal sealed class Utf8Interner
{
    private readonly Dictionary<ulong, int> _ids = new();
    private readonly List<byte[]> _values = new();

    public int Count => _values.Count;

    public byte[] this[int id] => _values[id];

    public int Intern(ReadOnlySpan<byte> value)
    {
        var key = Hash(value);
        while (_ids.TryGetValue(key, out var id))
        {
            if (_values[id].AsSpan().SequenceEqual(value))
                return id;

            key++; // 64-bit hash collision: probe the next slot, equality is always verified
        }

        var created = _values.Count;
        _values.Add(value.ToArray());
        _ids.Add(key, created);
        return created;
    }

    public int Find(ReadOnlySpan<byte> value)
    {
        var key = Hash(value);
        while (_ids.TryGetValue(key, out var id))
        {
            if (_values[id].AsSpan().SequenceEqual(value))
                return id;

            key++;
        }

        return -1;
    }

    public long ApproxBytes()
    {
        long total = _ids.Count * 24L;
        foreach (var v in _values)
            total += v.Length + 24;
        return total;
    }

    private static ulong Hash(ReadOnlySpan<byte> value)
    {
        var h = 14695981039346656037UL; // FNV-1a
        foreach (var b in value)
        {
            h ^= b;
            h *= 1099511628211UL;
        }

        return h;
    }
}
