using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

public class Clipmap : MonoBehaviour
{
    [SerializeField] private string textureRoot = "";
    [SerializeField] private int virtualSize = 16 * 1024;
    [SerializeField] private int clipSize = 2048;
    [SerializeField] private Texture2DArray clipTextureArray_;
    [SerializeField] private List<RawImage> uiImages_;

    private TextureFormat textureFormat_;
    private int tileSize_;
    private int virtualTileCount_;
    private int clipTileCount_;
    private int clipArrayCount_;
    private List<RectInt> loadedTilesRect_;

    private Dictionary<GraphicsFence, List<Texture2D>> allLoadedTextures_ = new Dictionary<GraphicsFence, List<Texture2D>>();
    List<Texture2D> lastLoadedTextures_ = new List<Texture2D>();
    
    public Texture2DArray TextureArray
    {
        get => clipTextureArray_;
    }

    public int TileSizeTexels
    {
        get => tileSize_;
    }

    public int VirtualSizeTexels
    {
        get => virtualSize;
    }

    private string GetTileAddress(int lod, int x, int y)
    {
        return textureRoot + "/Mip_" + lod + "/tile_" + lod + "_" + x + "_" + y + ".png";
    }

    Texture2D LoadTileImmediate(string address)
    {
        var handle = Addressables.LoadAssetAsync<Texture2D>(address);
        Texture2D texture = handle.WaitForCompletion();
        return texture;
    }

    public void LoadTileAsync(string address, int lod, int dstX, int dstY)
    {
        Addressables.LoadAssetAsync<Texture2D>(address).Completed += (handle) =>
        {
            OnTileLoaded(handle, lod, dstX, dstY); // Capture local variables
        };
    }

    private void OnTileLoaded(AsyncOperationHandle<Texture2D> handle, int lod, int dstX, int dstY)
    {
        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            Texture2D tileTexture = handle.Result;
            Debug.Assert(tileSize_ == tileTexture.width && tileSize_ == tileTexture.height);
            Graphics.CopyTexture(tileTexture, 0, 0, 0, 0, tileSize_, tileSize_,
                                 clipTextureArray_, lod, 0, dstX * tileSize_, dstY * tileSize_);
            Addressables.Release(handle);
            lastLoadedTextures_.Add(tileTexture);
        }
    }

    void Awake()
    {
        var texture = LoadTileImmediate(GetTileAddress(0, 0, 0));
        textureFormat_ = texture.format;
        tileSize_ = texture.width;
        virtualTileCount_ = virtualSize / tileSize_;
        clipTileCount_ = clipSize / tileSize_;
        clipArrayCount_ = (int)Mathf.Log(virtualSize / clipSize, 2.0f);

        Debug.Assert(texture.width == texture.height);
        Debug.Assert(virtualTileCount_ * tileSize_ == virtualSize);
        Debug.Assert(clipTileCount_ * tileSize_ == clipSize);
        Debug.Assert(clipSize <= virtualSize);
        Debug.Assert(Mathf.Pow(2, clipArrayCount_) * clipSize == virtualSize);

        clipTextureArray_ = new Texture2DArray(clipSize, clipSize, clipArrayCount_, textureFormat_, false, !texture.isDataSRGB);
        clipTextureArray_.wrapMode = TextureWrapMode.Repeat;

        for (int i = 0; i < Mathf.Min(clipArrayCount_, uiImages_.Count); ++i)
        {
            Texture2D uiTexture = new Texture2D(clipTextureArray_.width, clipTextureArray_.height, clipTextureArray_.format, false);
            uiImages_[i].texture = uiTexture;
        }

        loadedTilesRect_ = new List<RectInt>();
        for (int i = 0; i < clipArrayCount_; ++i)
        {
            loadedTilesRect_.Add(new RectInt(0, 0, 0, 0));
        }
    }

    void Update()
    {
        // TODO: Find a method to display texture array slices in the UI without texture copies.
        for (int i = 0; i < Mathf.Min(clipArrayCount_, uiImages_.Count); ++i)
        {
            Graphics.CopyTexture(clipTextureArray_, i, uiImages_[i].texture, 0);
        }

        // Unload any texture assets for textures that have been successfully loaded and copied to clipTextureArray_.
        var passedFences = allLoadedTextures_.Keys.Where(key => key.passed).ToList();
        foreach (var passedFence in passedFences)
        {
            var texturesToUnload = allLoadedTextures_[passedFence];
            foreach (var texture in texturesToUnload)
            {
                Resources.UnloadAsset(texture);
            }
            allLoadedTextures_.Remove(passedFence);
        }
    }

    public void UpdateTiles(float xPos, float yPos)
    {
        for (int i = clipArrayCount_ - 1; i >= 0; --i)
        {
            var loadedTiles = loadedTilesRect_[i];

            var lodVirtualTileCount = (int)(virtualTileCount_ / Mathf.Pow(2, i));
            Vector2Int centerSrcTile = new Vector2Int(Mathf.RoundToInt(xPos * lodVirtualTileCount), Mathf.RoundToInt(yPos * lodVirtualTileCount));

            var halfTileCount = clipTileCount_ / 2;
            centerSrcTile.x = Mathf.Clamp(centerSrcTile.x, halfTileCount, lodVirtualTileCount - halfTileCount);
            centerSrcTile.y = Mathf.Clamp(centerSrcTile.y, halfTileCount, lodVirtualTileCount - halfTileCount);

            RectInt requiredTiles = new RectInt(centerSrcTile.x - halfTileCount, centerSrcTile.y - halfTileCount, clipTileCount_, clipTileCount_);

            for (int y = requiredTiles.y; y < requiredTiles.y + requiredTiles.height; ++y)
            {
                for (int x = requiredTiles.x; x < requiredTiles.x + requiredTiles.height; ++x)
                {
                    if(!loadedTiles.Contains(new Vector2Int(x, y)))
                    {
                        LoadTileAsync(
                            GetTileAddress(i, x, (lodVirtualTileCount - y - 1)),
                                           i, x % clipTileCount_, y % clipTileCount_);;
                    }
                }
            }

            loadedTilesRect_[i] = requiredTiles;
        }

        // Use a fence to signal when it is safe to free loaded texture assets from CPU memory.
        if (lastLoadedTextures_.Count > 0)
        {
            var fence = Graphics.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation, SynchronisationStageFlags.AllGPUOperations);
            allLoadedTextures_[fence] = new List<Texture2D>(lastLoadedTextures_);
            lastLoadedTextures_.Clear();
        }
    }
}
