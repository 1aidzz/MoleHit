using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 光标悬停（未点击）到按钮上时播放 UI 音效。
///
/// 由 StartMenuUI / GameManager 在运行时挂到按钮上，场景节点不用改，
/// 也不需要重新执行「Tools → 打地鼠 → 生成开始菜单 UI」。
/// </summary>
[RequireComponent(typeof(Selectable))]
public class ButtonHoverSound : MonoBehaviour, IPointerEnterHandler
{
    /// <summary>
    /// 给按钮挂上悬停音效。同一个按钮重复调用只会挂一次，可以安全地在多处调用。
    /// </summary>
    public static void Attach(Button button)
    {
        if (button == null) return;
        if (button.GetComponent<ButtonHoverSound>() == null)
            button.gameObject.AddComponent<ButtonHoverSound>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (AudioManager.instance != null) AudioManager.instance.PlayButtonSound();
    }
}
