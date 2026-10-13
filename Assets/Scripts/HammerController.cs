using System.Collections;
using UnityEngine;

/// <summary>
/// 游戏内锤子光标：跟随鼠标/触点移动，以锤柄末端为轴挥动。
///
/// 判定点：静止（也是砸中到位）时锤头正好压在鼠标点上，
/// 即"鼠标指哪，锤头就在哪"，与原来的系统光标（热点在锤头）观感一致。
/// 动画：先短距离顺时针回拉蓄力 → 逆时针挥出、锤头压过判定点
/// → 短暂停顿 → 回弹到静止姿态。
/// </summary>
public class HammerController : MonoBehaviour
{
    [Header("尺寸设置")]
    [Tooltip("锤子在画面中的高度（世界单位），正交视口高约 10")]
    public float hammerWorldHeight = 2f;

    [Header("挥动动画")]
    [Tooltip("静止角度（度），同时也是砸中到位的角度：此时锤头正好落在鼠标点上")]
    public float restAngle = 0f;
    [Tooltip("蓄力角度（度）：挥击前先顺时针回拉，模拟蓄力")]
    public float windupAngle = 30f;
    [Tooltip("蓄力用时（秒）")]
    public float windupTime = 0.07f;
    [Tooltip("挥出用时（秒）")]
    public float swingOutTime = 0.07f;
    [Tooltip("挥过头角度（度）：砸中时稍稍压过判定点，再回弹到位")]
    public float smashOvershoot = 8f;
    [Tooltip("砸中后的停顿（秒）")]
    public float hitHoldTime = 0.05f;
    [Tooltip("回弹归位用时（秒）")]
    public float settleTime = 0.12f;

    [Header("旋转轴")]
    [Tooltip("锤柄末端（旋转轴）在贴图中的归一化坐标，左下角为原点")]
    public Vector2 handlePivot = new Vector2(0.78f, 0.05f);

    /// <summary>缓动方式</summary>
    private enum Ease { Smooth, In, Out }

    private SpriteRenderer spriteRenderer;
    private bool hasSprite;          // 贴图是否有效
    private Coroutine swingCoroutine;
    private Vector2 headOffset;      // 锤头（原光标热点）相对锤柄旋转轴的世界单位偏移

    /// <summary>
    /// 创建锤子对象。
    /// texture 直接复用 GameManager 已绑定的锤子贴图，
    /// hotspot 即锤头位置，用来保证击打点落在锤头上。
    /// </summary>
    public static HammerController Create(Texture2D texture, Vector2 hotspot)
    {
        GameObject go = new GameObject("HammerCursor");
        HammerController hammer = go.AddComponent<HammerController>();
        hammer.Setup(texture, hotspot);
        return hammer;
    }

    private void Setup(Texture2D texture, Vector2 hotspot)
    {
        spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sortingOrder = 100; // 永远画在最上层

        if (texture == null)
        {
            Debug.LogWarning("HammerController: 未绑定锤子贴图，游戏内锤子不可用，回退为系统光标。");
            return;
        }
        hasSprite = true;

        // 三线性过滤 + mipmap：锤子贴图原图为 933x1356，画面上只有约 200px，
        // 靠 mipmap 做降采样才能既清晰又不闪烁（导入设置里已开启 mipmap 与 Trilinear）
        texture.filterMode = FilterMode.Trilinear;

        // 通过 PPU 控制锤子在画面中的实际大小
        float ppu = texture.height / Mathf.Max(0.1f, hammerWorldHeight);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            handlePivot,   // 旋转轴 = 锤柄末端，挥动时锤柄几乎不动、锤头划弧
            ppu);
        sprite.name = "Hammer";
        spriteRenderer.sprite = sprite;

        // 锤头（原光标热点处）相对旋转轴的偏移，换算成世界单位。
        // hotspot 是"从图片左上角数"的像素坐标，先转成左下角原点的归一化坐标。
        headOffset = new Vector2(
            hotspot.x / texture.width - handlePivot.x,
            (1f - hotspot.y / texture.height) - handlePivot.y);
        headOffset = Vector2.Scale(headOffset,
            new Vector2(texture.width / ppu, texture.height / ppu));

        Hide(); // 默认隐藏，进入游戏时由 GameManager 调用 Show
    }

    private void Update()
    {
        if (!hasSprite || !spriteRenderer.enabled) return;

        // 手机端优先跟随触点，否则跟随鼠标
        Vector3 screenPos;
        if (Input.touchCount > 0)
            screenPos = Input.GetTouch(Input.touchCount - 1).position;
        else
            screenPos = Input.mousePosition;

        // 锚定：把旋转轴摆在"鼠标点 - 静止角度下锤头的偏移"处。
        // 因为静止角度就是砸中到位的角度，所以锤头始终压在鼠标点上（判定点=锤头）。
        transform.position = ScreenToWorld(screenPos) - Rotate(headOffset, restAngle);

        // 任意点击都触发挥动动画（与是否打中地鼠无关）
        // Unity 会把触摸模拟成鼠标左键，所以这里一个判断即可同时覆盖两端
        if (Input.GetMouseButtonDown(0))
        {
            Swing();
        }
    }

    /// <summary>把屏幕坐标换算到地鼠所在的 z=0 平面，并稍微前移避免被遮挡</summary>
    private Vector3 ScreenToWorld(Vector3 screenPos)
    {
        Camera cam = Camera.main;
        float dist = 10f;
        if (cam != null)
        {
            // 相机在 z=-10、朝 +z 看，取距离使落点在 z=0 平面
            dist = Mathf.Abs(cam.transform.position.z);
            if (dist < 0.1f) dist = 10f;
        }
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, dist));
        world.z -= 0.5f;
        return world;
    }

    private static Vector3 Rotate(Vector2 v, float degrees)
    {
        return Quaternion.Euler(0f, 0f, degrees) * (Vector3)v;
    }

    /// <summary>触发挥动：顺时针蓄力 → 逆时针挥出 → 停顿 → 回弹归位</summary>
    public void Swing()
    {
        if (!hasSprite) return;
        // 重复点击时打断上一次动画，从当前角度重新开始，保证连点手感
        if (swingCoroutine != null) StopCoroutine(swingCoroutine);
        swingCoroutine = StartCoroutine(SwingRoutine());
    }

    private IEnumerator SwingRoutine()
    {
        // 1) 蓄力：静止 → 顺时针回拉（快出慢收）
        yield return RotateTo(restAngle - windupAngle, windupTime, Ease.Out);

        // 2) 挥出：逆时针加速砸下，锤头压过鼠标点
        yield return RotateTo(restAngle + smashOvershoot, swingOutTime, Ease.In);

        // 3) 砸中停顿，让"砸中"有分量
        if (hitHoldTime > 0f) yield return new WaitForSeconds(hitHoldTime);

        // 4) 回弹到静止姿态（此时锤头重新压在鼠标点上）
        yield return RotateTo(restAngle, settleTime, Ease.Smooth);
        swingCoroutine = null;
    }

    /// <summary>把 localRotation 从当前角度平滑转到目标角度</summary>
    private IEnumerator RotateTo(float targetDeg, float duration, Ease ease)
    {
        float start = transform.localRotation.eulerAngles.z;
        // eulerAngles.z 落在 0~360（例如 -30° 会变成 330°），先归一到 -180~180
        if (start > 180f) start -= 360f;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            switch (ease)
            {
                case Ease.In: k = k * k; break;              // 加速砸下
                case Ease.Out: k = 1f - (1f - k) * (1f - k); break; // 快出慢收
                default: k = Mathf.SmoothStep(0f, 1f, k); break;
            }
            transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(start, targetDeg, k));
            yield return null;
        }
        transform.localRotation = Quaternion.Euler(0f, 0f, targetDeg);
    }

    /// <summary>进入游戏时调用：显示游戏内锤子，隐藏系统光标</summary>
    public void Show()
    {
        if (!hasSprite) return;
        spriteRenderer.enabled = true;
        Cursor.visible = false;
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        transform.localRotation = Quaternion.Euler(0f, 0f, restAngle);
        transform.position = ScreenToWorld(Input.mousePosition) - Rotate(headOffset, restAngle);
    }

    /// <summary>退出游戏时调用：隐藏锤子，恢复系统光标</summary>
    public void Hide()
    {
        if (spriteRenderer != null) spriteRenderer.enabled = false;
        transform.localRotation = Quaternion.Euler(0f, 0f, restAngle);
        if (swingCoroutine != null)
        {
            StopCoroutine(swingCoroutine);
            swingCoroutine = null;
        }
        Cursor.visible = true;
    }

    private void OnDisable()
    {
        // 兜底：对象被关闭时务必把系统光标还回来
        Cursor.visible = true;
    }
}
