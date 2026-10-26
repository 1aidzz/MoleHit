using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 把 DynamicDifficulty 组件固化到场景里的生成器物体上，方便在 Inspector 里调参。
///
/// 不直接改 .unity 文件的 YAML：手写 YAML 容易漏字段、GUID 对不上。
/// 即使不点这个菜单，运行时 MoleSpawner 也会自动补上该组件（默认参数即可玩），
/// 只是运行结束就没了，想长期保留 / 调整参数就点一下这里。
/// </summary>
public static class DifficultySetup
{
    [MenuItem("Tools/打地鼠/添加动态难度组件")]
    public static void AddDifficultyComponent()
    {
        MoleSpawner spawner = Object.FindObjectOfType<MoleSpawner>();
        if (spawner == null)
        {
            EditorUtility.DisplayDialog("添加动态难度", "当前场景里没找到 MoleSpawner。\n请先打开打地鼠场景再执行。", "好");
            return;
        }

        DynamicDifficulty existing = spawner.GetComponent<DynamicDifficulty>();
        if (existing != null)
        {
            Selection.activeGameObject = spawner.gameObject;
            EditorGUIUtility.PingObject(spawner.gameObject);
            Debug.Log("生成器上已经有 DynamicDifficulty 了，直接选中供你调参。", existing);
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(spawner.gameObject, "Add DynamicDifficulty");
        DynamicDifficulty difficulty = Undo.AddComponent<DynamicDifficulty>(spawner.gameObject);
        difficulty.spawner = spawner;

        // 顺带把生成器的开关打开，避免"组件加了但没生效"
        spawner.useDynamicDifficulty = true;

        EditorUtility.SetDirty(spawner);
        EditorUtility.SetDirty(difficulty);
        EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);

        Selection.activeGameObject = spawner.gameObject;
        EditorGUIUtility.PingObject(spawner.gameObject);

        Debug.Log("已添加 DynamicDifficulty（五档默认配置）。记得 Ctrl+S 保存场景。", difficulty);
    }
}
