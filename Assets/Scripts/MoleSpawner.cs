using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地鼠生成器。
///
/// 与旧版的区别：旧版是「生成一只 → 等它停留 → 等它缩回 → 再生成下一只」的串行流程，
/// 物理上不可能同时存在两只地鼠。现在改成「生成节奏」和「每只地鼠自己的一生」分离：
/// 生成循环只负责按间隔放地鼠，每只地鼠有自己的生命周期协程，
/// 于是同屏数量可以大于 1（由 DynamicDifficulty 决定上限，默认 1，行为与旧版一致）。
/// </summary>
public class MoleSpawner : MonoBehaviour
{
    public static MoleSpawner instance;

    [Header("引用")]
    public GameObject molePrefab;
    public Transform[] holeTransforms; // 把场景中所有洞口拖进来

    [Header("基础生成配置（最简单档，Inspector可调整）")]
    [Tooltip("生成间隔下限：这是「同屏 1 只」时的基准间隔，难度提高后会按同屏上限换算成更密的节奏")]
    public float spawnIntervalMin = 1.2f;
    [Tooltip("生成间隔上限：同上，与下限一起随机")]
    public float spawnIntervalMax = 2.2f;
    [Tooltip("地鼠露出（停留）时长基准值：难度提高后按比例缩短，但不会低于 DynamicDifficulty.minStayTime")]
    public float moleStayTime = 1.2f;

    [Header("动态难度")]
    [Tooltip("开启后由 DynamicDifficulty 按滚动得分率自动提高同屏数量、缩短停留与间隔；关闭则永远用上面的基础参数（同屏 1 只）")]
    public bool useDynamicDifficulty = true;

    [Header("地鼠层级")]
    [Tooltip("地鼠使用的排序值：需高于洞口（0），低于锤子（100）")]
    public int moleSortingOrder = 5;

    private const int DefaultSortingLayerValue = 0; // 本项目都在 Default 排序层

    /// <summary>一只在场的地鼠及其生命周期句柄</summary>
    private class ActiveMole
    {
        public GameObject obj;
        public Mole mole;
        public Coroutine lifeRoutine;
        public int holeIndex;
        public int id;
    }

    private Coroutine spawnCoroutine;
    private DynamicDifficulty difficulty;

    private readonly List<ActiveMole> active = new List<ActiveMole>(8);
    private readonly List<int> freeHoles = new List<int>(8);

    private int[] holeOccupant; // 每个洞口当前被哪只地鼠占着（0 = 空闲），避免两只地鼠叠在同一个洞
    private int nextMoleId = 1;
    private int lastHoleIndex = -1;

    private void Awake()
    {
        if (instance == null) instance = this;
        EnsureDifficulty();
    }

    #region 生命周期
    // 启动地鼠生成循环
    public void StartSpawn()
    {
        if (spawnCoroutine != null) StopCoroutine(spawnCoroutine);

        ClearAllMoles();     // 清掉上一局可能残留的地鼠
        EnsureDifficulty();
        EnsureHoleSlots();

        lastHoleIndex = -1;
        spawnCoroutine = StartCoroutine(SpawnLoop());
    }

    // 停止生成，隐藏并销毁本生成器放出去的全部地鼠
    public void StopSpawn()
    {
        if (spawnCoroutine != null)
        {
            StopCoroutine(spawnCoroutine);
            spawnCoroutine = null;
        }

        ClearAllMoles();

        // 兜底：场景里若有别的来源的 Mole（旧版本残留），也一并藏起来
        Mole[] allMoles = FindObjectsOfType<Mole>();
        foreach (var m in allMoles)
        {
            if (m != null) m.Hide();
        }
    }
    #endregion

    #region 生成循环
    IEnumerator SpawnLoop()
    {
        while (GameManager.instance.currentState == GameState.Playing)
        {
            // 同屏上限每拍都重新读：难度提高后下一拍就能多放一只
            int cap = CurrentMaxConcurrent;
            TrySpawn(cap);

            // 停留时长与间隔都在生成那一刻取一次，
            // 已经在场的地鼠不会因为难度变化而中途被"掐短"，手感更稳
            yield return new WaitForSeconds(NextInterval(cap));
        }
    }

    private void TrySpawn(int cap)
    {
        if (active.Count >= cap) return;        // 已到同屏上限
        if (molePrefab == null) return;

        Transform hole;
        int holeIndex = PickFreeHole(out hole);
        if (holeIndex < 0 || hole == null) return;

        // 实例化地鼠，作为洞口的子物体
        GameObject moleObj = Instantiate(molePrefab, hole.position, Quaternion.identity, hole);
        Mole mole = moleObj.GetComponent<Mole>();
        if (mole == null)
        {
            Destroy(moleObj);
            return;
        }

        mole.Setup(DefaultSortingLayerValue, moleSortingOrder);
        mole.Show(); // 从窗口下方上浮出来

        ActiveMole entry = new ActiveMole
        {
            obj = moleObj,
            mole = mole,
            holeIndex = holeIndex,
            id = nextMoleId++
        };

        holeOccupant[holeIndex] = entry.id;
        lastHoleIndex = holeIndex;
        active.Add(entry);

        entry.lifeRoutine = StartCoroutine(MoleLife(entry, NextStayTime()));
    }

    /// <summary>
    /// 一只地鼠的完整一生：露出 stay 秒 → 没被打中就缩回（连杀中断）→ 释放洞口并销毁。
    /// 每只地鼠各跑一份，所以多只地鼠可以同时在场上。
    /// </summary>
    private IEnumerator MoleLife(ActiveMole entry, float stay)
    {
        yield return new WaitForSeconds(stay);

        Mole mole = entry.mole;

        if (mole != null && !mole.isHit)
        {
            // 漏掉一只地鼠：连杀中断，击杀播报重新从 head shot 开始、得分倍率归 1
            KillAnnouncementManager.NotifyMiss();
            yield return mole.Retreat();
        }
        else if (mole != null)
        {
            // 已被打中：等它把眩晕/缩回演完再销毁，别在半途把它凭空删掉
            float deadline = Time.time + Mathf.Max(0.1f, mole.dizzyTime) + mole.moveTime + 0.3f;
            while (mole != null && mole.gameObject.activeSelf && Time.time < deadline)
            {
                yield return null;
            }
        }

        ReleaseMole(entry);
    }

    /// <summary>释放洞口占用、从场列表移除并销毁对象</summary>
    private void ReleaseMole(ActiveMole entry)
    {
        if (entry == null) return;

        if (holeOccupant != null && entry.holeIndex >= 0 && entry.holeIndex < holeOccupant.Length
            && holeOccupant[entry.holeIndex] == entry.id)
        {
            holeOccupant[entry.holeIndex] = 0;
        }

        active.Remove(entry);
        entry.lifeRoutine = null;

        if (entry.obj != null) Destroy(entry.obj);
    }

    private void ClearAllMoles()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            ActiveMole entry = active[i];
            if (entry == null) continue;

            if (entry.lifeRoutine != null) StopCoroutine(entry.lifeRoutine);
            if (entry.mole != null) entry.mole.Hide();
            if (entry.obj != null) Destroy(entry.obj);
        }

        active.Clear();
        ClearHoleSlots();
    }
    #endregion

    #region 洞口选择
    private void EnsureHoleSlots()
    {
        int n = holeTransforms == null ? 0 : holeTransforms.Length;
        if (holeOccupant == null || holeOccupant.Length != n)
        {
            holeOccupant = new int[n];
        }
    }

    private void ClearHoleSlots()
    {
        if (holeOccupant == null) return;
        for (int i = 0; i < holeOccupant.Length; i++) holeOccupant[i] = 0;
    }

    /// <summary>随机挑一个空闲洞口，尽量避开上一只地鼠用过的洞，画面更活</summary>
    private int PickFreeHole(out Transform hole)
    {
        hole = null;

        int n = holeTransforms == null ? 0 : holeTransforms.Length;
        if (n == 0) return -1;
        EnsureHoleSlots();

        freeHoles.Clear();
        for (int i = 0; i < n; i++)
        {
            if (holeOccupant[i] == 0) freeHoles.Add(i);
        }
        if (freeHoles.Count == 0) return -1;

        if (freeHoles.Count > 1)
        {
            int lastPos = freeHoles.IndexOf(lastHoleIndex);
            if (lastPos >= 0) freeHoles.RemoveAt(lastPos);
        }

        int chosen = freeHoles[Random.Range(0, freeHoles.Count)];
        hole = holeTransforms[chosen];
        return chosen;
    }
    #endregion

    #region 难度参数读取
    private bool DifficultyActive
    {
        get { return useDynamicDifficulty && difficulty != null && difficulty.isActiveAndEnabled; }
    }

    private int CurrentMaxConcurrent
    {
        get { return DifficultyActive ? difficulty.GetMaxConcurrent() : 1; }
    }

    private float NextStayTime()
    {
        return DifficultyActive ? difficulty.GetStayTime(moleStayTime) : moleStayTime;
    }

    private float NextInterval(int concurrent)
    {
        if (DifficultyActive)
        {
            return difficulty.GetSpawnInterval(spawnIntervalMin, spawnIntervalMax, concurrent);
        }

        float wait = Random.Range(spawnIntervalMin, spawnIntervalMax);
        return Mathf.Max(0.05f, wait);
    }

    /// <summary>确保场景里有难度控制器；没有就自动挂一个（默认参数即五档方案）</summary>
    private void EnsureDifficulty()
    {
        difficulty = GetComponent<DynamicDifficulty>();

        if (!useDynamicDifficulty) return;

        if (difficulty == null)
        {
            difficulty = gameObject.AddComponent<DynamicDifficulty>();
        }
        if (difficulty != null) difficulty.spawner = this;
    }
    #endregion
}
