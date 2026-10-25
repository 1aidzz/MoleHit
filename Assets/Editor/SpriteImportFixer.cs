#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把 2D 贴图统一切到"清晰"的导入设置。
///
/// 背景很糊的原因：
///   1. 所有图都被压成了 Normal 质量的 DXT/BC 压缩（有损，边缘发虚、块状）；
///   2. 背景原图是 2848x1600，但 Max Size 只有 2048，导入时被强制缩小，
///      再显示到 1920x1080 就等于缩了两次。
///   3. 地鼠原图 2048 却只有 ~230px 的显示大小，没开 mipmap，
///      大幅缩小时会闪烁/发虚。
///
/// 这里统一处理：不压缩、Max Size 足够大、按需开 mipmap。
/// 菜单：Tools → 打地鼠 → 修复贴图导入设置
/// </summary>
public static class SpriteImportFixer
{
    private const int Max2K = 2048;
    private const int Max4K = 4096;

    // 显示尺寸接近原图、甚至被放大 → 不要 mipmap（mipmap 会让放大变软）
    private static readonly string[] NoMipmapPaths =
    {
        "Assets/Resources/Dizzy/dizzy.png",
        "Assets/Resources/Mole/mole_hit.png",
        "Assets/Sprites/UI/panel_round.png",
    };

    // 显示时会被明显缩小 → 开 mipmap，缩小时更干净
    private static readonly string[] MipmapPaths =
    {
        "Assets/Sprites/mole.png",
        "Assets/Sprites/hole.png",
    };

    // 大背景图：Max Size 提到 4096，避免 2848 被压到 2048
    private static readonly string[] BackgroundPaths =
    {
        "Assets/Sprites/Background/background.png",
        "Assets/Sprites/Background/startBg.png",
        "Assets/Sprites/Background/title.png",
        "Assets/Sprites/Background/win.png",
        "Assets/Sprites/Background/failure.png",
    };

    [MenuItem("Tools/打地鼠/修复贴图导入设置")]
    public static void FixAll()
    {
        AssetDatabase.StartAssetEditing();

        foreach (string path in NoMipmapPaths)
            Apply(path, Max2K, false, false);
        foreach (string path in MipmapPaths)
            Apply(path, Max2K, true, false);
        foreach (string path in BackgroundPaths)
            Apply(path, Max4K, true, false);

        // 连杀图标：480x450，屏幕上是 1:1 大小，不要 mipmap
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/KillAnnounce/icons" }))
            Apply(AssetDatabase.GUIDToAssetPath(guid), Max2K, false, false);

        // 九宫格面板：切好 border，方便 Image 用 Sliced 拉伸不糊角
        ApplySliced("Assets/Sprites/UI/panel_round.png", 128, 36);

        AssetDatabase.StopAssetEditing();
        AssetDatabase.Refresh();
        Debug.Log("[SpriteImportFixer] 贴图导入设置已更新。");
    }

    private static void Apply(string path, int maxSize, bool mipmaps, bool forceSprite)
    {
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;

        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 100f;
        ti.mipmapEnabled = mipmaps;
        ti.filterMode = FilterMode.Bilinear;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.alphaIsTransparency = true;
        ti.sRGBTexture = true;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = maxSize;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.compressionQuality = 100;
        ti.crunchedCompression = false;
        ti.SaveAndReimport();
    }

    /// <summary>设为九宫格精灵：border 用像素表示，四角保持圆形不拉伸</summary>
    private static void ApplySliced(string path, int maxSize, int border)
    {
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;

        Apply(path, maxSize, false, false);
        ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;

        TextureImporterSettings settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        settings.spriteBorder = new Vector4(border, border, border, border);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(settings);
        ti.SaveAndReimport();
    }
}
#endif
