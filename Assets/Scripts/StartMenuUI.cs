using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 开始菜单控制器。
///
/// 这里的面板、按钮、滑条都不是运行时临时创建的，而是场景里的真实节点
/// （由 Assets/Editor/StartMenuUIBuilder.cs 的菜单项一次性生成），
/// 所以可以在 Inspector / Scene 视图里自由改外观和布局，也能单独拖动排序。
///
/// 如果场景里还没有这些节点（没执行过生成），引用都是空的，
/// 本脚本只作为空壳存在，不会报任何错。
/// </summary>
public class StartMenuUI : MonoBehaviour
{
    public static StartMenuUI instance { get; private set; }

    #region 场景节点引用（由编辑器脚本自动赋值）
    [Header("开始界面按钮")]
    public Button settingsButton;
    public Button infoButton;
    public Button quitButton;

    [Header("面板")]
    public GameObject settingsPanel;
    public GameObject infoPanel;

    [Header("面板返回按钮")]
    public Button settingsCloseButton;
    public Button infoCloseButton;

    [Header("音量滑条")]
    public Slider bgmSlider;
    public Slider sfxSlider;
    public TextMeshProUGUI bgmValueText;
    public TextMeshProUGUI sfxValueText;
    #endregion

    [Header("游戏说明文案")]
    [Tooltip("留空则用下面的默认文案；填了就以这里为准")]
    [TextArea(3, 6)]
    public string rulesOverride = "";

    [Tooltip("勾选后不再自动改写说明文字，可以完全自己在 Inspector / 场景里编辑 Rules 节点")]
    public bool keepRulesTextFromScene = false;

    /// <summary>
    /// 游戏说明正文。
    ///
    /// 注意：项目用的 TMP 字体（TextMesh Pro/Resources/Fonts & Materials/font SDF）
    /// 只收录了 116 个字符，超出范围的字符会渲染成空心方块（Unity 会打 CMissingCharacterWarning）。
    /// 所以这段文案是逐字对着字体字符表挑出来的，改动时请保持用字在这个集合内，
    /// 或者先走 Window → TextMeshPro → Font Asset Creator 用同一份 font.ttf 重新生成一份字更全的 SDF。
    /// </summary>
    public const string DefaultRules =
        "本局的时限为 60 秒：得分达到 30 分即为获胜，得分在 30 分以内即为出局。";

    private void Awake()
    {
        if (instance == null) instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Start()
    {
        Bind(settingsButton, OpenSettings);
        Bind(infoButton, OpenInfo);
        Bind(quitButton, QuitGame);
        Bind(settingsCloseButton, CloseSettings);
        Bind(infoCloseButton, CloseInfo);

        BindSlider(bgmSlider, true);
        BindSlider(sfxSlider, false);

        ApplyRulesText();

        CloseAll();
    }

    #region 游戏说明文案
    /// <summary>
    /// 把文案写回到说明面板里的 Rules 文本节点。
    /// 这样改文案只需要改脚本，不用再执行一遍「生成开始菜单 UI」才能让场景里的文字更新。
    /// </summary>
    private void ApplyRulesText()
    {
        if (keepRulesTextFromScene) return;

        TextMeshProUGUI rules = FindRulesText();
        if (rules == null) return;

        rules.text = string.IsNullOrEmpty(rulesOverride) ? DefaultRules : rulesOverride;
        rules.alignment = TextAlignmentOptions.Center; // 单段文字居中比原来的左上对齐更好看
    }

    private TextMeshProUGUI FindRulesText()
    {
        if (infoPanel == null) return null;

        Transform found = infoPanel.transform.Find("Window/Rules");
        if (found != null) return found.GetComponent<TextMeshProUGUI>();

        TextMeshProUGUI[] all = infoPanel.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == "Rules") return all[i];
        }
        return null;
    }
    #endregion

    #region 面板开关
    public void OpenSettings() { Open(settingsPanel, infoPanel); }

    public void OpenInfo() { Open(infoPanel, settingsPanel); }

    public void CloseSettings() { SetPanel(settingsPanel, false); PlayButton(); }

    public void CloseInfo() { SetPanel(infoPanel, false); PlayButton(); }

    /// <summary>关闭所有开场面板，由 GameManager 在开始游戏/返回菜单时调用</summary>
    public void CloseAll()
    {
        SetPanel(settingsPanel, false);
        SetPanel(infoPanel, false);
    }

    private void Open(GameObject target, GameObject other)
    {
        SetPanel(other, false);
        SetPanel(target, true);
        PlayButton();
    }

    private static void SetPanel(GameObject panel, bool visible)
    {
        if (panel != null && panel.activeSelf != visible) panel.SetActive(visible);
    }
    #endregion

    #region 结束游戏
    public void QuitGame()
    {
        PlayButton();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    #endregion

    #region 音量滑条
    private void BindSlider(Slider slider, bool isBgm)
    {
        if (slider == null) return;

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;

        AudioManager am = AudioManager.instance;
        float saved = isBgm
            ? (am != null ? am.BgmVolume : 0.6f)
            : (am != null ? am.SfxVolume : 0.8f);

        // 用无通知版本赋值，避免初始化时反过来把保存值覆盖掉
        slider.SetValueWithoutNotify(saved);
        UpdateValueText(isBgm, saved);

        bool bgm = isBgm;
        slider.onValueChanged.AddListener(delegate (float v) { OnVolumeChanged(bgm, v); });
    }

    private void OnVolumeChanged(bool isBgm, float value)
    {
        AudioManager am = AudioManager.instance;
        if (am == null) return;

        value = Mathf.Clamp01(value);
        if (isBgm) am.SetBgmVolume(value);
        else am.SetSfxVolume(value);

        UpdateValueText(isBgm, value);
    }

    private void UpdateValueText(bool isBgm, float value)
    {
        string text = Mathf.RoundToInt(value * 100f) + "%";
        if (isBgm)
        {
            if (bgmValueText != null) bgmValueText.text = text;
        }
        else
        {
            if (sfxValueText != null) sfxValueText.text = text;
        }
    }
    #endregion

    #region 工具方法
    /// <summary>
    /// 绑定按钮。
    /// 这些按钮是新建的、本身没有任何持久化回调，不存在「一次点击触发两件事」的问题。
    /// </summary>
    private static void Bind(Button button, UnityAction action)
    {
        if (button == null) return;
        button.onClick.AddListener(action);
    }

    private static void PlayButton()
    {
        if (AudioManager.instance != null) AudioManager.instance.PlayButtonSound();
    }
    #endregion
}
