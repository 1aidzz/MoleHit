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

    private SpriteRenderer spriteRenderer;
    private SpriteRectClipper clipper;
    private BoxCollider2D hitBox;
    private Vector3 restLocalPos;    // 默认站位
    private Vector3 hiddenLocalPos;  // 藏在窗口下方时的位置
    private Coroutine moveCoroutine;

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

        if (clipper == null)
        {
            clipper = GetComponent<SpriteRectClipper>();
            if (clipper == null) clipper = gameObject.AddComponent<SpriteRectClipper>();
        }

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
        GameManager.instance.AddScore(10);
        Hide();
    }

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
