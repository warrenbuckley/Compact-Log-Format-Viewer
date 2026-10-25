namespace LogViewer.Engine;

/// <summary>Layout of the per-row level byte in the index.</summary>
internal static class LevelByte
{
    /// <summary>Bits 0-2: the level (0 Verbose .. 5 Fatal).</summary>
    public const byte LevelMask = 7;

    /// <summary>The @l text was not one of the six level names, so the level above is a guess (Information) and must not be relied on to exclude the row.</summary>
    public const byte UnknownLevel = 0x40;

    /// <summary>The line has an @x that is not null.</summary>
    public const byte HasException = 0x80;
}
