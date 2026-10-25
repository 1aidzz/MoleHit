using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 读取一张精灵"真正有像素的区域"（忽略四周透明留白）。
///
/// 用途：不同来源的贴图画布尺寸/留白可能差很多（例如 2048x2048 的原图
/// 与 380x383 的哭哭脸），若按整张画布缩放，图案看起来会忽大忽小。
/// 这里按 alpha 外框计算图案本身的尺寸，让缩放/定位都基于可见内容。
///
/// 贴图导入时需勾选 Readable，否则无法读取像素，调用方应退回整张精灵的尺寸。
/// </summary>
public static class SpriteOpaqueBounds
{
    private const byte AlphaThreshold = 24;

    private static readonly Dictionary<int, RectInt> opaqueCache = new Dictionary<int, RectInt>();
    private static readonly Dictionary<int, bool> failedCache = new Dictionary<int, bool>();

    /// <summary>
    /// 图案外框（贴图坐标系）。返回 false 表示贴图不可读或全透明。
    /// </summary>
    public static bool TryGet(Sprite sprite, out RectInt bounds)
    {
        bounds = default(RectInt);
        if (sprite == null) return false;

        Texture2D tex = sprite.texture;
        if (tex == null) return false;

        int key = sprite.GetInstanceID();
        RectInt cached;
        if (opaqueCache.TryGetValue(key, out cached))
        {
            bounds = cached;
            return true;
        }
        if (failedCache.ContainsKey(key)) return false;

        if (!tex.isReadable)
        {
            failedCache[key] = true;
            return false;
        }

        Rect rect = sprite.rect;
        int x0 = Mathf.Max(0, Mathf.RoundToInt(rect.xMin));
        int y0 = Mathf.Max(0, Mathf.RoundToInt(rect.yMin));
        int x1 = Mathf.Min(tex.width, Mathf.RoundToInt(rect.xMax));
        int y1 = Mathf.Min(tex.height, Mathf.RoundToInt(rect.yMax));
        if (x1 <= x0 || y1 <= y0)
        {
            failedCache[key] = true;
            return false;
        }

        Color32[] pixels;
        try { pixels = tex.GetPixels32(); }
        catch (UnityException)
        {
            failedCache[key] = true;
            return false;
        }

        int minX = x1, minY = y1, maxX = x0 - 1, maxY = y0 - 1;
        for (int y = y0; y < y1; y++)
        {
            int row = y * tex.width;
            for (int x = x0; x < x1; x++)
            {
                if (pixels[row + x].a <= AlphaThreshold) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX || maxY < minY)
        {
            failedCache[key] = true;
            return false;
        }

        bounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        opaqueCache[key] = bounds;
        return true;
    }

    /// <summary>
    /// 图案在精灵本地空间的尺寸与中心偏移（相对 pivot）。
    /// 贴图不可读时退回整张精灵的矩形。
    /// </summary>
    public static void GetContentRect(Sprite sprite, out Vector2 sizeLocal, out Vector2 centerOffsetLocal)
    {
        float ppu = Mathf.Max(0.0001f, sprite.pixelsPerUnit);
        Rect rect = sprite.rect;

        RectInt b;
        if (TryGet(sprite, out b))
        {
            sizeLocal = new Vector2(b.width / ppu, b.height / ppu);
            centerOffsetLocal = new Vector2(
                (b.x + b.width * 0.5f - rect.x - sprite.pivot.x) / ppu,
                (b.y + b.height * 0.5f - rect.y - sprite.pivot.y) / ppu);
            return;
        }

        sizeLocal = new Vector2(rect.width / ppu, rect.height / ppu);
        centerOffsetLocal = Vector2.zero;
    }
}
