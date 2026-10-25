#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>命令行入口：一次跑完「修复贴图 + 生成计分板 + 保存场景」。</summary>
public static class MoleHitPolishSetup
{
    public static void Run()
    {
        SpriteImportFixer.FixAll();
        ScoreBoardBuilder.BuildAndSave();
        AssetDatabase.SaveAssets();
    }
}
#endif
