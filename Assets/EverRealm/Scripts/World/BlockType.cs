namespace EverRealm.World
{
    /// <summary>Block ids stored in chunk data. Values index into <see cref="WorldSettings.blockColors"/>.</summary>
    public enum BlockType : byte
    {
        Air = 0,
        Grass = 1,
        Dirt = 2,
        Stone = 3,
        Sand = 4,
    }
}
