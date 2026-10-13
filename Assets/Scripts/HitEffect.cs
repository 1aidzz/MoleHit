using UnityEngine;

/// <summary>
/// 击中地鼠时的漫画式爆闪特效：一个多尖角（12 角）星形多边形，
/// 在命中点弹出（放大回弹）→ 轻微旋转 → 淡出销毁。
/// 贴图由程序在首次使用时生成，不需要任何外部素材。
/// </summary>
public class HitEffect : MonoBehaviour
{
    private const int TexSize = 256;   // 星形贴图边长（像素）
    private const int Spikes = 12;     // 尖角数量

    private static Texture2D starTex;  // 所有特效共用一张贴图，只生成一次

    private SpriteRenderer sr;
    private float life;
    private float duration = 0.38f;    // 特效总时长
    private float spin;                // 存活期间的旋转速度

    /// <summary>在指定世界坐标生成一次爆闪</summary>
    public static void Spawn(Vector3 worldPos)
    {
        if (starTex == null) starTex = BuildStarTexture();

        GameObject go = new GameObject("HitEffect");
        go.transform.position = worldPos;
        go.AddComponent<HitEffect>();
    }

    private void Awake()
    {
        sr = gameObject.AddComponent<SpriteRenderer>();
        // 星星本体约占贴图的 94%，除以 PPU 后可视直径约 1.4 世界单位
        sr.sprite = Sprite.Create(
            starTex,
            new Rect(0, 0, TexSize, TexSize),
            new Vector2(0.5f, 0.5f),
            TexSize / 1.5f);
        sr.sortingOrder = 90; // 画在地鼠(5)之上、锤子(100)之下

        // 每次随机初始角度与旋转方向，避免连续击中时观感重复
        transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-30f, 30f));
        spin = Random.Range(-70f, 70f);
        transform.localScale = Vector3.one * 0.4f;
    }

    private void Update()
    {
        life += Time.deltaTime;
        float k = life / duration;
        if (k >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        // 弹出：0.4 → 1.25 冲大，再回落到 1.0，模拟砸中的冲击
        float scale = k < 0.35f
            ? Mathf.Lerp(0.4f, 1.25f, k / 0.35f)
            : Mathf.Lerp(1.25f, 1f, (k - 0.35f) / 0.65f);
        transform.localScale = Vector3.one * scale;

        transform.rotation = Quaternion.Euler(0f, 0f,
            transform.rotation.eulerAngles.z + spin * Time.deltaTime);

        // 后半段整体淡出
        float alpha = k < 0.55f ? 1f : 1f - (k - 0.55f) / 0.45f;
        sr.color = new Color(1f, 1f, 1f, alpha);
    }

    /// <summary>
    /// 程序化生成 12 尖角星形贴图：
    /// 半径随角度按三角波在"外半径-内半径"间线性折返，得到直边尖刺；
    /// 颜色从中心白热 → 金黄 → 边缘橙红。
    /// </summary>
    private static Texture2D BuildStarTexture()
    {
        Color[] pixels = new Color[TexSize * TexSize];
        Vector2 center = new Vector2(TexSize, TexSize) * 0.5f - Vector2.one * 0.5f;
        float rOuter = TexSize * 0.5f * 0.94f;
        float rInner = TexSize * 0.5f * 0.40f;

        Color whiteHot = new Color(1f, 1f, 0.88f);
        Color gold = new Color(1f, 0.85f, 0.25f);
        Color orange = new Color(1f, 0.45f, 0.10f);

        for (int y = 0; y < TexSize; y++)
        {
            for (int x = 0; x < TexSize; x++)
            {
                Vector2 p = new Vector2(x, y) - center;
                float r = p.magnitude;
                if (r > rOuter) continue;

                // 三角波：12 个尖角
                float t = Mathf.Repeat(Mathf.Atan2(p.y, p.x) * Spikes / (2f * Mathf.PI), 1f);
                float tri = 1f - Mathf.Abs(t * 2f - 1f);
                float rEdge = Mathf.Lerp(rInner, rOuter, tri);
                if (r > rEdge) continue;

                float kn = Mathf.Clamp01(r / rEdge);
                Color col = kn < 0.25f
                    ? Color.Lerp(whiteHot, gold, kn / 0.25f)
                    : Color.Lerp(gold, orange, (kn - 0.25f) / 0.75f);

                // 边缘 1.5px 抗锯齿
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
}
