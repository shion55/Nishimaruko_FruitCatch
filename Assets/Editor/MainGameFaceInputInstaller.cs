#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MainGameFaceInputInstaller
{
    private const string ScenePath = "Assets/Scenes/MainGame.unity";

    [InitializeOnLoadMethod]
    private static void ApplyAfterScriptReload()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
                Apply();
        };
    }

    [MenuItem("FruitCatch/MainGame顔入力を適用")]
    public static void Apply()
    {
        if (!File.Exists(ScenePath))
            return;

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        FaceMovementController controller = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            controller = root.GetComponentInChildren<FaceMovementController>(true);
            if (controller != null)
                break;
        }

        if (controller != null)
        {
            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("port").intValue = 5015;
            serialized.FindProperty("movefacetoggle").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        if (opened && scene.IsValid() && scene.isLoaded)
            EditorSceneManager.CloseScene(scene, true);
    }
}
#endif
