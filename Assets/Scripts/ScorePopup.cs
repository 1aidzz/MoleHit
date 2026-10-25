using UnityEngine;

/// <summary>
/// 加分的实时飘字特效：在地鼠被击中的位置弹出「+12」，向上飘、轻微放大后淡出。
///
/// 分数由 GameManager.AddScore 返回（已含连杀倍率），所以飘的数字和实际加的分完全一致。
/// 只用一张文字网格，不依赖任何贴图素材。
/// </summary>
public class ScorePopup : MonoBehaviour
{
    #region 表现参数
    [Tooltip("特效总时长（秒）")]
    public float duration = 0.85f;
    [Tooltip("向上飘的距离（世界单位）")]
    public float riseDistance = 1.25f;
    [Tooltip("文字高度（世界单位，正交视口高约 10）")]
    public float textHeight = 0.85f;
    [Tooltip("左右随机偏移幅度（世界单位），连续得分时不会完全重叠")]
    public float driftRange = 0.28f;
    #endregion

    private static Font cachedFont;

    private TextMesh mainText;
    private TextMesh shadowText;
    private Renderer mainRenderer;
    private Renderer shadowRenderer;

    private bool built;
    private bool normalized;
    private float life;
    private float drift;
    private float targetScale;
    private Vector3 startPos;
    private Color mainColor;
    private Color shadowColor;

    private static readonly Color Gold = new Color(1f, 0.84f, 0.16f, 1f);
    private static readonly Color Shadow = new Color(0.24f, 0.13f, 0.04f, 1f);

    /// <summary>在指定世界坐标弹出一个「+amount」</summary>
    public static void Spawn(Vector3 worldPos, int amount)
    {
        Spawn(worldPos, amount, 0.35f);
    }

    /// <summary>在指定世界坐标弹出一个「+amount」，rise 是相对命中点的初始上移</summary>
    public static void Spawn(Vector3 worldPos, int amount, float rise)
    {
        if (amount == 0) return;

        GameObject go = new GameObject("ScorePopup");
        go.transform.position = worldPos + Vector3.up * rise;

        ScorePopup popup = go.AddComponent<ScorePopup>();
        popup.Build(amount);
    }

    private void Build(int amount)
    {
        if (built) return;
        built = true;

        startPos = transform.position;
        drift = Random.Range(-driftRange, driftRange);

        Font font = GetFont();
        if (font == null)
        {
            Debug.LogWarning("ScorePopup: 找不到可用字体，加分飘字不可用。");
            Destroy(gameObject);
            return;
        }

        string label = "+" + amount;

        // 主文字
        GameObject mainObj = new GameObject("Main");
        mainObj.transform.SetParent(transform, false);
        mainText = mainObj.AddComponent<TextMesh>();
        ConfigureText(mainText, font, label);
        mainRenderer = mainObj.GetComponent<Renderer>();
        if (mainRenderer != null) mainRenderer.sortingOrder = 95; // 地鼠之上、锤子(100)之下

        // 深色描边（同一段文字错开一点画在下面），草地背景上也看得清
        GameObject shadowObj = new GameObject("Shadow");
        shadowObj.transform.SetParent(transform, false);
        shadowText = shadowObj.AddComponent<TextMesh>();
        ConfigureText(shadowText, font, label);
        shadowRenderer = shadowObj.GetComponent<Renderer>();
        if (shadowRenderer != null) shadowRenderer.sortingOrder = 94;

        mainColor = Gold;
        shadowColor = Shadow;
        mainText.color = mainColor;
        shadowText.color = shadowColor;

        Normalize();
    }

    private static void ConfigureText(TextMesh tm, Font font, string label)
    {
        tm.font = font;
        tm.text = label;
        tm.fontSize = 96;
        tm.fontStyle = FontStyle.Bold;
        tm.characterSize = 0.1f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.richText = false;
    }

    /// <summary>把文字缩放到统一的世界高度，并给描边一个随文字大小变化的偏移</summary>
    private void Normalize()
    {
        if (mainRenderer == null) return;

        transform.localScale = Vector3.one;

        float h = mainRenderer.bounds.size.y;
        if (h <= 0.0001f) return; // 网格还没生成好，下一帧再试

        targetScale = textHeight / h;
        transform.localScale = Vector3.one * targetScale;

        if (shadowText != null)
        {
            // 偏移按文字自身高度换算，缩放后依然是"一点点"
            float offset = h * 0.045f;
            shadowText.transform.localPosition = new Vector3(offset, -offset, 0f);
        }

        normalized = true;
    }

    private void Update()
    {
        if (!built) return;

        life += Time.deltaTime;

        if (!normalized)
        {
            Normalize();
            // 网格还没生成好就等下一帧；等太久说明字体有问题，直接收掉，避免留下空节点
            if (!normalized)
            {
                if (life > 0.25f) Destroy(gameObject);
                return;
            }
        }

        float k = duration <= 0f ? 1f : life / duration;
        if (k >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        // 弹出：1.35 → 1.0，很快回落，模拟"跳一下"
        float pop = k < 0.18f
            ? Mathf.Lerp(1.35f, 1f, k / 0.18f)
            : 1f;

        // 上升：先快后慢
        float up = 1f - (1f - k) * (1f - k);
        Vector3 pos = startPos;
        pos.y += riseDistance * up;
        pos.x += drift * up;
        transform.position = pos;

        transform.localScale = Vector3.one * (targetScale * pop);

        // 后 40% 淡出
        float alpha = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
        mainText.color = new Color(mainColor.r, mainColor.g, mainColor.b, alpha);
        shadowText.color = new Color(shadowColor.r, shadowColor.g, shadowColor.b, alpha);
    }

    private static Font GetFont()
    {
        if (cachedFont != null) return cachedFont;

        // 优先用计分文字同一款字体，保证观感一致、且打包时字体一定被包含
        if (GameManager.instance != null && GameManager.instance.scoreText != null)
            cachedFont = GameManager.instance.scoreText.font;

        if (cachedFont == null)
        {
            try { cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { cachedFont = null; }
        }
        if (cachedFont == null)
        {
            try { cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch { cachedFont = null; }
        }

        return cachedFont;
    }
}
