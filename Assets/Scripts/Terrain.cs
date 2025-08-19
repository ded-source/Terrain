using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;

public class Terrain : MonoBehaviour
{
    [SerializeField] private float terrainSizeMeters = 8192.0f;
    [SerializeField] private float terrainHeightMeters = 32.0f;
    [SerializeField] private float tileSizeMeters = 64.0f;
    [SerializeField] private int tileQuadCount = 32;

    [SerializeField] private Material terrainMaterialPrefab;

    [SerializeField] private float lod0Distance = 64.0f;
    [SerializeField] private float lodBias = 2.0f;

    [SerializeField] private Camera mainCamera;
    [SerializeField] private Camera overheadDebugCamera;

    [SerializeField] private Clipmap albedoMapClipmap;
    [SerializeField] private Clipmap heightMapClipmap;

    Mesh tileMesh_;
    List<Matrix4x4> tileMatrices_;
    int lodCount_;

    private Material terrainMaterial_;
    private Material debugMaterial_;

    class QuadTreeNode
    {
        public Vector3 pos;
        public float size;
        public int lod;
        public Bounds bounds;

        public QuadTreeNode[] children = { null, null, null, null };

        public QuadTreeNode(Vector3 _pos, float _size, float _height, int _lod)
        {
            pos = _pos;
            size = _size;
            lod = _lod;

            var sizeVec = new Vector3(_size, _height, _size);
            bounds = new Bounds(_pos + sizeVec * 0.5f, sizeVec);
        }
    }

    QuadTreeNode quadTreeRoot_;

    void SetupTileMesh()
    {
        tileMesh_ = new Mesh();

        // Create vertices for a mesh with tileQuadCount * tileQuadCount quads.
        int tileVertCount = (tileQuadCount + 1);
        Vector3[] tile_vertices = new Vector3[tileVertCount * tileVertCount];
        Vector2[] tile_uvs = new Vector2[tileVertCount * tileVertCount];
        for (int y = 0; y < tileVertCount; ++y)
        {
            for (int x = 0; x < tileVertCount; ++x)
            {
                tile_vertices[y * tileVertCount + x] = new Vector3((float)x / tileQuadCount, 0.0f, (float)y / tileQuadCount);
                tile_uvs[y * tileVertCount + x] = new Vector2((float)x / tileQuadCount, (float)y / tileQuadCount);
            }
        }
        tileMesh_.vertices = tile_vertices;
        tileMesh_.uv = tile_uvs;


        // There are 6 vertex indices per quad in the mesh
        int[] tile_indices = new int[tileQuadCount * tileQuadCount * 6];
        for (int y = 0; y < tileQuadCount; ++y)
        {
            for (int x = 0; x < tileQuadCount; ++x)
            {
                /*
                        D-------C-------x
                        |(x,y)  |(x+1,y)|
                        |       |       |
                        A-------B-------x

                        Vertiex indices for quad (x,y) in Unity's left-handed coordinate system:
                            { A, D, B }
                            { B, D, C }
                        Where A = x, B = A+1, C = B + tileVertCount, D = A + tileVertCount
                 */

                int A = y * tileVertCount + x;
                int B = A + 1;
                int C = B + tileVertCount;
                int D = A + tileVertCount;
                tile_indices[y * tileQuadCount * 6 + x * 6 + 0] = A;
                tile_indices[y * tileQuadCount * 6 + x * 6 + 1] = D;
                tile_indices[y * tileQuadCount * 6 + x * 6 + 2] = B;
                tile_indices[y * tileQuadCount * 6 + x * 6 + 3] = B;
                tile_indices[y * tileQuadCount * 6 + x * 6 + 4] = D;
                tile_indices[y * tileQuadCount * 6 + x * 6 + 5] = C;
            }
        }

        tileMesh_.triangles = tile_indices;
        tileMesh_.RecalculateNormals();

        var boundsSize = new Vector3(1.0f, terrainHeightMeters, 1.0f);
        tileMesh_.bounds = new Bounds(boundsSize * 0.5f, boundsSize);
    }

    void SetupInstanceMatrices()
    {
        int tileCount = Mathf.CeilToInt(terrainSizeMeters / tileSizeMeters);
        tileMatrices_ = new List<Matrix4x4>(tileCount * tileCount);
        for(int i = 0; i < tileMatrices_.Capacity; ++i)
        {
            tileMatrices_.Add(Matrix4x4.identity);
        }
        for(int y = 0; y < tileCount; y++)
        {
            for (int x = 0; x < tileCount; x++)
            {
                tileMatrices_[y * tileCount + x] =
                    Matrix4x4.Translate(new Vector3(x * tileSizeMeters, 0.0f, y * tileSizeMeters)) *
                    Matrix4x4.Scale(new Vector3(tileSizeMeters, 1.0f, tileSizeMeters));
            }
        }
    }

    void SetupMaterial()
    {
        terrainMaterial_ = Instantiate(terrainMaterialPrefab);
        debugMaterial_ = Instantiate(terrainMaterialPrefab);

        terrainMaterial_.SetFloat("_HeightScale", terrainHeightMeters);
        terrainMaterial_.SetFloat("_TerrainSize", terrainSizeMeters);
        terrainMaterial_.SetFloat("_TileSize", tileSizeMeters);
        terrainMaterial_.DisableKeyword("_ENABLE_DEBUG_VIEW_ON");

        debugMaterial_.SetFloat("_HeightScale", terrainHeightMeters);
        debugMaterial_.SetFloat("_TerrainSize", terrainSizeMeters);
        debugMaterial_.SetFloat("_TileSize", tileSizeMeters);
        debugMaterial_.EnableKeyword("_ENABLE_DEBUG_VIEW_ON");
    }

    // This makes some assumptions about terrain size and tile size being powers of two.
    void SetupQuadTree()
    {
        lodCount_ = Mathf.RoundToInt(Mathf.Log(terrainSizeMeters / tileSizeMeters, 2));
        Debug.Assert(lodCount_ == Mathf.Log(terrainSizeMeters / tileSizeMeters, 2));
        Console.WriteLine("Lod count: " + lodCount_);
        quadTreeRoot_ = new QuadTreeNode(new Vector3(0, 0, 0), terrainSizeMeters, terrainHeightMeters, lodCount_ - 1);
        List<QuadTreeNode> pendingNodes = new List<QuadTreeNode> { quadTreeRoot_ };
        while (pendingNodes.Count > 0)
        {
            var currentNode = pendingNodes.Last();
            pendingNodes.RemoveAt(pendingNodes.Count - 1);

            if (currentNode.size > tileSizeMeters)
            {
                var halfSize = currentNode.size / 2;
                var xOffset = new Vector3(halfSize, 0, 0);
                var yOffset = new Vector3(0, 0, halfSize);
                var lod = Mathf.RoundToInt(Mathf.Log(halfSize / tileSizeMeters, 2)) - 1;
                Debug.Assert(lod == Mathf.Log(halfSize / tileSizeMeters, 2) - 1.0f);
                currentNode.children[0] = new QuadTreeNode(currentNode.pos, halfSize, terrainHeightMeters, lod);
                currentNode.children[1] = new QuadTreeNode(currentNode.pos + xOffset, halfSize, terrainHeightMeters, lod);
                currentNode.children[2] = new QuadTreeNode(currentNode.pos + yOffset, halfSize, terrainHeightMeters, lod);
                currentNode.children[3] = new QuadTreeNode(currentNode.pos + xOffset + yOffset, halfSize, terrainHeightMeters, lod);
                pendingNodes.AddRange(currentNode.children);

                Debug.Assert(halfSize >= tileSizeMeters);
            }
        }
    }

    void UpdateTiles(Vector3 eyePos)
    {
        tileMatrices_.Clear();

        var frustumPlanes = GeometryUtility.CalculateFrustumPlanes(mainCamera);

        List<QuadTreeNode> pendingNodes = new List<QuadTreeNode> { quadTreeRoot_ };
        while (pendingNodes.Count > 0)
        {
            var currentNode = pendingNodes.Last();
            pendingNodes.RemoveAt(pendingNodes.Count - 1);

            var halfSize = currentNode.size / 2;
            var xOffset = new Vector3(halfSize, 0, 0);
            var yOffset = new Vector3(0, 0, halfSize);

            var distance = (eyePos - (currentNode.pos + xOffset + yOffset)).magnitude;

            var lodDistance = Mathf.Pow(2, currentNode.lod + lodBias) * lod0Distance;

            bool subdivideTile = distance - halfSize < lodDistance;
            if (subdivideTile && (currentNode.children[0] != null))
            {
                pendingNodes.AddRange(currentNode.children);
            }
            else
            {
                if (GeometryUtility.TestPlanesAABB(frustumPlanes, currentNode.bounds))
                {
                    var mat =
                        Matrix4x4.Translate(currentNode.pos) *
                        Matrix4x4.Scale(new Vector4(currentNode.size, 1.0f, currentNode.size, 1.0f));
                    tileMatrices_.Add(mat);
                }
            }
        }

        albedoMapClipmap.UpdateTiles(mainCamera.transform.position.x / terrainSizeMeters, mainCamera.transform.position.z / terrainSizeMeters);
        heightMapClipmap.UpdateTiles(mainCamera.transform.position.x / terrainSizeMeters, mainCamera.transform.position.z / terrainSizeMeters);
    }

    void SetupCameras()
    {
        mainCamera.transform.position = new Vector3(terrainSizeMeters / 2, terrainHeightMeters * 2, terrainSizeMeters / 2);

        overheadDebugCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        overheadDebugCamera.transform.position = mainCamera.transform.position + new Vector3(0.0f, 1000.0f, 0.0f);
        overheadDebugCamera.orthographicSize = terrainSizeMeters / 2;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        SetupTileMesh();
        SetupInstanceMatrices();
        SetupMaterial();
        SetupQuadTree();
        SetupCameras();
    }

    void UpdateMaterials()
    {
        terrainMaterial_.SetTexture("_MainTexClipmapArray", albedoMapClipmap.ClipmapTextureArray);
        terrainMaterial_.SetFloat("_MainTexClipmapArrayCount", albedoMapClipmap.ClipmapTextureArray.depth);
        terrainMaterial_.SetTexture("_MainTexClipmapArray", albedoMapClipmap.ClipmapTextureArray);
        terrainMaterial_.SetFloat("_MainTexClipmapArrayCount", albedoMapClipmap.ClipmapTextureArray.depth);

        debugMaterial_.SetTexture("_HeightMapClipmapArray", heightMapClipmap.ClipmapTextureArray);
        debugMaterial_.SetFloat("_HeightMapClipmapArrayCount", heightMapClipmap.ClipmapTextureArray.depth);
        debugMaterial_.SetTexture("_HeightMapClipmapArray", heightMapClipmap.ClipmapTextureArray);
        debugMaterial_.SetFloat("_HeightMapClipmapArrayCount", heightMapClipmap.ClipmapTextureArray.depth);
    }

    // Update is called once per frame
    void Update()
    {
        UpdateTiles(mainCamera.transform.position);
        UpdateMaterials();

        // Draw for main camera
        Graphics.DrawMeshInstanced(
            tileMesh_, 0, terrainMaterial_, tileMatrices_,
            properties: null,
            castShadows: UnityEngine.Rendering.ShadowCastingMode.On,
            receiveShadows: true,
            layer: 0);

        // Draw for debug camera
        Graphics.DrawMeshInstanced(
            tileMesh_, 0, debugMaterial_, tileMatrices_,
            properties: null,
            castShadows: UnityEngine.Rendering.ShadowCastingMode.On,
            receiveShadows: true,
            LayerMask.NameToLayer("Debug"));
    }
}
