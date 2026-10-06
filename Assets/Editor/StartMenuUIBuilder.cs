#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// 在场景里生成「游戏设置 / 游戏说明 / 结束游戏」的真实 UI 节点。
///
/// 使用方式：
///   1. 回到 Unity，等待编译完成
///   2. 打开 WhackAMole 场景
///   3. 菜单栏 → Tools → 打地鼠 → 生成开始菜单 UI
///   4. Ctrl+S 保存场景
///
/// 生成出来的就是普通 uGUI 节点，可以在 Hierarchy / Inspector 里随便改，
/// 之后不再需要这段代码（重复执行会先清掉旧的再重建，所以也能当"重新生成"用）。
///
/// 之所以用 MenuItem 而不是直接改 .unity 文件的 YAML：
/// Unity 自己在位运行时会持有场景的内存副本，手写 YAML 很容易被它覆盖回去，
/// 由 Unity 自己创建节点则不会有这个冲突。
/// </summary>
public static class StartMenuUIBuilder
{
    private const string ControllerName = "MenuUIController";  // 挂脚本的节点，始终激活
    private const string PanelRootName = "MenuPanels";        // 面板容器
    private const string StartBgPath = "Assets/Sprites/Background/startBg.png";

    // 和现有「开始游戏」按钮一致的墨色
    private static readonly Color InkColor = new Color(0.196f, 0.196f, 0.196f);
    private static readonly Color TrackColor = new Color(0.16f, 0.13f, 0.09f, 0.9f);
    private static readonly Color FillColor = new Color(0.96f, 0.75f, 0.22f, 1f);

    // 文案统一放在 StartMenuUI.DefaultRules 里，避免两处改漏
    private const string GameRules = StartMenuUI.DefaultRules;

    [MenuItem("Tools/打地鼠/生成开始菜单 UI")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[StartMenuUIBuilder] 请先退出播放模式再生成，否则改动不会保存到场景里。");
            return;
        }

        Scene activeScene = EditorSceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
        {
            Debug.LogError("[StartMenuUIBuilder] 没有找到已加载的场景，请先打开 WhackAMole 场景。");
            return;
        }

        Canvas canvas = null;
        Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        foreach (Canvas c in canvases)
        {
            if (c != null && c.gameObject.scene == activeScene && c.name == "Canvas")
            {
                canvas = c;
                break;
            }
        }
        if (canvas == null)
        {
            Debug.LogError("[StartMenuUIBuilder] 场景里找不到名为 Canvas 的画布。");
            return;
        }

        Transform startScreen = canvas.transform.Find("StartScreen");
        Transform startBtn = startScreen != null ? startScreen.Find("StartBtn") : null;
        if (startBtn == null)
        {
            Debug.LogError("[StartMenuUIBuilder] 找不到 Canvas/StartScreen/StartBtn，无法复用样式。");
            return;
        }

        Image btnImage = startBtn.GetComponent<Image>();
        TextMeshProUGUI textTemplate = startBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (btnImage == null || textTemplate == null)
        {
            Debug.LogError("[StartMenuUIBuilder] StartBtn 上缺少 Image 或 TextMeshProUGUI，无法复用样式。");
            return;
        }

        // 记录一次整棵树的快照，生成完可以 Ctrl+Z 撤掉
        Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject, "生成开始菜单 UI");

        // 幂等：先清掉上一次生成的东西
        DestroyChildByName(canvas.transform, PanelRootName);
        DestroyChildByName(canvas.transform, ControllerName);
        DestroyChildByName(startScreen, "SettingsButton");
        DestroyChildByName(startScreen, "InfoButton");
        DestroyChildByName(startScreen, "QuitButton");

        // 直接沿用按钮上那张 Unity 内置 UI 贴图，不用去猜 Resources.GetBuiltinResource 的路径
        Sprite uiSprite = btnImage.sprite;
        Sprite windowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(StartBgPath);
        if (windowSprite == null)
        {
            Debug.LogWarning("[StartMenuUIBuilder] 没读到 " + StartBgPath +
                             "，面板窗口会缺少背景图，可以在 Inspector 里手动指定。");
        }

        StartMenuUI ui = CreateController(canvas.transform);
        GameObject buttonTemplate = startBtn.gameObject;
        Vector2 btnSize = startBtn.GetComponent<RectTransform>().sizeDelta;

        // ---- 1. 开始界面上的三个按钮，排在「开始游戏」下面 ----
        float firstY = -150f;
        float step = 50f;
        ui.settingsButton = MakeButton(buttonTemplate, startScreen, "SettingsButton", "游戏设置", 0f, firstY, btnSize);
        ui.infoButton = MakeButton(buttonTemplate, startScreen, "InfoButton", "游戏说明", 0f, firstY - step, btnSize);
        ui.quitButton = MakeButton(buttonTemplate, startScreen, "QuitButton", "结束游戏", 0f, firstY - step * 2f, btnSize);

        // ---- 2. 面板容器（放在 Canvas 最下面，保证画在所有东西上面）----
        GameObject panelRoot = NewUIObject(PanelRootName, canvas.transform);
        Stretch(panelRoot.GetComponent<RectTransform>());
        panelRoot.transform.SetAsLastSibling();

        // ---- 3. 两个面板 ----
        ui.settingsPanel = BuildSettingsPanel(panelRoot.transform, buttonTemplate, textTemplate, uiSprite, windowSprite, ui);
        ui.infoPanel = BuildInfoPanel(panelRoot.transform, buttonTemplate, textTemplate, uiSprite, windowSprite, ui);

        ui.settingsPanel.SetActive(false);
        ui.infoPanel.SetActive(false);

        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = panelRoot;

        Debug.Log("[StartMenuUIBuilder] 生成完成：开始界面按钮 + 设置面板 + 说明面板已写入场景，按 Ctrl+S 保存。");
    }

    #region 面板
    private static GameObject BuildSettingsPanel(Transform panelRoot, GameObject buttonTemplate, TextMeshProUGUI textTemplate,
                                                 Sprite uiSprite, Sprite windowSprite, StartMenuUI ui)
    {
        RectTransform window;
        GameObject panel = BuildWindow(panelRoot, "SettingsPanel", uiSprite, windowSprite, out window);

        AddTitle(textTemplate, window, "Title", "游戏设置", new Vector2(0f, 132f));

        DefaultControls.Resources res = MakeResources(uiSprite);

        AddRowLabel(textTemplate, window, "BgmLabel", "音乐音量", new Vector2(-205f, 35f));
        ui.bgmSlider = AddSlider(window, "BgmSlider", res, new Vector2(45f, 35f));
        ui.bgmValueText = AddValueText(textTemplate, window, "BgmValue", new Vector2(230f, 35f));

        AddRowLabel(textTemplate, window, "SfxLabel", "音效音量", new Vector2(-205f, -35f));
        ui.sfxSlider = AddSlider(window, "SfxSlider", res, new Vector2(45f, -35f));
        ui.sfxValueText = AddValueText(textTemplate, window, "SfxValue", new Vector2(230f, -35f));

        ui.settingsCloseButton = MakeButton(buttonTemplate, window, "CloseButton", "返回", 0f, -128f, new Vector2(170f, 46f));
        return panel;
    }

    private static GameObject BuildInfoPanel(Transform panelRoot, GameObject buttonTemplate, TextMeshProUGUI textTemplate,
                                             Sprite uiSprite, Sprite windowSprite, StartMenuUI ui)
    {
        RectTransform window;
        GameObject panel = BuildWindow(panelRoot, "InfoPanel", uiSprite, windowSprite, out window);

        AddTitle(textTemplate, window, "Title", "游戏说明", new Vector2(0f, 132f));
        AddBody(textTemplate, window, "Rules", GameRules, new Vector2(0f, 5f));

        ui.infoCloseButton = MakeButton(buttonTemplate, window, "CloseButton", "返回", 0f, -128f, new Vector2(170f, 46f));
        return panel;
    }

    private static GameObject BuildWindow(Transform parent, string name, Sprite overlaySprite, Sprite windowSprite,
                                          out RectTransform window)
    {
        GameObject panel = NewUIObject(name, parent);
        Stretch(panel.GetComponent<RectTransform>());

        // 全屏半透明遮罩：既能压暗背景，也能挡住下方按钮的点击
        Image overlay = panel.AddComponent<Image>();
        overlay.sprite = overlaySprite;
        overlay.type = Image.Type.Sliced;
        overlay.color = new Color(0f, 0f, 0f, 0.5f);
        overlay.raycastTarget = true;

        GameObject win = NewUIObject("Window", panel.transform);
        window = win.GetComponent<RectTransform>();
        AnchorCenter(window, new Vector2(660f, 360f), Vector2.zero);

        Image bg = win.AddComponent<Image>();
        bg.sprite = windowSprite;
        bg.type = Image.Type.Simple;
        bg.color = Color.white;
        bg.raycastTarget = true;

        return panel;
    }
    #endregion

    #region 通用构造
    private static StartMenuUI CreateController(Transform canvasRoot)
    {
        GameObject go = NewUIObject(ControllerName, canvasRoot);
        Stretch(go.GetComponent<RectTransform>());
        StartMenuUI ui = go.GetComponent<StartMenuUI>();
        if (ui == null) ui = go.AddComponent<StartMenuUI>();
        return ui;
    }

    private static GameObject NewUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go;
    }

    /// <summary>
    /// 用现有「开始游戏」按钮做模板克隆外观，然后换一个新的 Button 组件。
    /// 换组件这一步是关键：克隆会把模板上那条「StartGame」的持久化回调一起带来，
    /// 而 RemoveAllListeners() 清不掉持久化回调（它只清运行时加的），
    /// 于是点一下会既开页面又开始游戏。
    /// </summary>
    private static Button MakeButton(GameObject template, Transform parent, string name, string label,
                                     float x, float y, Vector2 size)
    {
        GameObject go = Object.Instantiate(template, parent, false);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.name = name;
        SetLayerRecursive(go.transform, parent.gameObject.layer);

        RectTransform rt = go.GetComponent<RectTransform>();
        Vector3 templateScale = template.GetComponent<RectTransform>().localScale;
        AnchorCenter(rt, size, new Vector2(x, y));
        rt.localScale = templateScale;

        Image img = go.GetComponent<Image>();
        img.color = Color.white;
        img.raycastTarget = true;

        Button old = go.GetComponent<Button>();
        Navigation nav = old != null ? old.navigation : new Navigation();
        ColorBlock colors = old != null ? old.colors : ColorBlock.defaultColorBlock;
        if (old != null) Undo.DestroyObjectImmediate(old);

        Button btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.ColorTint;
        btn.targetGraphic = img;
        btn.colors = colors;
        btn.navigation = nav;
        btn.interactable = true;

        TextMeshProUGUI text = go.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null)
        {
            text.text = label;
            text.raycastTarget = false;
        }

        return btn;
    }

    private static TextMeshProUGUI AddText(TextMeshProUGUI template, Transform parent, string name, string content,
                                           float fontSize, Color color, Vector2 size, Vector2 pos,
                                           TextAlignmentOptions alignment)
    {
        GameObject go = Object.Instantiate(template.gameObject, parent, false);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.name = name;
        SetLayerRecursive(go.transform, parent.gameObject.layer);

        TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
        t.text = content;
        t.color = color;
        t.fontSize = fontSize;
        t.fontSizeMax = fontSize * 2f;
        t.enableAutoSizing = false;
        t.alignment = alignment;
        t.enableWordWrapping = true;
        t.raycastTarget = false;

        AnchorCenter(go.GetComponent<RectTransform>(), size, pos);
        return t;
    }

    private static void AddTitle(TextMeshProUGUI template, Transform parent, string name, string content, Vector2 pos)
    {
        AddText(template, parent, name, content, 44f, InkColor, new Vector2(420f, 70f), pos, TextAlignmentOptions.Center);
    }

    private static void AddRowLabel(TextMeshProUGUI template, Transform parent, string name, string content, Vector2 pos)
    {
        AddText(template, parent, name, content, 28f, InkColor, new Vector2(220f, 44f), pos, TextAlignmentOptions.MidlineLeft);
    }

    private static TextMeshProUGUI AddValueText(TextMeshProUGUI template, Transform parent, string name, Vector2 pos)
    {
        return AddText(template, parent, name, "60%", 26f, InkColor, new Vector2(140f, 44f), pos, TextAlignmentOptions.MidlineRight);
    }

    private static void AddBody(TextMeshProUGUI template, Transform parent, string name, string content, Vector2 pos)
    {
        AddText(template, parent, name, content, 26f, InkColor, new Vector2(560f, 220f), pos, TextAlignmentOptions.TopLeft);
    }

    private static DefaultControls.Resources MakeResources(Sprite uiSprite)
    {
        DefaultControls.Resources res = new DefaultControls.Resources();
        res.standard = uiSprite;
        res.background = uiSprite;
        res.knob = uiSprite;
        return res;
    }

    /// <summary>直接复用 Unity 官方的滑条工厂，结构和它的菜单创建出来的一模一样</summary>
    private static Slider AddSlider(Transform parent, string name, DefaultControls.Resources res, Vector2 pos)
    {
        GameObject go = DefaultControls.CreateSlider(res);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.name = name;
        SetLayerRecursive(go.transform, parent.gameObject.layer);
        go.transform.SetParent(parent, false);
        AnchorCenter(go.GetComponent<RectTransform>(), new Vector2(280f, 26f), pos);

        Slider slider = go.GetComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.interactable = true;
        slider.SetValueWithoutNotify(0.8f);

        Transform background = go.transform.Find("Background");
        if (background != null) background.GetComponent<Image>().color = TrackColor;

        Transform fillArea = go.transform.Find("Fill Area");
        if (fillArea != null)
        {
            Transform fill = fillArea.Find("Fill");
            if (fill != null) fill.GetComponent<Image>().color = FillColor;
        }

        Transform handleArea = go.transform.Find("Handle Slide Area");
        if (handleArea != null)
        {
            Transform handle = handleArea.Find("Handle");
            if (handle != null)
            {
                handle.GetComponent<Image>().color = Color.white;
                handle.GetComponent<RectTransform>().sizeDelta = new Vector2(26f, 0f);
            }
        }

        return slider;
    }
    #endregion

    #region 工具
    private static void DestroyChildByName(Transform parent, string childName)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName) Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    private static void SetLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursive(root.GetChild(i), layer);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.localScale = Vector3.one;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
    }

    private static void AnchorCenter(RectTransform rt, Vector2 size, Vector2 pos)
    {
        rt.localScale = Vector3.one;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }
    #endregion
}
#endif
