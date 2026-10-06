using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 把一张精灵裁剪到世界坐标中的矩形窗口内，只有落在窗口里的部分会显示。
///
/// 原理：「改写网格顶点 + 同步重算 UV」
///  1. 把世界坐标的裁剪窗口换算到本地坐标，求出它与精灵矩形的交集；
///  2. 用交集的 4 个角重新搭一个四边形（几何上就只存在于可见区域）；
///  3. 再按这几个点在原精灵矩形里的比例反算 UV，让贴图内容在世界坐标里保持不动。
///
/// 注意 1：只改 UV 是做不到的 —— UV 只决定采样哪个像素，三角形仍铺在原来的位置，
/// 像素不会因为 UV 变化而消失。必须同时把顶点收进来才成立。
///
/// 注意 2：Unity 不允许同一个物体上同时存在 SpriteRenderer 与 MeshFilter / MeshRenderer
/// （会报 "conflicts with the existing SpriteRenderer derived component"），
/// 因此 mesh 挂在本物体新建的【子物体】上。该子物体的本地变换为单位矩阵，
/// 所以「子物体本地坐标」与「本物体本地坐标」数值完全一致，下面所有换算不受影响。
/// </summary>
public class SpriteRectClipper : MonoBehaviour
{
    private const float MinSize = 0.0001f;
    private const string ChildName = "ClippedRender";

    private Sprite sourceSprite;
    private Rect clipRect;
    private Mesh mesh;
    private Material material;
    private MeshRenderer meshRenderer;
    private GameObject renderObject;

    private readonly Vector3[] vertices = new Vector3[4];
    private readonly Vector2[] uvs = new Vector2[4];
    private readonly int[] triangles = { 0, 1, 2, 0, 2, 3 };
    // Sprites/Default 会把顶点色乘进最终颜色，自建网格默认没有顶点色会导致画面变黑
    private readonly Color32[] colors =
    {
        new Color32(255, 255, 255, 255),
        new Color32(255, 255, 255, 255),
        new Color32(255, 255, 255, 255),
        new Color32(255, 255, 255, 255)
    };

    private bool ready;

    /// <summary>用一张精灵初始化裁剪渲染。返回 false 表示初始化失败（调用方应保留原 SpriteRenderer）。</summary>
    public bool Setup(Sprite sprite, Color tint, int sortingLayerValue, int sortingOrder)
    {
        if (sprite == null || sprite.texture == null) return false;

        sourceSprite = sprite;

        MeshFilter meshFilter = EnsureRenderHost();
        if (meshFilter == null || meshRenderer == null) return false;

        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = "ClippedSprite";
        }
        meshFilter.sharedMesh = mesh;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        if (shader == null)
        {
            Debug.LogWarning("SpriteRectClipper: 找不到可用着色器，改用原来的 SpriteRenderer。");
            return false;
        }

        if (material == null) material = new Material(shader);
        else material.shader = shader;
        material.mainTexture = sprite.texture;
        material.color = tint;
        meshRenderer.sharedMaterial = material;
        meshRenderer.sortingLayerID = sortingLayerValue;
        meshRenderer.sortingOrder = sortingOrder;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        ready = true;
        UpdateMesh();
        return true;
    }

    /// <summary>
    /// 建立承载网格的子物体。本地变换必须严格是单位矩阵，
    /// 否则子物体空间与本物体空间不一致，整个贴合 (pivot/缩放) 都会算错。
    /// </summary>
    private MeshFilter EnsureRenderHost()
    {
        if (renderObject == null)
        {
            Transform existing = transform.Find(ChildName);
            renderObject = existing != null ? existing.gameObject : new GameObject(ChildName);
        }

        Transform rt = renderObject.transform;
        rt.SetParent(transform, false);
        rt.localPosition = Vector3.zero;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;

        MeshFilter filter = renderObject.GetComponent<MeshFilter>();
        if (filter == null) filter = renderObject.AddComponent<MeshFilter>();
        if (meshRenderer == null) meshRenderer = renderObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = renderObject.AddComponent<MeshRenderer>();

        return filter;
    }

    /// <summary>设置显示窗口（世界坐标）</summary>
    public void SetClipRect(Rect worldRect)
    {
        clipRect = worldRect;
    }

    private void LateUpdate()
    {
        UpdateMesh();
    }

    private void UpdateMesh()
    {
        if (!ready || sourceSprite == null || meshRenderer == null) return;

        // 1) 世界窗口 → 本地坐标（自动处理洞口 0.7、地鼠 0.25 的缩放与层级嵌套）
        Vector3 worldMin = new Vector3(clipRect.xMin, clipRect.yMin, 0f);
        Vector3 worldMax = new Vector3(clipRect.xMax, clipRect.yMax, 0f);
        Vector3 localMin = transform.InverseTransformPoint(worldMin);
        Vector3 localMax = transform.InverseTransformPoint(worldMax);

        float clipX0 = Mathf.Min(localMin.x, localMax.x);
        float clipX1 = Mathf.Max(localMin.x, localMax.x);
        float clipY0 = Mathf.Min(localMin.y, localMax.y);
        float clipY1 = Mathf.Max(localMin.y, localMax.y);

        // 2) 精灵在本地空间的矩形（由 rect / pivot / pixelsPerUnit 推出，与 Tight 网格无关）
        Rect spriteRect = GetSpriteLocalRect();

        // 3) 求交集
        float x0 = Mathf.Max(spriteRect.xMin, clipX0);
        float x1 = Mathf.Min(spriteRect.xMax, clipX1);
        float y0 = Mathf.Max(spriteRect.yMin, clipY0);
        float y1 = Mathf.Min(spriteRect.yMax, clipY1);

        if (x1 - x0 < MinSize || y1 - y0 < MinSize)
        {
            // 完全跑到窗口外：不渲染
            meshRenderer.enabled = false;
            return;
        }
        meshRenderer.enabled = true;

        // 4) 用交集的四个角重建四边形
        vertices[0] = new Vector3(x0, y0, 0f);
        vertices[1] = new Vector3(x0, y1, 0f);
        vertices[2] = new Vector3(x1, y1, 0f);
        vertices[3] = new Vector3(x1, y0, 0f);

        // 5) UV 反算：让裁剪后的内容在世界坐标里纹丝不动
        for (int i = 0; i < 4; i++)
        {
            uvs[i] = LocalToUv(vertices[i], spriteRect);
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.colors32 = colors;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
    }

    /// <summary>精灵矩形在本地空间的表示</summary>
    private Rect GetSpriteLocalRect()
    {
        float ppu = sourceSprite.pixelsPerUnit;
        Rect r = sourceSprite.rect;        // 贴图上的像素矩形
        Vector2 pivot = sourceSprite.pivot; // 轴心相对该矩形左下角的像素偏移
        return new Rect(-pivot.x / ppu, -pivot.y / ppu, r.width / ppu, r.height / ppu);
    }

    /// <summary>本地坐标点 → 贴图 UV</summary>
    private Vector2 LocalToUv(Vector3 local, Rect spriteRect)
    {
        float u = Mathf.Clamp01((local.x - spriteRect.xMin) / spriteRect.width);
        float v = Mathf.Clamp01((local.y - spriteRect.yMin) / spriteRect.height);

        Rect texRect = sourceSprite.textureRect;
        return new Vector2(
            (texRect.xMin + u * texRect.width) / sourceSprite.texture.width,
            (texRect.yMin + v * texRect.height) / sourceSprite.texture.height);
    }

    private void OnDestroy()
    {
        if (mesh != null)
        {
            Destroy(mesh);
            mesh = null;
        }
        if (material != null)
        {
            Destroy(material);
            material = null;
        }
    }
}
