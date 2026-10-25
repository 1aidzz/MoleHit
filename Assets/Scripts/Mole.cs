using System.Collections;
using UnityEngine;

public class Mole : MonoBehaviour
{
    public bool isHit = false; // 防止同一只地鼠重复计分

    #region 默认站位（洞口局部坐标）
    [Header("默认站位（洞口局部坐标）")]
    [Tooltip("相对洞口中心的左右偏移")]
    public float xOffset = 0f;
    [Tooltip("相对洞口中心上移的高度，需求：成为子物体后 Y 轴 +1")]
    public float heightOffset = 1f;
    #endregion

    #region 上浮动画
    [Header("上浮动画")]
    [Tooltip("从默认站位下方多远处开始上浮（局部单位，需足够大才能让地鼠完全藏在窗口下方）")]
    public float riseDistance = 3.4f;
    [Tooltip("上浮 / 下沉时长（秒）")]
    public float moveTime = 0.28f;
    #endregion

    #region 显示窗口（只有窗口内的部分会显示）
    [Header("显示窗口")]
    [Tooltip("正方形窗口边长，世界单位，需略大于地鼠本身")]
    public float windowSize = 2.4f;
    #endregion

    #region 命中判定
    [Header("命中判定")]
    [Tooltip("命中框使用与显示窗口完全等大的正方形。开启后：只有完全露出才可被打中；关闭则只要露出一点就能打")]
    public bool hitOnlyWhenFullyOut = true;
    #endregion

    #region 命中眩晕
    [Header("命中眩晕")]
    [Tooltip("击中后先左右摇晃并显示头顶眩晕图标的时长（秒），之后再缩回洞口")]
    public float dizzyTime = 0.5f;
    [Tooltip("摇晃幅度（局部单位）")]
    public float shakeAmplitude = 0.12f;
    [Tooltip("眩晕图标相对地鼠中心上移的高度（世界单位），觉得图标太高/太低就调这个")]
    public float dizzyRise = 1.9f;
    #endregion

    #region 受击表情（哭哭脸）
    [Header("受击表情")]
    [Tooltip("被打中后换成的哭哭脸。留空则自动读取 Assets/Resources/Mole/mole_hit（不带扩展名）")]
    public Sprite hitSprite;
    [Tooltip("哭哭脸大小倍数。1 = 自动缩放成和普通地鼠一样大（推荐），0.8 更小、1.2 更大。\n" +
             "不需要自己算画布比例：代码会按两张图的实际图案尺寸自动换算")]
    public float hitSpriteScale = 1f;
    [Tooltip("哭哭脸图片的 Resources 路径（不要写扩展名）")]
    public const string HitSpriteResourcePath = "Mole/mole_hit";
    #endregion

    private SpriteRenderer spriteRenderer;
    private SpriteRectClipper clipper;
    private BoxCollider2D hitBox;
    private Vector3 restLocalPos;    // 默认站位
    private Vector3 hiddenLocalPos;  // 藏在窗口下方时的位置
    private Coroutine moveCoroutine;

    // 换表情时要用到：初始化时记下排序与色调
    private Sprite baseSprite;
    private Color spriteTint = Color.white;
    private int spriteSortingLayer;
    private int spriteSortingOrder;

    private static Sprite cachedHitSprite;
    private static bool triedLoadHitSprite;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        RefreshPositions();
    }

    private void RefreshPositions()
    {
        restLocalPos = new Vector3(xOffset, heightOffset, 0f);
        hiddenLocalPos = restLocalPos + Vector3.down * riseDistance;
    }

    /// <summary>
    /// 由生成器调用：把地鼠裁剪到"正方形窗口"内（超出窗口立即不显示），
    /// 并统一排序层级，保证它画在洞口之上、锤子之下。
    /// </summary>
    public void Setup(int sortingLayerValue, int sortingOrder)
    {
        RefreshPositions();
        BuildHitBox();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null || spriteRenderer.sprite == null) return;

        Sprite sprite = spriteRenderer.sprite;
        Color tint = spriteRenderer.color;

        baseSprite = sprite;
        spriteTint = tint;
        spriteSortingLayer = sortingLayerValue;
        spriteSortingOrder = sortingOrder;

        if (clipper == null)
        {
            clipper = GetComponent<SpriteRectClipper>();
            if (clipper == null) clipper = gameObject.AddComponent<SpriteRectClipper>();
        }
        clipper.spriteScale = 1f;

        // 只有在裁剪渲染确实搭起来之后才关掉原 SpriteRenderer，
        // 万一初始化失败也还能看见地鼠，不会变成"什么都不显示"
        if (clipper.Setup(sprite, tint, sortingLayerValue, sortingOrder))
        {
            clipper.SetClipRect(ComputeWindowRect());
            spriteRenderer.enabled = false;
        }
        else
        {
            clipper = null;
        }
    }

    /// <summary>
    /// 以「默认站位」为窗口中心算出显示窗口（世界坐标）。
    /// 用父物体（洞口）换算，自动适配洞口的 0.7 缩放。
    /// </summary>
    private Rect ComputeWindowRect()
    {
        Vector3 worldCenter = GetWindowCenterWorld();

        return new Rect(
            worldCenter.x - windowSize * 0.5f,
            worldCenter.y - windowSize * 0.5f,
            windowSize,
            windowSize);
    }

    /// <summary>
    /// 窗口中心的世界坐标。以父物体（洞口）换算，自动适配洞口的 0.7 缩放。
    /// 命中框也锚定在这个点上，因此它固定在空中，不随地鼠上下移动。
    /// </summary>
    private Vector3 GetWindowCenterWorld()
    {
        return transform.parent != null
            ? transform.parent.TransformPoint(restLocalPos)
            : transform.TransformPoint(restLocalPos);
    }

    #region 方形命中框

    /// <summary>
    /// 把贴合精灵轮廓的碰撞体换成与显示窗口等大的正方形碰撞体。
    /// 碰撞体必须挂在本体上：InputManager 用 hit.collider.GetComponent&lt;Mole&gt;() 取地鼠。
    /// </summary>
    private void BuildHitBox()
    {
        PolygonCollider2D polygon = GetComponent<PolygonCollider2D>();

        // 移除所有非方形的碰撞体（原本贴合精灵的多边形碰撞体就在其中）
        Collider2D[] colliders = GetComponents<Collider2D>();
        foreach (Collider2D c in colliders)
        {
            if (c != null && !(c is BoxCollider2D))
            {
                // Destroy 要等到帧末才真正生效，先关掉让它当场退出物理查询，
                // 否则本帧内旧的多边形碰撞体仍可能被射线打到
                c.enabled = false;
                Destroy(c);
            }
        }

        hitBox = GetComponent<BoxCollider2D>();
        if (hitBox == null) hitBox = gameObject.AddComponent<BoxCollider2D>();
        hitBox.isTrigger = polygon != null && polygon.isTrigger;
        hitBox.enabled = false;
        UpdateHitBox();
    }

    /// <summary>
    /// 每帧把方框对齐到窗口：尺寸换算成世界单位，位置锚定在窗口中心。
    /// 因为地鼠自己会上下移动，偏移量必须用 worldToLocalMatrix 反算，
    /// 直接写 offset = 0 会让方框跟着地鼠跑。
    /// </summary>
    private void LateUpdate()
    {
        UpdateHitBox();
    }

    private void UpdateHitBox()
    {
        if (hitBox == null) return;

        Vector3 lossy = transform.lossyScale;
        float sx = Mathf.Abs(lossy.x) < 0.0001f ? 1f : Mathf.Abs(lossy.x);
        float sy = Mathf.Abs(lossy.y) < 0.0001f ? 1f : Mathf.Abs(lossy.y);
        hitBox.size = new Vector2(windowSize / sx, windowSize / sy);

        Vector3 delta = GetWindowCenterWorld() - transform.position;
        Vector3 local = transform.worldToLocalMatrix.MultiplyVector(delta);
        hitBox.offset = new Vector2(local.x, local.y);
    }

    private void SetHitActive(bool active)
    {
        if (hitBox != null) hitBox.enabled = active && gameObject.activeSelf;
    }

    #endregion

    /// <summary>显示地鼠：从窗口下方连续上浮到默认站位</summary>
    public void Show()
    {
        RefreshPositions();
        gameObject.SetActive(true);
        isHit = false;
        RestoreBaseSprite();
        transform.localPosition = hiddenLocalPos;
        UpdateHitBox();
        StopMove();
        moveCoroutine = StartCoroutine(RiseAndArm());
    }

    private IEnumerator RiseAndArm()
    {
        // 上浮途中还没露出（或只露一点），先不开放命中
        SetHitActive(!hitOnlyWhenFullyOut);
        yield return MoveTo(restLocalPos, moveTime);
        SetHitActive(true);
    }

    /// <summary>立即隐藏</summary>
    public void Hide()
    {
        StopMove();
        SetHitActive(false);
        RefreshPositions();
        transform.localPosition = hiddenLocalPos;
        gameObject.SetActive(false);
    }

    /// <summary>缩回窗口下方再隐藏，给地鼠一个自然的退场</summary>
    public IEnumerator Retreat()
    {
        if (isHit) yield break; // 已经被打掉了
        StopMove();
        SetHitActive(false);
        RefreshPositions();
        yield return MoveTo(hiddenLocalPos, moveTime);
        gameObject.SetActive(false);
    }

    /// <summary>被击中调用</summary>
    public void Hit()
    {
        if (isHit) return;
        isHit = true;
        SetHitActive(false);
        StopMove();

        // 立刻换成哭哭脸，后面摇晃/眩晕都基于这张图
        ApplyHitFace();

        Vector3 center = GetWindowCenterWorld();

        // 命中点爆闪星形特效
        HitEffect.Spawn(center);

        // 头顶眩晕图标（图片来源见 DizzyEffect）
        DizzyEffect.Spawn(center, dizzyRise);

        // 连杀播报：显示击杀图标 + 播放语音（同时让本局连杀数 +1）
        KillAnnouncementManager.NotifyKill();

        // 分数按当前连杀倍率结算：连杀越长，单次得分越高。
        // 用返回的实际得分弹「+N」，保证飘字和真实加分一致（含倍率）。
        int gained = GameManager.instance.AddScore(10);
        ScorePopup.Spawn(center, gained);

        // 先晕一下再退场，让"打晕"的过程看得见
        StartCoroutine(DizzyThenHide());
    }

    /// <summary>左右摇晃一小会儿，然后收进洞口</summary>
    private IEnumerator DizzyThenHide()
    {
        float duration = Mathf.Max(0.05f, dizzyTime);
        Vector3 start = transform.localPosition;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            float amp = shakeAmplitude * (1f - k);
            transform.localPosition = start + Vector3.right * (Mathf.Sin(t * 38f) * amp);
            yield return null;
        }
        transform.localPosition = start;
        Hide();
    }

    #region 受击表情
    /// <summary>
    /// 换成哭哭脸。优先级：Mole 上手动指定的 hitSprite → Resources/Mole/mole_hit。
    /// 两者都没有时安静返回，保持原图。
    /// </summary>
    private void ApplyHitFace()
    {
        Sprite face = hitSprite != null ? hitSprite : LoadHitSprite();
        if (face == null) return;

        float autoScale = ComputeHitFaceScale(face);

        if (clipper != null)
        {
            clipper.spriteScale = Mathf.Max(0.0001f, autoScale * hitSpriteScale);
            if (clipper.Setup(face, spriteTint, spriteSortingLayer, spriteSortingOrder))
            {
                clipper.SetClipRect(ComputeWindowRect());
                return;
            }
            clipper.spriteScale = 1f;
        }

        // 没有裁剪器（或重建失败）时直接换原 SpriteRenderer 的图
        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = face;
            spriteRenderer.enabled = true;
        }
    }

    /// <summary>
    /// 算出"让哭哭脸看起来和普通地鼠一样大"所需的缩放。
    /// 按两张图实际图案（忽略透明留白）的最长边比例换算，
    /// 这样无论哭哭脸画布是 380x383 还是 2048x2048 都不用管。
    /// </summary>
    private float ComputeHitFaceScale(Sprite face)
    {
        if (baseSprite == null) return 1f;

        Vector2 baseSize, baseCenter;
        Vector2 faceSize, faceCenter;
        SpriteOpaqueBounds.GetContentRect(baseSprite, out baseSize, out baseCenter);
        SpriteOpaqueBounds.GetContentRect(face, out faceSize, out faceCenter);

        float baseExtent = Mathf.Max(baseSize.x, baseSize.y);
        float faceExtent = Mathf.Max(faceSize.x, faceSize.y);
        if (faceExtent < 0.0001f) return 1f;

        return baseExtent / faceExtent;
    }

    private static Sprite LoadHitSprite()
    {
        if (!triedLoadHitSprite)
        {
            triedLoadHitSprite = true;
            cachedHitSprite = Resources.Load<Sprite>(HitSpriteResourcePath);
            if (cachedHitSprite == null)
                Debug.Log("Mole: 没找到哭哭脸 " + HitSpriteResourcePath + "，被打中时继续显示原图。");
        }
        return cachedHitSprite;
    }

    /// <summary>把地鼠恢复成普通表情（地鼠对象被复用时才会用到，生成器目前每次都是新实例）</summary>
    private void RestoreBaseSprite()
    {
        if (baseSprite == null || clipper == null) return;
        clipper.spriteScale = 1f;
        if (clipper.Setup(baseSprite, spriteTint, spriteSortingLayer, spriteSortingOrder))
            clipper.SetClipRect(ComputeWindowRect());
    }
    #endregion

    private void StopMove()
    {
        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
            moveCoroutine = null;
        }
    }

    private IEnumerator MoveTo(Vector3 targetLocal, float duration)
    {
        if (duration <= 0f)
        {
            transform.localPosition = targetLocal;
            moveCoroutine = null;
            yield break;
        }

        Vector3 start = transform.localPosition;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            transform.localPosition = Vector3.Lerp(start, targetLocal, k);
            yield return null;
        }
        transform.localPosition = targetLocal;
        moveCoroutine = null;
    }
}
