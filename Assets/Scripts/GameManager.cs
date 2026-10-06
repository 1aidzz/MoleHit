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

        AudioManager.instance.PlayMenuBGM();
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

    public void AddScore(int add)
    {
        score += add;
        scoreText.text = $"分数：{score}";
        AudioManager.instance.PlayHitSound();
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

        AudioManager.instance.PlayButtonSound();
        AudioManager.instance.StopBGM();
        AudioManager.instance.PlayGameBGM();

        if (hammer != null) hammer.Show();
        else Cursor.SetCursor(hammerCursor, cursorHotspot, CursorMode.Auto);

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

    private void OnDisable()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        if (hammer != null) hammer.Hide();
    }
}
