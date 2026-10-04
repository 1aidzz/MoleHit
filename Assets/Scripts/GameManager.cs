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

        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
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

        gameOverPanel.SetActive(false);
        startScreen.SetActive(false);

        currentState = GameState.Playing;

        AudioManager.instance.PlayButtonSound();
        AudioManager.instance.StopBGM();
        AudioManager.instance.PlayGameBGM();
        Cursor.SetCursor(hammerCursor, cursorHotspot, CursorMode.Auto);

        MoleSpawner.instance.StartSpawn();
    }

    private void SetResultBackground()
    {
        resultBg.sprite = score >= winScoreLine ? winBg : failureBg;
        finalScoreText.text = score >= winScoreLine ? $"恭喜获胜！最终得分：{score}" : $"地鼠们得逞啦！最终得分：{score}";
    }

    public void GameOver()
    {
        currentState = GameState.GameOver;
        MoleSpawner.instance.StopSpawn();

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
    }

    public void RestartGame()
    {
        gameOverPanel.SetActive(false);
        startScreen.SetActive(true);

        AudioManager.instance.PlayButtonSound();
        AudioManager.instance.PlayMenuBGM();
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    private void OnDisable()
    {
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }
}
