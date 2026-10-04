using UnityEngine;

public class Mole : MonoBehaviour
{
    public bool isHit = false; // 防止同一只地鼠重复计分

    // 显示地鼠
    public void Show()
    {
        gameObject.SetActive(true);
        isHit = false;
    }

    // 隐藏地鼠
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    // 被击中调用
    public void Hit()
    {
        if (isHit) return;
        isHit = true;
        GameManager.instance.AddScore(10);
        Hide();
    }
}
