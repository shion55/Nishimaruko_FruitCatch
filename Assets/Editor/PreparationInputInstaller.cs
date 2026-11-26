#if UNITY_EDITOR
using System.IO;
using FruitCatch.Preparation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PreparationInputInstaller
{
    private const string ScenePath = "Assets/Scenes/Preparation.unity";

    [InitializeOnLoadMethod]
    private static void ApplyAfterScriptReload()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
                Apply();
        };
    }

    [MenuItem("FruitCatch/Preparation入力連携を適用")]
    public static void Apply()
    {
        if (!File.Exists(ScenePath))
            return;

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        PreparationFlowController flow = FindInScene<PreparationFlowController>(scene);
        PreparationCameraSurface camera = FindInScene<PreparationCameraSurface>(scene);
        if (flow == null || camera == null)
        {
            CloseIfNeeded(scene, opened);
            return;
        }

        // 旧版インストーラーがAdditiveで開いた別シーンへ生成した同名オブジェクトだけを除去する。
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene loaded = SceneManager.GetSceneAt(i);
            if (loaded == scene)
                continue;
            foreach (GameObject root in loaded.GetRootGameObjects())
            {
                if (root.name == "FruitCatchTrackerProcess" &&
                    root.GetComponent<FruitCatchTrackerProcess>() != null)
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        GameObject flowObject = flow.gameObject;
        MicrophoneAnalysis microphone = flowObject.GetComponent<MicrophoneAnalysis>();
        if (microphone == null)
            microphone = flowObject.AddComponent<MicrophoneAnalysis>();

        PreparationVoiceDetector voice = flowObject.GetComponent<PreparationVoiceDetector>();
        if (voice == null)
            voice = flowObject.AddComponent<PreparationVoiceDetector>();
        SetReference(voice, "flow", flow);
        SetReference(voice, "microphone", microphone);

        PreparationPythonBridge bridge = flowObject.GetComponent<PreparationPythonBridge>();
        if (bridge == null)
            bridge = flowObject.AddComponent<PreparationPythonBridge>();
        SetReference(bridge, "flow", flow);
        SetReference(bridge, "cameraSurface", camera);

        if (flowObject.GetComponent<FruitCatchTrackerProcess>() == null)
            flowObject.AddComponent<FruitCatchTrackerProcess>();

        SerializedObject flowSerialized = new SerializedObject(flow);
        SerializedProperty steps = flowSerialized.FindProperty("steps");
        PreparationSignal[] signals =
        {
            PreparationSignal.HandsRaised,
            PreparationSignal.Timed,
            PreparationSignal.FaceAligned,
            PreparationSignal.VoiceDetected,
            PreparationSignal.Timed,
            PreparationSignal.Timed
        };
        float[] holdSeconds = { 1f, 7f, 0.1f, 1f, 7f, 2f };
        for (int i = 0; steps.arraySize == signals.Length && i < signals.Length; i++)
        {
            SerializedProperty step = steps.GetArrayElementAtIndex(i);
            step.FindPropertyRelative("requiredSignal").enumValueIndex = (int)signals[i];
            step.FindPropertyRelative("requiredHoldSeconds").floatValue = holdSeconds[i];
        }
        flowSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(flow);
        EditorUtility.SetDirty(flowObject);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        CloseIfNeeded(scene, opened);
        Debug.Log("PreparationのPython映像・両手・顔・音声・自動進行を接続しました。");
    }

    private static void SetReference(Object target, string propertyName, Object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        serialized.FindProperty(propertyName).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);
            if (found != null)
                return found;
        }
        return null;
    }

    private static void CloseIfNeeded(Scene scene, bool opened)
    {
        if (opened && scene.IsValid() && scene.isLoaded)
            EditorSceneManager.CloseScene(scene, true);
    }
}
#endif
