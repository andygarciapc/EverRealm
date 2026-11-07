namespace EverRealm.World
{
    /// <summary>
    /// Block ids stored in chunk data. Values index into <see cref="WorldSettings.blockColors"/>, except
    /// Grass, Stone and Leaves, whose colours come from the land they're on (<see cref="ColumnPaint"/>).
    /// </summary>
    public enum BlockType : byte
    {
        Air = 0,
        Grass = 1,
        Dirt = 2,
        Stone = 3,
        Sand = 4,
        Wood = 5,
        Planks = 6,
        Thatch = 7,
        Path = 8,
        Leaves = 9,
        Log = 10,
        Snow = 11,
    }
}
