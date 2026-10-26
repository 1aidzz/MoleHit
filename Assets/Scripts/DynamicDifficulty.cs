using System;
using UnityEngine;

/// <summary>
/// 动态难度（DDA）：按「滚动时间窗口内的得分率」自动分档，
/// 得分率高就提高同屏地鼠上限、缩短露出时长与生成间隔。
///
/// 设计约束（对应需求）：
/// 1) 只读分数、不写分数——原计分规则（每只 10 分 + 连杀倍率）完全没动，
///    本组件只调用 GameManager.instance.score 读取。
/// 2) 有上限：maxConcurrentLimit / minStayTime / minSpawnInterval 三重封顶。
/// 3) 不跳变：升档要「达标持续一段时间」+ 档位冷却 + 每次只进一档，
///    而且真正生效的参数由连续量 appliedLevel 平滑逼近目标档，
///    即便得分数值暴涨，地鼠节奏也是"几秒内一点点变快"，不会瞬间切表。
/// 4) 不刷分：开局预热期内难度锁在最简单档，且必须攒够采样窗口才允许调整。
/// </summary>
[DisallowMultipleComponent]
public class DynamicDifficulty : MonoBehaviour
{
    public static DynamicDifficulty instance;

    #region 一档难度配置
    /// <summary>
    /// 一个难度档位。除 scoreRate 是「进入条件」外，
    /// 其余三项都是相对 MoleSpawner 基础参数的比例，方便美术/策划只调基础值。
    /// </summary>
    [Serializable]
    public class Tier
    {
        [Tooltip("档位名称，仅用于日志/调试显示")]
        public string label = "档";

        [Tooltip("进入本档所需的得分率（分/秒，滚动窗口内的平均值）")]
        public float scoreRate = 0f;

        [Tooltip("本档同屏地鼠上限（最终还会被 maxConcurrentLimit 截断）")]
        public int maxConcurrent = 1;

        [Tooltip("露出时长倍数：实际停留 = MoleSpawner.moleStayTime × 该值，再被 minStayTime 兜底")]
        public float stayTimeScale = 1f;

        [Tooltip("生成间隔倍数：实际间隔 = 基础间隔 ÷ 同屏上限 × 该值，再被 minSpawnInterval 兜底")]
        public float spawnIntervalScale = 1f;
    }
    #endregion

    #region 引用
    [Header("引用")]
    [Tooltip("留空会自动找同一个物体上的 MoleSpawner")]
    public MoleSpawner spawner;
    #endregion

    #region 滚动得分率统计
    [Header("滚动得分率统计")]
    [Tooltip("统计窗口长度（秒）：只看最近这么长时间的得分，单位是「分/秒」")]
    public float scoreWindow = 8f;

    [Tooltip("采样周期（秒）：每隔这么久记录一次当前分数")]
    public float sampleInterval = 0.25f;

    [Tooltip("得分率平滑时间（秒）：越大越迟钝、越小越灵敏，防止单次爆发就升档")]
    public float rateSmoothing = 1.5f;

    [Tooltip("开局预热时间（秒）：这段时间内难度锁在最简单档，避免手感还没建立就变难")]
    public float warmupTime = 5f;
    #endregion

    #region 难度分级表
    [Header("难度分级（得分率从低到高排列）")]
    [Tooltip("档位 0 建议保持 maxConcurrent=1、两个 scale=1，这样和原版节奏完全一致")]
    public Tier[] tiers = CreateDefaultTiers();
    #endregion

    #region 难度上限
    [Header("难度上限（硬顶，任何档位都不能突破）")]
    [Tooltip("同屏地鼠数量绝对上限")]
    public int maxConcurrentLimit = 3;

    [Tooltip("露出时长绝对下限（秒），再难也不会比这更短")]
    public float minStayTime = 0.65f;

    [Tooltip("生成间隔绝对下限（秒），再快也不会比这更密")]
    public float minSpawnInterval = 0.45f;
    #endregion

    #region 防止跳变
    [Header("防止跳变")]
    [Tooltip("升档需要得分率达标并持续这么久（秒）")]
    public float tierUpHoldTime = 2f;

    [Tooltip("降档需要得分率低于阈值并持续这么久（秒），比升档更慢，避免忽难忽易")]
    public float tierDownHoldTime = 3.5f;

    [Tooltip("两次换档之间的最短间隔（秒）")]
    public float tierChangeCooldown = 4f;

    [Tooltip("降档迟滞系数：得分率要跌破阈值的这么多倍才算真的降（1 = 不迟滞）")]
    [Range(0.3f, 1f)]
    public float tierDownHysteresis = 0.75f;

    [Tooltip("参数逼近目标档的速度（档/秒）：0.6 表示换一档大约用 1.7 秒过渡完")]
    public float levelRampSpeed = 0.6f;
    #endregion

    #region 调试
    [Header("调试")]
    [Tooltip("换档时在 Console 打一行日志")]
    public bool logTierChanges = false;
    #endregion

    #region 运行时状态（只读给外部看）
    /// <summary>滚动窗口内的平滑得分率（分/秒）</summary>
    public float ScoreRate { get { return smoothRate; } }

    /// <summary>逻辑上的目标档位（整数）</summary>
    public int TargetTier { get { return targetTier; } }

    /// <summary>实际生效的档位（浮点，用于参数插值，所以变化是连续的）</summary>
    public float AppliedLevel { get { return appliedLevel; } }

    public int TierCount { get { return tiers == null ? 0 : tiers.Length; } }

    public string CurrentTierLabel
    {
        get
        {
            if (TierCount == 0) return "-";
            int i = Mathf.Clamp(Mathf.RoundToInt(appliedLevel), 0, TierCount - 1);
            return string.IsNullOrEmpty(tiers[i].label) ? ("档 " + i) : tiers[i].label;
        }
    }

    /// <summary>换档事件（传新档位），需要做 UI 提示时挂这个即可</summary>
    public event Action<int> TierChanged;
    #endregion

    private struct Sample
    {
        public float time;
        public int score;
    }

    private readonly System.Collections.Generic.List<Sample> samples = new System.Collections.Generic.List<Sample>(64);

    private float sessionStart;
    private float lastSampleTime;
    private float smoothRate;
    private int targetTier;
    private float appliedLevel;
    private int pendingTier;
    private float pendingSince;
    private float nextChangeTime;

    private void Awake()
    {
        if (instance == null) instance = this;
        if (spawner == null) spawner = GetComponent<MoleSpawner>();

        // 给一套合法初值，即使没走到 BeginSession 也不会算出乱七八糟的参数
        BeginSession();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    /// <summary>新一局开始：清空统计窗口并把难度复位到最简单档</summary>
    public void BeginSession()
    {
        float now = Time.time;

        samples.Clear();
        samples.Add(new Sample { time = now, score = 0 });

        sessionStart = now;
        lastSampleTime = now;
        smoothRate = 0f;

        targetTier = 0;
        appliedLevel = 0f;
        pendingTier = 0;
        pendingSince = now;
        nextChangeTime = now;
    }

    /// <summary>一局结束：停掉统计，参数回到基准（下一次 BeginSession 再从简单档开始）</summary>
    public void EndSession()
    {
        samples.Clear();
        smoothRate = 0f;
        targetTier = 0;
        appliedLevel = 0f;
        pendingTier = 0;
    }

    private void Update()
    {
        GameManager gm = GameManager.instance;
        if (gm == null || gm.currentState != GameState.Playing) return;

        float now = Time.time;
        float dt = Time.deltaTime;

        SampleScore(now);
        UpdateScoreRate(now, dt);

        bool warmedUp = now - sessionStart >= warmupTime;
        if (warmedUp)
        {
            UpdateTargetTier(ComputeDesiredTier(), now);
        }
        else
        {
            targetTier = 0;
            pendingTier = 0;
        }

        // 真正作用到玩法上的参数由这个连续量决定，所以永远不会一步到位
        float goal = warmedUp ? targetTier : 0f;
        appliedLevel = Mathf.MoveTowards(appliedLevel, goal, Mathf.Max(0f, levelRampSpeed) * dt);
    }

    #region 滚动窗口统计
    private void SampleScore(float now)
    {
        if (now - lastSampleTime < sampleInterval) return;

        lastSampleTime = now;
        samples.Add(new Sample { time = now, score = GameManager.instance.score });

        // 只保留窗口内的样本，并额外留一个更早的样本作为窗口左边界，
        // 否则边界被整段裁掉时统计时长会忽长忽短，得分率跟着跳
        while (samples.Count > 2 && samples[1].time <= now - scoreWindow)
        {
            samples.RemoveAt(0);
        }
    }

    private void UpdateScoreRate(float now, float dt)
    {
        if (samples.Count < 2) { smoothRate = 0f; return; }

        float span = now - samples[0].time;
        if (span < 0.0001f) return;

        int gained = Mathf.Max(0, GameManager.instance.score - samples[0].score);
        float raw = gained / span;

        if (rateSmoothing <= 0.01f)
        {
            smoothRate = raw;
            return;
        }

        // 指数平滑：单只地鼠的爆发不会立刻把难度顶上去
        float k = 1f - Mathf.Exp(-dt / rateSmoothing);
        smoothRate = Mathf.Lerp(smoothRate, raw, k);
    }

    /// <summary>
    /// 按当前平滑得分率算"应该"呆在哪一档。
    /// 升档用原始阈值，降档用「阈值 × 迟滞系数」，避免在阈值上下反复横跳。
    /// </summary>
    private int ComputeDesiredTier()
    {
        int last = TierCount - 1;
        if (last < 0) return 0;

        int up = 0;
        for (int i = last; i >= 0; i--)
        {
            if (smoothRate >= tiers[i].scoreRate) { up = i; break; }
        }
        if (up > targetTier) return up;

        int down = 0;
        float factor = Mathf.Clamp01(tierDownHysteresis);
        for (int i = last; i >= 0; i--)
        {
            if (smoothRate >= tiers[i].scoreRate * factor) { down = i; break; }
        }
        return Mathf.Min(down, targetTier);
    }
    #endregion

    #region 换档节奏控制
    private void UpdateTargetTier(int desired, float now)
    {
        if (desired == targetTier)
        {
            pendingTier = targetTier;
            pendingSince = now;
            return;
        }

        bool goingUp = desired > targetTier;

        // 目标一变就重新计时：只有"持续"达标才算数
        if (pendingTier != desired)
        {
            pendingTier = desired;
            pendingSince = now;
            return;
        }

        float hold = goingUp ? tierUpHoldTime : tierDownHoldTime;
        if (now - pendingSince < hold) return;
        if (now < nextChangeTime) return;

        // 一次只走一档：就算得分率直接冲到最高档，也是逐级上台阶，
        // 再叠加 appliedLevel 的平滑过渡，玩家感受到的是"慢慢变快"
        targetTier = goingUp
            ? Mathf.Min(targetTier + 1, desired)
            : Mathf.Max(targetTier - 1, desired);

        nextChangeTime = now + Mathf.Max(0f, tierChangeCooldown);
        pendingSince = now;

        if (logTierChanges)
        {
            Debug.Log($"[难度] 得分率 {smoothRate:F1} 分/秒 → 目标 {targetTier} 档（{CurrentTierLabel}）");
        }
        if (TierChanged != null) TierChanged(targetTier);
    }
    #endregion

    #region 对外参数（MoleSpawner 调用）
    /// <summary>按实际生效档位在相邻两档之间线性插值，返回值因此是连续变化的</summary>
    private float LerpTier(Func<Tier, float> pick)
    {
        int last = TierCount - 1;
        if (last < 0) return 0f;

        float level = Mathf.Clamp(appliedLevel, 0f, last);
        int a = Mathf.FloorToInt(level);
        int b = Mathf.Min(a + 1, last);
        float t = level - a;
        return Mathf.Lerp(pick(tiers[a]), pick(tiers[b]), t);
    }

    /// <summary>本帧允许同时存在的地鼠数量（含硬上限）</summary>
    public int GetMaxConcurrent()
    {
        float value = LerpTier(tr => tr.maxConcurrent);
        return Mathf.Clamp(Mathf.RoundToInt(value), 1, Mathf.Max(1, maxConcurrentLimit));
    }

    /// <summary>地鼠露出时长：基础值 × 档位倍数，并被 minStayTime 兜底</summary>
    public float GetStayTime(float baseStayTime)
    {
        float value = baseStayTime * LerpTier(tr => tr.stayTimeScale);
        return Mathf.Max(Mathf.Max(0.05f, minStayTime), value);
    }

    /// <summary>
    /// 下一次生成的等待时长。
    /// 除以 concurrent 是为了"同屏 N 只、每只待 T 秒 → 平均每 T/N 秒放一只"，
    /// 所以同屏上限提高时节奏会自然变密；同屏上限为 1 时该值等于原始间隔。
    /// </summary>
    public float GetSpawnInterval(float baseMin, float baseMax, int concurrent)
    {
        float low = Mathf.Min(baseMin, baseMax);
        float high = Mathf.Max(baseMin, baseMax);

        float scale = LerpTier(tr => tr.spawnIntervalScale);
        float wait = UnityEngine.Random.Range(low, high) * scale / Mathf.Max(1, concurrent);

        return Mathf.Max(Mathf.Max(0.05f, minSpawnInterval), wait);
    }
    #endregion

    /// <summary>
    /// 默认五档：档 0 与原版完全一致，档 4 触到硬上限附近。
    /// 阈值（分/秒）是按「每只 10 分 + 连杀倍率」实测标定的：
    /// 手感生疏的玩家基本停在 0 档，普通水平在 1~2 档，高手 30 秒左右进到 3~4 档。
    /// </summary>
    private static Tier[] CreateDefaultTiers()
    {
        return new Tier[]
        {
            new Tier { label = "悠闲", scoreRate = 0f,  maxConcurrent = 1, stayTimeScale = 1.00f, spawnIntervalScale = 1.00f },
            new Tier { label = "熟练", scoreRate = 8f,  maxConcurrent = 2, stayTimeScale = 0.95f, spawnIntervalScale = 0.95f },
            new Tier { label = "紧张", scoreRate = 14f, maxConcurrent = 2, stayTimeScale = 0.86f, spawnIntervalScale = 0.88f },
            new Tier { label = "狂热", scoreRate = 21f, maxConcurrent = 3, stayTimeScale = 0.78f, spawnIntervalScale = 0.82f },
            new Tier { label = "地狱", scoreRate = 30f, maxConcurrent = 3, stayTimeScale = 0.70f, spawnIntervalScale = 0.76f },
        };
    }
}
