using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoleSpawner : MonoBehaviour
{
    public static MoleSpawner instance;

    [Header("引用")]
    public GameObject molePrefab;
    public Transform[] holeTransforms; // 把场景中所有洞口拖进来

    [Header("生成配置，Inspector可调整")]
    public float spawnIntervalMin = 1.2f;
    public float spawnIntervalMax = 2.2f;
    public float moleStayTime = 1.2f;

    [Header("地鼠层级")]
    [Tooltip("地鼠使用的排序值：需高于洞口（0），低于锤子（100）")]
    public int moleSortingOrder = 5;

    private const int DefaultSortingLayerValue = 0; // 本项目都在 Default 排序层

    private Coroutine spawnCoroutine;

    private void Awake()
    {
        if (instance == null) instance = this;
    }

    // 启动地鼠生成循环
    public void StartSpawn()
    {
        if (spawnCoroutine != null) StopCoroutine(spawnCoroutine);
        spawnCoroutine = StartCoroutine(SpawnLoop());
    }

    // 停止生成，隐藏全部地鼠
    public void StopSpawn()
    {
        if (spawnCoroutine != null) StopCoroutine(spawnCoroutine);
        Mole[] allMoles = FindObjectsOfType<Mole>();
        foreach (var m in allMoles)
        {
            m.Hide();
        }
    }

    IEnumerator SpawnLoop()
    {
        while (GameManager.instance.currentState == GameState.Playing)
        {
            // 随机选一个洞口
            int randomIndex = Random.Range(0, holeTransforms.Length);
            Transform selectedHole = holeTransforms[randomIndex];

            // 实例化地鼠，作为洞口的子物体
            GameObject moleObj = Instantiate(molePrefab, selectedHole.position, Quaternion.identity, selectedHole);
            Mole mole = moleObj.GetComponent<Mole>();
            mole.Setup(DefaultSortingLayerValue, moleSortingOrder);
            mole.Show(); // 从窗口下方上浮出来

            // 停留一段时间隐藏
            yield return new WaitForSeconds(moleStayTime);

            // 没被打中就先缩回窗口下方，再销毁
            if (!mole.isHit)
            {
                yield return mole.Retreat();
            }
            Destroy(moleObj);

            // 等待下一次生成
            float wait = Random.Range(spawnIntervalMin, spawnIntervalMax);
            yield return new WaitForSeconds(wait);
        }
    }
}
