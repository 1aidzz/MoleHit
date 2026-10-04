using UnityEngine;

public class InputManager : MonoBehaviour
{
    void Update()
    {
        // 鼠标点击
        if (Input.GetMouseButtonDown(0))
        {
            RaycastHit2D hit = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(Input.mousePosition), Vector2.zero);
            if (hit.collider != null)
            {
                Mole mole = hit.collider.GetComponent<Mole>();
                if (mole != null && GameManager.instance.currentState == GameState.Playing)
                {
                    mole.Hit();
                }
            }
        }

        // 手机触控点击
        foreach (Touch touch in Input.touches)
        {
            if (touch.phase == TouchPhase.Began)
            {
                RaycastHit2D hit = Physics2D.Raycast(Camera.main.ScreenToWorldPoint(touch.position), Vector2.zero);
                if (hit.collider != null)
                {
                    Mole mole = hit.collider.GetComponent<Mole>();
                    if (mole != null && GameManager.instance.currentState == GameState.Playing)
                    {
                        mole.Hit();
                    }
                }
            }
        }
    }
}
