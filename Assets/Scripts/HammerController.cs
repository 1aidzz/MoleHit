using System.Collections;
using UnityEngine;

/// <summary>
/// 游戏内锤子光标：跟随鼠标/触点移动，点击时逆时针旋转 90 度后平滑归位。
/// 说明：系统光标（Cursor.SetCursor）是一张静态位图，无法做旋转动画，
/// 因此游戏进行中改用游戏内 Sprite 模拟光标，贴图与热点与原系统光标完全一致。
/// </summary>
public class HammerController : MonoBehaviour
{
    [Header("尺寸设置")]
    [Tooltip("锤子在画面中的高度（世界单位），正交视口高约 10")]
    public float hammerWorldHeight = 2f;

    [Header("挥动动画")]
    [Tooltip("逆时针旋转角度（度），正角度即逆时针")]
    public float swingAngle = 90f;
    [Tooltip("挥出用时（秒）")]
    public float swingOutTime = 0.07f;
    [Tooltip("归位用时（秒）")]
    public float swingBackTime = 0.18f;

    private SpriteRenderer spriteRenderer;
    private bool hasSprite;          // 贴图是否有效
    private Coroutine swingCoroutine;

    /// <summary>
    /// 创建锤子对象。
    /// texture 直接复用 GameManager 已绑定的锤子贴图，
    /// hotspot 与原系统光标热点一致，保证击打点不偏移。
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

        // 原光标热点是"从图片左上角数"的像素坐标，
        // Sprite.Create 的 pivot 是"从左下角数"的归一化坐标，需要换算
        Vector2 pivot = new Vector2(
            hotspot.x / texture.width,
            1f - hotspot.y / texture.height);

        // 通过 PPU 控制锤子在画面中的实际大小
        float ppu = texture.height / Mathf.Max(0.1f, hammerWorldHeight);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            pivot,
            ppu);
        sprite.name = "Hammer";
        spriteRenderer.sprite = sprite;

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

        transform.position = ScreenToWorld(screenPos);

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

    /// <summary>触发挥动：逆时针转 swingAngle 度，再平滑归位</summary>
    public void Swing()
    {
        if (!hasSprite) return;
        // 重复点击时打断上一次动画，从当前角度重新开始，保证连点手感
        if (swingCoroutine != null) StopCoroutine(swingCoroutine);
        swingCoroutine = StartCoroutine(SwingRoutine());
    }

    private IEnumerator SwingRoutine()
    {
        Quaternion rest = Quaternion.identity;
        Quaternion target = Quaternion.Euler(0f, 0f, swingAngle);
        float t;

        // 挥出：0 → 90°（逆时针）
        t = 0f;
        while (t < swingOutTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / swingOutTime));
            transform.localRotation = Quaternion.Slerp(rest, target, k);
            yield return null;
        }
        transform.localRotation = target;

        // 归位：90° → 0
        t = 0f;
        while (t < swingBackTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / swingBackTime));
            transform.localRotation = Quaternion.Slerp(target, rest, k);
            yield return null;
        }
        transform.localRotation = rest;
        swingCoroutine = null;
    }

    /// <summary>进入游戏时调用：显示游戏内锤子，隐藏系统光标</summary>
    public void Show()
    {
        if (!hasSprite) return;
        spriteRenderer.enabled = true;
        Cursor.visible = false;
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        transform.position = ScreenToWorld(Input.mousePosition);
    }

    /// <summary>退出游戏时调用：隐藏锤子，恢复系统光标</summary>
    public void Hide()
    {
        if (spriteRenderer != null) spriteRenderer.enabled = false;
        transform.localRotation = Quaternion.identity;
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
