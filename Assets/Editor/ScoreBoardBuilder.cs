#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 给左上角的「分数 / 剩余时间」文字加一个纸片风格的底板，
/// 让它在草地背景上更清楚、不显得突兀。
///
/// 菜单：Tools → 打地鼠 → 生成计分板
/// 重复执行会先删掉旧的再重建，可以当"重新生成"用。
///
/// 底板大小按两行文字的实际宽度自动算，留出边距，并保证不出画布。
/// 生成的节点放在 Canvas 最前面（画在文字之下），文字本身不移动，
/// 所以 GameManager 里的引用不受影响。
/// </summary>
public static class ScoreBoardBuilder
{
    private const string CanvasName = "Canvas";
    private const string BoardName = "ScoreBoard";
    private const string PanelSpritePath = "Assets/Sprites/UI/panel_round.png";
    private const string ScenePath = "Assets/Scenes/WhackAMole.unity";

    /// <summary>文字到纸片边缘的留白（UI 单位）</summary>
    private const float Padding = 24f;
    /// <summary>额外预留的宽度</summary>
    private const float ExtraWidth = 0f;
    /// <summary>底板与画布边缘的最小距离</summary>
    private const float ScreenMargin = 8f;

    private static readonly Color FillColor = new Color(1f, 0.975f, 0.91f, 0.95f);
    private static readonly Color BorderColor = new Color(0.36f, 0.24f, 0.14f, 1f);
    private static readonly Color TextColor = new Color(0.24f, 0.15f, 0.09f, 1f);

    [MenuItem("Tools/打地鼠/生成计分板")]
    public static void BuildFromMenu()
    {
        if (Build())
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[ScoreBoardBuilder] 计分板已生成，按 Ctrl+S 保存场景。");
        }
    }

    /// <summary>批量/命令行入口：打开场景 → 生成 → 保存</summary>
    public static void BuildAndSave()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError("[ScoreBoardBuilder] 打不开场景 " + ScenePath);
            return;
        }

        if (Build())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ScoreBoardBuilder] 计分板已生成并保存场景。");
        }
    }

    private static bool Build()
    {
        Canvas canvas = FindCanvas();
        if (canvas == null)
        {
            Debug.LogError("[ScoreBoardBuilder] 场景里找不到名为 Canvas 的画布。");
            return false;
        }

        Transform scoreText = FindDeep(canvas.transform, "ScoreText");
        Transform timeText = FindDeep(canvas.transform, "TimeText");
        if (scoreText == null || timeText == null)
        {
            Debug.LogError("[ScoreBoardBuilder] 找不到 ScoreText 或 TimeText。");
            return false;
        }

        Sprite panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PanelSpritePath);
        if (panelSprite == null)
        {
            Debug.LogError("[ScoreBoardBuilder] 缺少底板图 " + PanelSpritePath +
                           "，请先执行 Tools → 打地鼠 → 修复贴图导入设置。");
            return false;
        }

        Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject, "生成计分板");

        // 幂等：先把文字挪回 Canvas 根（保持世界位置），再删掉旧底板
        Transform old = FindDeep(canvas.transform, BoardName);
        if (old != null && old != canvas.transform)
        {
            scoreText.SetParent(canvas.transform, true);
            timeText.SetParent(canvas.transform, true);
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        Text score = scoreText.GetComponent<Text>();
        Text time = timeText.GetComponent<Text>();

        RectTransform scoreRt = (RectTransform)scoreText;
        RectTransform timeRt = (RectTransform)timeText;

        // 两行文字合起来占的范围（锚点在画布中心）
        float left = Mathf.Min(scoreRt.anchoredPosition.x - scoreRt.sizeDelta.x * 0.5f,
                              timeRt.anchoredPosition.x - timeRt.sizeDelta.x * 0.5f);
        float top = Mathf.Max(scoreRt.anchoredPosition.y + scoreRt.sizeDelta.y * 0.5f,
                              timeRt.anchoredPosition.y + timeRt.sizeDelta.y * 0.5f);
        float bottom = Mathf.Min(scoreRt.anchoredPosition.y - scoreRt.sizeDelta.y * 0.5f,
                                 timeRt.anchoredPosition.y - timeRt.sizeDelta.y * 0.5f);

        // 用文字实际排版宽度决定底板宽度，避免留一大片空白
        float contentWidth = Mathf.Max(MeasureWidth(score), MeasureWidth(time));
        if (contentWidth <= 0f) contentWidth = Mathf.Max(scoreRt.sizeDelta.x, timeRt.sizeDelta.x);

        float width = contentWidth + Padding * 2f + ExtraWidth;
        float height = (top - bottom) + Padding * 2f;

        // 左边贴齐文字左边，再按画布边界往里收
        Rect canvasRect = GetCanvasRect(canvas);
        float leftEdge = left - Padding;
        float minLeft = canvasRect.xMin + ScreenMargin;
        if (leftEdge < minLeft) leftEdge = minLeft;
        float maxLeft = canvasRect.xMax - ScreenMargin - width;
        if (leftEdge > maxLeft) leftEdge = maxLeft;

        Vector2 pos = new Vector2(leftEdge + width * 0.5f, (top + bottom) * 0.5f);
        float minY = canvasRect.yMin + ScreenMargin + height * 0.5f;
        float maxY = canvasRect.yMax - ScreenMargin - height * 0.5f;
        pos.y = Mathf.Clamp(pos.y, Mathf.Min(minY, maxY), Mathf.Max(minY, maxY));

        GameObject board = new GameObject(BoardName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(board, "Create " + BoardName);
        board.layer = canvas.gameObject.layer;

        RectTransform rt = board.GetComponent<RectTransform>();
        rt.SetParent(canvas.transform, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, height);
        rt.localScale = Vector3.one;

        Image image = board.AddComponent<Image>();
        image.sprite = panelSprite;
        image.type = Image.Type.Sliced;
        image.color = FillColor;
        image.raycastTarget = false;

        // 描边：Outline 会沿四个方向各画一份，形成等宽边框
        Outline outline = board.AddComponent<Outline>();
        outline.effectColor = BorderColor;
        outline.effectDistance = new Vector2(3f, -3f);
        outline.useGraphicAlpha = true;

        // 文字挂到底板下：底板整体缩放做"加分跳动"时，文字跟着一起动
        scoreText.SetParent(board.transform, true);
        timeText.SetParent(board.transform, true);

        // 数字变化时文字可能变长，允许溢出，不要被 rect 裁掉
        StyleText(score);
        StyleText(time);

        // 放在最前面 → 画在其它 UI 之下；开始界面/结算面板仍会盖住它
        board.transform.SetSiblingIndex(0);

        Selection.activeGameObject = board;
        return true;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static Canvas FindCanvas()
    {
        Canvas[] canvases = Object.FindObjectsOfType<Canvas>();
        for (int i = 0; i < canvases.Length; i++)
            if (canvases[i] != null && canvases[i].name == CanvasName)
                return canvases[i];
        return null;
    }

    /// <summary>
    /// 画布的逻辑区域（锚点 0.5 为中心，所以是 -宽/2 .. +宽/2）。
    /// ScaleWithScreenSize 时画布逻辑尺寸由参考分辨率决定，直接用它比 canvas.rect 更可靠
    /// （批处理/游戏视图还没布局时 rect 会不准，边界判断一错底板就跑到文字外面）。
    /// </summary>
    private static Rect GetCanvasRect(Canvas canvas)
    {
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        Vector2 refRes = scaler != null ? scaler.referenceResolution : new Vector2(1920f, 1080f);
        if (refRes.x <= 1f || refRes.y <= 1f) refRes = new Vector2(1920f, 1080f);

        Rect refRect = new Rect(-refRes.x * 0.5f, -refRes.y * 0.5f, refRes.x, refRes.y);
        if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            return refRect;

        Rect rect = ((RectTransform)canvas.transform).rect;
        if (rect.width < refRes.x * 0.5f || rect.height < refRes.y * 0.5f)
            return refRect;
        return rect;
    }

    private static float MeasureWidth(Text text)
    {
        return text != null ? text.preferredWidth : 0f;
    }

    private static void StyleText(Text text)
    {
        if (text == null) return;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = TextColor;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
    }
}
#endif
