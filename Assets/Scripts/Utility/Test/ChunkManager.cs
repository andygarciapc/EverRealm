using UnityEngine;
//using static MeshGenerator;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class ChunkManager : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool showChunkCoordinates = true;

    public ChunkData ChunkData { get; private set; }
    private MeshGenerator meshGenerator;
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private bool needsMeshUpdate;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();
    }

    public void Initialize(ChunkData data, MeshGenerator generator)
    {
        ChunkData = data;
        meshGenerator = generator;
        UpdateChunk();
    }

    void Update()
    {
        if (ChunkData.IsModified || needsMeshUpdate)
        {
            UpdateChunk();
        }
    }

    public void UpdateChunk()
    {
        if (ChunkData == null || meshGenerator == null) return;

        // Use fully qualified type name
        MeshGenerator.VoxelMeshData meshData = meshGenerator.GenerateMesh(ChunkData);
        ApplyMesh(meshData);
        ChunkData.MarkAsProcessed();
        needsMeshUpdate = false;
    }

    private void ApplyMesh(MeshGenerator.VoxelMeshData meshData)
    {
        Mesh mesh = new Mesh();
        mesh.name = $"Chunk_{ChunkData.chunkPosition}";

        mesh.SetVertices(meshData.vertices);
        mesh.SetTriangles(meshData.triangles, 0);
        mesh.SetUVs(0, meshData.uvs);
        mesh.RecalculateNormals();

        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = mesh;
    }

    public void SetBlock(Vector3Int localPosition, byte blockID)
    {
        if (ChunkData.IsValidLocalPosition(localPosition.x, localPosition.y, localPosition.z))
        {
            ChunkData.SetBlock(localPosition.x, localPosition.y, localPosition.z, blockID);
            needsMeshUpdate = true;
        }
    }

    void OnDrawGizmos()
    {
        if (!showChunkCoordinates || ChunkData == null) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position + new Vector3(8, 128, 8),
            new Vector3(16, 256, 16));

        UnityEditor.Handles.Label(transform.position + Vector3.up * 256,
            $"Chunk: {ChunkData.chunkPosition}");
    }
}