#if UNITY_EDITOR
using System.IO;
using FruitCatch.Preparation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PreparationMenuAnimationInstaller
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

    [MenuItem("FruitCatch/承認済みメニューアニメーションを適用")]
    public static void Apply()
    {
        if (!File.Exists(ScenePath))
            return;

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForInstall = !scene.IsValid() || !scene.isLoaded;
        if (openedForInstall)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        GameObject startPanel = FindInScene(scene, "01_Start");
        if (startPanel == null)
        {
            CloseIfNeeded(scene, openedForInstall);
            return;
        }

        if (startPanel.GetComponentInChildren<PreparationMenuCharacterAnimator>(true) != null)
        {
            CloseIfNeeded(scene, openedForInstall);
            return;
        }

        RemoveChild(startPanel.transform, "CarrierImage");
        RemoveChild(startPanel.transform, "EaterImage");

        GameObject animationArea = CreateRect(
            "MenuCharacterAnimationArea",
            startPanel.transform,
            new Vector2(0f, 0f),
            new Vector2(1f, 0.52f));

        Sprite eaterSprite = LoadSprite("Assets/Image/honbansozai/mochi-shomen.png");
        Sprite waitSprite = LoadSprite("Assets/Image/honbansozai/robo-wait.png");
        Sprite holdSprite = LoadSprite("Assets/Image/honbansozai/robo-hold.png");
        Sprite walk1Sprite = LoadSprite("Assets/Image/honbansozai/robo-walk1.png");
        Sprite walk2Sprite = LoadSprite("Assets/Image/honbansozai/robo-walk2.png");

        Image eaterImage = CreateCharacterImage(animationArea.transform, "MenuEater", eaterSprite);
        Image carrierImage = CreateCharacterImage(animationArea.transform, "MenuCarrier", waitSprite);

        PreparationMenuCharacterAnimator animator = animationArea.AddComponent<PreparationMenuCharacterAnimator>();
        SerializedObject serialized = new SerializedObject(animator);
        serialized.FindProperty("carrierTransform").objectReferenceValue = carrierImage.rectTransform;
        serialized.FindProperty("eaterTransform").objectReferenceValue = eaterImage.rectTransform;
        serialized.FindProperty("carrierImage").objectReferenceValue = carrierImage;
        serialized.FindProperty("eaterImage").objectReferenceValue = eaterImage;
        serialized.FindProperty("holdFrontSprite").objectReferenceValue = holdSprite;
        serialized.FindProperty("waitFrontSprite").objectReferenceValue = waitSprite;
        SerializedProperty walkSprites = serialized.FindProperty("walkSprites");
        walkSprites.arraySize = 2;
        walkSprites.GetArrayElementAtIndex(0).objectReferenceValue = walk1Sprite;
        walkSprites.GetArrayElementAtIndex(1).objectReferenceValue = walk2Sprite;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        GameObject facePanel = FindInScene(scene, "03_FaceAndCarrier");
        if (facePanel != null)
            RemoveChild(facePanel.transform, "CarrierImage");

        GameObject voicePanel = FindInScene(scene, "04_VoiceAndEater");
        if (voicePanel != null)
        {
            RemoveChild(voicePanel.transform, "MicrophoneDirection");
            Transform explanation = FindChildRecursive(voicePanel.transform, "EaterExplanation");
            if (explanation != null && explanation.TryGetComponent(out TMP_Text text))
            {
                text.text =
                    "マイクにむかって、こえをだしてね\n\n" +
                    "Eater\nこえをだすと口をひらいて\nくだものをたべられるよ\n\n" +
                    "こえの高さをかえると\nなにかがかわるかも！";
                EditorUtility.SetDirty(text);
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        CloseIfNeeded(scene, openedForInstall);
        Debug.Log("承認済みのPreparationメニューアニメーションを適用しました。");
    }

    private static Sprite LoadSprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

    private static Image CreateCharacterImage(Transform parent, string name, Sprite sprite)
    {
        GameObject gameObject = CreateRect(name, parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.one * 216f;
        Image image = gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private static GameObject CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        return gameObject;
    }

    private static GameObject FindInScene(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform match = FindChildRecursive(root.transform, name);
            if (match != null)
                return match.gameObject;
        }
        return null;
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform match = FindChildRecursive(parent.GetChild(i), name);
            if (match != null)
                return match;
        }
        return null;
    }

    private static void RemoveChild(Transform parent, string name)
    {
        Transform child = FindChildRecursive(parent, name);
        if (child != null && child != parent)
            Object.DestroyImmediate(child.gameObject);
    }

    private static void CloseIfNeeded(Scene scene, bool openedForInstall)
    {
        if (openedForInstall && scene.IsValid() && scene.isLoaded)
            EditorSceneManager.CloseScene(scene, true);
    }
}
#endif
