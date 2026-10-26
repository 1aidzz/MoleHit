using UnityEngine;
using UnityEngine.UI;

public enum GameState
{
    Waiting,    // 等待开始
    Playing,    // 游戏进行中
    GameOver    // 游戏结束
}

public class GameManager : MonoBehaviour
{
    public static GameManager instance;

    #region UI引用
    [Header("UI引用")]
    public Text scoreText;          // 游戏内分数显示
    public Text timeText;           // 游戏内倒计时显示
    public GameObject gameOverPanel;// 结算面板整体
    public Text finalScoreText;     // 结算最终得分文本
    public GameObject startScreen;  // 开始界面整体
    #endregion

    #region 结算背景配置
    [Header("结算背景配置")]
    public Image resultBg;          // 结算背景组件
    public Sprite failureBg;        // 失败背景图
    public Sprite winBg;            // 胜利背景图
    #endregion

    #region 游戏规则参数
    [Header("游戏规则")]
    [Tooltip("一局游戏的总时长，单位：秒")]
    public float TotalTime = 60f;
    [Tooltip("达到该分数判定为胜利")]
    public int winScoreLine = 30;
    #endregion

    #region 连杀得分倍率
    [Header("连杀得分倍率")]
    [Tooltip("保持连杀时，每连续命中一只地鼠让得分倍率增加的数值（连杀第 1 只倍率为 1，漏掉一只则归 1 重算）")]
    public float multiplierPerKill = 0.1f;
    #endregion

    #region 鼠标光标设置
    [Header("鼠标光标设置")]
    public Texture2D hammerCursor;
    public Vector2 cursorHotspot = new Vector2(8, 8);
    #endregion

    #region 运行时状态
    [Header("运行时状态")]
    public int score = 0;
    public float timeLeft;
    public GameState currentState;
    #endregion

    private HammerController hammer;   // 游戏内锤子光标（支持挥动动画）

    private void Awake()
    {
        if (instance == null)
            instance = this;
    }

    void Start()
    {
        currentState = GameState.Waiting;

        scoreText.text = $"分数：{score}";
        timeText.text = $"剩余时间：{Mathf.Round(TotalTime)}";
        scoreTextBaseColor = scoreText.color;

        gameOverPanel.SetActive(false);
        startScreen.SetActive(true);
        ShowResultBg(false);

        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

        // 创建游戏内锤子（复用 hammerCursor 贴图与热点），替代系统光标以支持挥动动画
        hammer = HammerController.Create(hammerCursor, cursorHotspot);

        // 「游戏设置 / 游戏说明 / 结束游戏」是场景里的真实节点，
        // 由 Tools → 打地鼠 → 生成开始菜单 UI 生成一次即可；
        // 没生成过时 StartMenuUI.instance 为空，这里会安静地跳过。
        CloseStartMenuPanels();

        AttachHoverSounds();

        // 把「分数 / 剩余时间」两行文字挂到计分板下（生成计分板时已做好，
        // 这里兜底旧场景：不挂也行，只是加分的跳动不会带着文字一起动）
        EnsureScoreBoardParent();

        AudioManager.instance.PlayMenuBGM();
    }

    /// <summary>让分数/时间文字成为计分板的子节点，保留原有位置</summary>
    private void EnsureScoreBoardParent()
    {
        Transform board = FindScoreBoard();
        if (board == null) return;
        if (scoreText == null || timeText == null) return;
        if (scoreText.transform.parent == board) return;

        scoreText.transform.SetParent(board, true);
        timeText.transform.SetParent(board, true);
    }

    void Update()
    {
        if (currentState == GameState.Playing)
        {
            timeLeft -= Time.deltaTime;
            timeText.text = $"剩余时间：{Mathf.Round(timeLeft)}";

            if (timeLeft <= 0)
            {
                GameOver();
            }
        }
    }

    /// <summary>
    /// 当前得分倍率：连杀第 1 只 = 1，之后每多连一只 +multiplierPerKill。
    /// 连杀数由 KillAnnouncementManager 统计，漏掉一只地鼠会被清零，倍率随之回到 1。
    /// </summary>
    public float ScoreMultiplier
    {
        get
        {
            int streak = KillAnnouncementManager.KillCount;
            if (streak <= 1) return 1f;
            return 1f + (streak - 1) * multiplierPerKill;
        }
    }

    /// <summary>
    /// 加分（已含连杀倍率），返回本局实际加了多少分，供加分飘字显示。
    /// </summary>
    public int AddScore(int add)
    {
        int gained = Mathf.RoundToInt(add * ScoreMultiplier);
        score += gained;
        scoreText.text = $"分数：{score}";
        AudioManager.instance.PlayHitSound();
        PunchScoreBoard();
        return gained;
    }

    public void StartGame()
    {
        score = 0;
        timeLeft = TotalTime;

        scoreText.text = "分数：0";
        timeText.text = $"剩余时间：{Mathf.Round(timeLeft)}";

        CloseStartMenuPanels();

        gameOverPanel.SetActive(false);
        startScreen.SetActive(false);
        ShowResultBg(false);

        currentState = GameState.Playing;

        KillAnnouncementManager.ResetKills(); // 连杀播报从零开始计

        AudioManager.instance.StopBGM();
        AudioManager.instance.PlayGameBGM();

        if (hammer != null) hammer.Show();
        else Cursor.SetCursor(hammerCursor, cursorHotspot, CursorMode.Auto);

        // 新一局：把动态难度的滚动统计清零、复位到最简单档（分数已在上方归零）
        if (DynamicDifficulty.instance != null) DynamicDifficulty.instance.BeginSession();

        MoleSpawner.instance.StartSpawn();
    }

    private void SetResultBackground()
    {
        if (resultBg == null) return;
        resultBg.sprite = score >= winScoreLine ? winBg : failureBg;
        finalScoreText.text = score >= winScoreLine ? $"恭喜获胜！最终得分：{score}" : $"地鼠们得逞啦！最终得分：{score}";
    }

    /// <summary>
    /// 结算背景只在结算时才显示。
    /// 注意：Image 的 sprite 为 null 时，Unity 会用内置白色纹理填充，
    /// 于是在赋图之前它会一直是一个白框，必须显式关掉渲染。
    /// </summary>
    private void ShowResultBg(bool visible)
    {
        if (resultBg != null) resultBg.enabled = visible;
    }

    public void GameOver()
    {
        currentState = GameState.GameOver;
        MoleSpawner.instance.StopSpawn();

        // 停掉难度统计，参数回基准（下一局从简单档重新爬）
        if (DynamicDifficulty.instance != null) DynamicDifficulty.instance.EndSession();

        ShowResultBg(true);
        SetResultBackground();
        gameOverPanel.SetActive(true);

        // 结算只播放对应BGM，去掉短音效
        if (score >= winScoreLine)
        {
            AudioManager.instance.PlayWinBGM();
        }
        else
        {
            AudioManager.instance.PlayLoseBGM();
        }

        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        if (hammer != null) hammer.Hide();
    }

    public void RestartGame()
    {
        gameOverPanel.SetActive(false);
        startScreen.SetActive(true);
        ShowResultBg(false);
        CloseStartMenuPanels();

        AudioManager.instance.PlayButtonSound();
        AudioManager.instance.PlayMenuBGM();
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        if (hammer != null) hammer.Hide();
    }

    /// <summary>关闭可能还开着的设置/说明面板</summary>
    private void CloseStartMenuPanels()
    {
        if (StartMenuUI.instance != null) StartMenuUI.instance.CloseAll();
    }

    #region 计分板反馈

    private Coroutine boardPunch;
    private Color scoreTextBaseColor = Color.black;

    /// <summary>
    /// 加分时让左上角计分板轻轻"跳"一下，再闪一下颜色。
    /// 缩放的是整块底板（文字是它的子节点，会一起动），所以对齐不会错。
    /// </summary>
    private void PunchScoreBoard()
    {
        if (boardPunch != null) StopCoroutine(boardPunch);
        boardPunch = StartCoroutine(PunchScoreBoardRoutine());
    }

    private System.Collections.IEnumerator PunchScoreBoardRoutine()
    {
        Transform board = FindScoreBoard();
        if (board == null)
        {
            // 没有底板时退回只闪文字颜色
            yield return FlashScoreText();
            yield break;
        }

        Color flash = new Color(1f, 0.55f, 0.10f);

        float up = 0.09f;
        float down = 0.20f;

        float t = 0f;
        while (t < up)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / up);
            board.localScale = Vector3.one * Mathf.Lerp(1f, 1.07f, k);
            scoreText.color = Color.Lerp(scoreTextBaseColor, flash, k);
            yield return null;
        }

        t = 0f;
        while (t < down)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / down);
            board.localScale = Vector3.one * Mathf.Lerp(1.07f, 1f, k);
            scoreText.color = Color.Lerp(flash, scoreTextBaseColor, k);
            yield return null;
        }

        board.localScale = Vector3.one;
        scoreText.color = scoreTextBaseColor;
        boardPunch = null;
    }

    /// <summary>
    /// 找左上角的计分板节点。
    /// 不假设 ScoreText 一定是它的子节点（场景没重新生成时两者是兄弟），
    /// 所以从 ScoreText 往上找到 Canvas 再往下搜一次。
    /// </summary>
    private Transform FindScoreBoard()
    {
        if (scoreText == null) return null;

        // 正常情况下文字就挂在底板下面
        Transform parent = scoreText.transform.parent;
        if (parent != null && parent.name == "ScoreBoard") return parent;

        Transform root = scoreText.transform;
        while (root.parent != null) root = root.parent;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].name == "ScoreBoard") return all[i];

        return null;
    }

    private System.Collections.IEnumerator FlashScoreText()
    {
        Color flash = new Color(1f, 0.55f, 0.10f);

        float t = 0f;
        while (t < 0.09f)
        {
            t += Time.deltaTime;
            scoreText.color = Color.Lerp(scoreTextBaseColor, flash, Mathf.Clamp01(t / 0.09f));
            yield return null;
        }
        t = 0f;
        while (t < 0.20f)
        {
            t += Time.deltaTime;
            scoreText.color = Color.Lerp(flash, scoreTextBaseColor, Mathf.Clamp01(t / 0.20f));
            yield return null;
        }
        scoreText.color = scoreTextBaseColor;
        boardPunch = null;
    }

    #endregion

    /// <summary>
    /// 给开始界面上的所有按钮挂上「光标悬停即播放音效」的组件。
    /// 接口用（设置/说明/结束）那几个按钮已由 StartMenuUI 处理，这里主要覆盖「开始游戏」按钮，
    /// Attach 本身幂等，重复不会挂两次。
    /// </summary>
    private void AttachHoverSounds()
    {
        if (startScreen == null) return;

        Button[] buttons = startScreen.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            ButtonHoverSound.Attach(buttons[i]);
        }
    }

    private void OnDisable()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        if (hammer != null) hammer.Hide();
    }
}
