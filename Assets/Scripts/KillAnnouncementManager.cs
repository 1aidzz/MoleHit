using System.Collections;
using UnityEngine;

/// <summary>
/// 连杀播报系统：统计本局击杀数，每次击中地鼠时
/// 在场景上方（天空区域）显示对应击杀图标，并播放对应语音。
///
/// 第 1~8 次击杀依次为：
/// head shot / double kill / multi kill / ultra kill /
/// unbreakable / unbelievable / you wanna a piece of me? / come get some
/// 第 9 次起：播放普通击杀音效，但图标固定为 come get some。
///
/// 连杀：漏掉一只地鼠（它自己缩回洞口）会中断连杀，播报重新从 head shot 开始，
/// 得分倍率也随之归 1（倍率由 GameManager 按这里的 KillCount 计算）。
///
/// 图标与语音来自绿幕素材视频切出的资源，
/// 放在 Assets/Resources/KillAnnounce/ 下，运行时用 Resources.Load 加载，
/// 场景无需任何手动配置（本组件在第一次击杀时自动创建）。
/// </summary>
public class KillAnnouncementManager : MonoBehaviour
{
    public static KillAnnouncementManager instance;

    [Header("图标显示（世界坐标，正交视口高 10，顶部为 +5、底部为 -5）")]
    [Tooltip("图标中心点。放在屏幕中上方，避开下方的洞口与地鼠")]
    public Vector2 iconWorldPos = new Vector2(0f, 3.6f);
    [Tooltip("图标整体缩放倍数")]
    public float iconScale = 0.6f;
    [Tooltip("图标弹出用时（秒）")]
    public float popTime = 0.15f;
    [Tooltip("图标停留时长（秒）")]
    public float showDuration = 1.0f;
    [Tooltip("图标淡出用时（秒）")]
    public float fadeTime = 0.25f;

    /// <summary>击杀顺序对应的资源名（图标 icon_XXX / 语音 voice_XXX 同名）</summary>
    private static readonly string[] AnnouncementNames =
    {
        "headshot", "doublekill", "multikill", "ultrakill",
        "unbreakable", "unbelievable", "youwanna", "comegetsome"
    };

    private int killCount;                       // 本局连杀数（漏掉一只就清零）
    private Sprite[] icons;                      // 与 AnnouncementNames 对齐
    private AudioClip[] voices;                  // 与 AnnouncementNames 对齐
    private AudioClip normalKillVoice;           // 第 9 次及以后播的普通击杀音效
    private SpriteRenderer iconRenderer;
    private Coroutine showCoroutine;

    /// <summary>
    /// 当前连杀数：连续命中地鼠、中途没有漏掉的次数。
    /// 漏掉一只（地鼠自己缩回洞口）就会清零，下一只命中的播报重新从 head shot 开始。
    /// </summary>
    public static int KillCount
    {
        get { return instance != null ? instance.killCount : 0; }
    }

    /// <summary>每次击中地鼠后调用（Mole.Hit 触发）</summary>
    public static void NotifyKill()
    {
        if (instance == null)
        {
            GameObject go = new GameObject("KillAnnouncementManager");
            instance = go.AddComponent<KillAnnouncementManager>();
        }
        instance.OnKill();
    }

    /// <summary>
    /// 漏掉一只地鼠（它没被打中、自己缩回了洞口）：连杀中断，
    /// 击杀播报收起并归零，下一只命中重新从 head shot 开始。
    /// </summary>
    public static void NotifyMiss()
    {
        if (instance == null) return;
        instance.ResetStreak();
    }

    /// <summary>新一局开始时清零连杀并收起图标</summary>
    public static void ResetKills()
    {
        if (instance == null) return;
        instance.ResetStreak();
    }

    private void ResetStreak()
    {
        killCount = 0;
        if (showCoroutine != null)
        {
            StopCoroutine(showCoroutine);
            showCoroutine = null;
        }
        if (iconRenderer != null) iconRenderer.enabled = false;
    }

    private void Awake()
    {
        instance = this;
        LoadAssets();

        iconRenderer = gameObject.AddComponent<SpriteRenderer>();
        // 图标现在固定显示在屏幕中上方，不会与洞口/地鼠重叠，
        // 层级只要盖住背景(0)即可，仍留在地鼠(5)之下、锤子(100)之下
        iconRenderer.sortingOrder = 3;
        iconRenderer.enabled = false;
        iconRenderer.transform.localScale = Vector3.one * Mathf.Max(0.0001f, iconScale);
        transform.position = new Vector3(iconWorldPos.x, iconWorldPos.y, 0f);
    }

    private void LoadAssets()
    {
        int n = AnnouncementNames.Length;
        icons = new Sprite[n];
        voices = new AudioClip[n];

        for (int i = 0; i < n; i++)
        {
            icons[i] = Resources.Load<Sprite>($"KillAnnounce/icons/icon_{AnnouncementNames[i]}");
            voices[i] = Resources.Load<AudioClip>($"KillAnnounce/audio/voice_{AnnouncementNames[i]}");
            if (icons[i] == null)
                Debug.LogWarning($"KillAnnouncementManager: 缺少图标 KillAnnounce/icons/icon_{AnnouncementNames[i]}");
            if (voices[i] == null)
                Debug.LogWarning($"KillAnnouncementManager: 缺少语音 KillAnnounce/audio/voice_{AnnouncementNames[i]}");
        }

        normalKillVoice = Resources.Load<AudioClip>("KillAnnounce/audio/voice_normal");
        if (normalKillVoice == null)
            Debug.LogWarning("KillAnnouncementManager: 缺少普通击杀音效 KillAnnounce/audio/voice_normal");
    }

    private void OnKill()
    {
        killCount++;
        int idx = killCount - 1;

        Sprite icon;
        AudioClip voice;
        if (idx < AnnouncementNames.Length)
        {
            icon = icons[idx];
            voice = voices[idx];
        }
        else
        {
            // 之后都是普通击杀音效，但图标仍是 come get some
            icon = icons[AnnouncementNames.Length - 1];
            voice = normalKillVoice;
        }

        if (voice != null && AudioManager.instance != null)
            AudioManager.instance.PlayKillVoice(voice);

        if (icon != null)
        {
            if (showCoroutine != null) StopCoroutine(showCoroutine);
            showCoroutine = StartCoroutine(ShowIconRoutine(icon));
        }
    }

    /// <summary>图标弹出（带回弹）→ 停留 → 淡出</summary>
    private IEnumerator ShowIconRoutine(Sprite icon)
    {
        iconRenderer.sprite = icon;
        iconRenderer.enabled = true;
        transform.position = new Vector3(iconWorldPos.x, iconWorldPos.y, 0f);

        // 弹出：0.3 → 1.12 → 1.0 的回弹缩放，再乘上整体缩放 iconScale
        float baseScale = Mathf.Max(0.0001f, iconScale);
        float t = 0f;
        while (t < popTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / popTime);
            float scale = k < 0.7f
                ? Mathf.Lerp(0.3f, 1.12f, k / 0.7f)
                : Mathf.Lerp(1.12f, 1f, (k - 0.7f) / 0.3f);
            iconRenderer.transform.localScale = Vector3.one * (scale * baseScale);
            iconRenderer.color = Color.white;
            yield return null;
        }
        iconRenderer.transform.localScale = Vector3.one * baseScale;
        iconRenderer.color = Color.white;

        // 停留
        yield return new WaitForSeconds(showDuration);

        // 淡出
        t = 0f;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            float a = 1f - Mathf.Clamp01(t / fadeTime);
            iconRenderer.color = new Color(1f, 1f, 1f, a);
            yield return null;
        }
        iconRenderer.enabled = false;
        showCoroutine = null;
    }
}
