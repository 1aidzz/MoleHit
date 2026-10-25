using UnityEngine;

/// <summary>
/// 击中地鼠时的「眩晕」特效：地鼠头顶出现一张放大的眩晕图，上下浮动后淡出。
/// 与 HitEffect（命中爆闪）同时出现，让"被打晕"更明显。
///
/// 只有一张图、不旋转，仅做「弹出 → 上下浮动 → 淡出」。
///
/// 【大小】按贴图里"真正有像素的区域"来缩放，而不是整张画布。
/// dizzy.png 四周留白很多（404x383 的画布，图案只占 323x129），
/// 若按画布缩放，图案会显得特别小，所以这里会自动裁掉透明留白再放大。
///
/// 【图片来源】默认读取 Assets/Resources/Dizzy/dizzy.png（不带扩展名）。
/// 把图片丢进那个目录、导入类型设为 Sprite (2D and UI) 即可自动生效，
/// 不需要在场景里拖引用，也不用改代码。
/// 没有放图时会自动用程序生成的黄色小星星代替，所以先空着也能正常跑。
/// </summary>
public class DizzyEffect : MonoBehaviour
{
    /// <summary>自定义眩晕图标的 Resources 路径（不要写扩展名）</summary>
    public const string SpriteResourcePath = "Dizzy/dizzy";

    [Header("表现参数")]
    [Tooltip("特效总时长（秒），建议与地鼠的 dizzyTime 保持一致")]
    public float duration = 0.5f;
    [Tooltip("图案的可见最长边在世界里的尺寸（正交视口高约 10，4 表示约屏幕高度的 40%）")]
    public float iconSize = 4f;
    [Tooltip("图案中心相对命中点的上移高度（世界单位，大致到头顶）")]
    public float rise = 1.9f;
    [Tooltip("上下浮动幅度（世界单位）")]
    public float bob = 0.16f;
    [Tooltip("上下浮动的快慢（越大越快）")]
    public float bobSpeed = 9f;

    private static Sprite cachedSprite;
    private static bool triedLoad;

    private SpriteRenderer iconRenderer;
    private Vector3 basePos;
    private float life;
    private float iconScale;
    private bool built;

    /// <summary>在指定世界坐标生成一次眩晕特效</summary>
    public static void Spawn(Vector3 worldPos)
    {
        Spawn(worldPos, 1.9f);
    }

    /// <summary>在指定世界坐标生成一次眩晕特效，并指定图案中心相对命中点的上移高度</summary>
    public static void Spawn(Vector3 worldPos, float riseHeight)
    {
        GameObject go = new GameObject("DizzyEffect");
        go.transform.position = worldPos;

        DizzyEffect effect = go.AddComponent<DizzyEffect>();
        effect.rise = riseHeight;
        effect.Build();
    }

    private void Build()
    {
        if (built) return;
        built = true;

        Sprite sprite = GetSprite();

        // 用图案的真实外框算缩放和中心，忽略四周透明留白
        Vector2 contentCenterLocal;
        float contentExtent = GetContentExtent(sprite, out contentCenterLocal);
        iconScale = iconSize / Mathf.Max(0.0001f, contentExtent);

        basePos = new Vector3(0f, rise, 0f)
                  - new Vector3(contentCenterLocal.x, contentCenterLocal.y, 0f) * iconScale;

        GameObject icon = new GameObject("DizzyIcon");
        icon.transform.SetParent(transform, false);
        icon.transform.localPosition = basePos;
        icon.transform.localScale = Vector3.one * iconScale;

        iconRenderer = icon.AddComponent<SpriteRenderer>();
        iconRenderer.sprite = sprite;
        iconRenderer.sortingOrder = 60; // 地鼠(5) 之上、锤子(100) 之下
    }

    private void Update()
    {
        if (!built) return;

        life += Time.deltaTime;
        float k = duration <= 0f ? 1f : life / duration;
        if (k >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        // 弹出：0.5 → 1.15 冲大，再回落 1.0
        float pop = k < 0.2f
            ? Mathf.Lerp(0.5f, 1.15f, k / 0.2f)
            : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((k - 0.2f) / 0.25f));

        // 上下浮动
        Vector3 p = basePos;
        p.y += Mathf.Sin(life * bobSpeed) * bob;
        iconRenderer.transform.localPosition = p;

        // 末尾 30% 淡出
        float alpha = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;

        float s = iconScale * pop;
        iconRenderer.transform.localScale = new Vector3(s, s, 1f);
        iconRenderer.color = new Color(1f, 1f, 1f, alpha);
    }

    #region 图案实际尺寸
    /// <summary>
    /// 返回图案可见部分的最长边（精灵本地单位），以及图案中心相对 pivot 的本地偏移。
    /// 贴图不可读时退回整张精灵的尺寸。
    /// </summary>
    private static float GetContentExtent(Sprite sprite, out Vector2 contentCenterLocal)
    {
        Vector2 sizeLocal;
        SpriteOpaqueBounds.GetContentRect(sprite, out sizeLocal, out contentCenterLocal);
        return Mathf.Max(sizeLocal.x, sizeLocal.y);
    }
    #endregion

    #region 图标来源
    private static Sprite GetSprite()
    {
        if (!triedLoad)
        {
            triedLoad = true;
            cachedSprite = Resources.Load<Sprite>(SpriteResourcePath);
        }
        if (cachedSprite != null) return cachedSprite;

        if (fallbackSprite == null)
        {
            if (fallbackTex == null) fallbackTex = BuildStarTexture();
            fallbackSprite = Sprite.Create(
                fallbackTex,
                new Rect(0, 0, TexSize, TexSize),
                new Vector2(0.5f, 0.5f),
                TexSize);
        }
        return fallbackSprite;
    }

    private const int TexSize = 128;
    private static Texture2D fallbackTex;
    private static Sprite fallbackSprite;

    /// <summary>没放自定义图时使用的四角小星星（程序生成，无需素材）</summary>
    private static Texture2D BuildStarTexture()
    {
        Color[] pixels = new Color[TexSize * TexSize];
        Vector2 center = new Vector2(TexSize, TexSize) * 0.5f - new Vector2(0.5f, 0.5f);
        float outer = TexSize * 0.5f * 0.96f;
        float inner = TexSize * 0.16f;

        Color core = new Color(1f, 0.99f, 0.65f);
        Color edge = new Color(1f, 0.78f, 0.20f);

        for (int y = 0; y < TexSize; y++)
        {
            for (int x = 0; x < TexSize; x++)
            {
                Vector2 p = new Vector2(x, y) - center;
                float r = p.magnitude;
                if (r > outer) continue;

                float t = Mathf.Repeat(Mathf.Atan2(p.y, p.x) * 4f / (2f * Mathf.PI), 1f);
                float tri = 1f - Mathf.Abs(t * 2f - 1f);
                float rEdge = Mathf.Lerp(inner, outer, tri);
                if (r > rEdge) continue;

                float kn = Mathf.Clamp01(r / rEdge);
                Color col = Color.Lerp(core, edge, kn);
                col.a = Mathf.Clamp01((rEdge - r) / 1.5f);
                pixels[y * TexSize + x] = col;
            }
        }

        Texture2D tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
    #endregion
}
