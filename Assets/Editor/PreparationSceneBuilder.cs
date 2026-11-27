#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using FruitCatch.Preparation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PreparationSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Preparation.unity";
    private const string FontPath = "Assets/Font/MochiyPopPOne-Regular SDF-Dynamic.asset";
    private const string CarrierSpritePath = "Assets/Image/honbansozai/robo-wait.png";
    private const string CarrierHoldSpritePath = "Assets/Image/honbansozai/robo-hold.png";
    private const string CarrierWalk1SpritePath = "Assets/Image/honbansozai/robo-walk1.png";
    private const string CarrierWalk2SpritePath = "Assets/Image/honbansozai/robo-walk2.png";
    private const string EaterSpritePath = "Assets/Image/honbansozai/mochi-shomen.png";
    private const string FruitSpritePath = "Assets/Image/honbansozai/fruit1.png";
    private const string BugSpritePath = "Assets/Image/honbansozai/spider.png";

    [InitializeOnLoadMethod]
    private static void BuildMissingSceneAfterScriptReload()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isCompiling && !File.Exists(ScenePath))
                Build();
        };
    }

    [MenuItem("FruitCatch/Preparationシーンを生成")]
    public static void Build()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Sprite carrier = AssetDatabase.LoadAssetAtPath<Sprite>(CarrierSpritePath);
        Sprite carrierHold = AssetDatabase.LoadAssetAtPath<Sprite>(CarrierHoldSpritePath);
        Sprite carrierWalk1 = AssetDatabase.LoadAssetAtPath<Sprite>(CarrierWalk1SpritePath);
        Sprite carrierWalk2 = AssetDatabase.LoadAssetAtPath<Sprite>(CarrierWalk2SpritePath);
        Sprite eater = AssetDatabase.LoadAssetAtPath<Sprite>(EaterSpritePath);
        Sprite fruit = AssetDatabase.LoadAssetAtPath<Sprite>(FruitSpritePath);
        Sprite bug = AssetDatabase.LoadAssetAtPath<Sprite>(BugSpritePath);

        Scene previousScene = SceneManager.GetActiveScene();
        bool canBuildAdditively = previousScene.IsValid() &&
                                  previousScene.isLoaded &&
                                  !string.IsNullOrEmpty(previousScene.path);
        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            canBuildAdditively ? NewSceneMode.Additive : NewSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        CreateCamera();
        CreateEventSystem();

        Canvas canvas = CreateCanvas();
        RectTransform canvasTransform = canvas.GetComponent<RectTransform>();

        PreparationCameraSurface cameraSurface = CreateCameraSurface(canvasTransform, font);
        var panels = new List<GameObject>();

        panels.Add(CreateStartPanel(canvasTransform, font, carrier, carrierHold, carrierWalk1, carrierWalk2, eater));
        panels.Add(CreateTwoPlayerPanel(canvasTransform, font));
        panels.Add(CreateFaceAndCarrierPanel(canvasTransform, font, carrier));
        panels.Add(CreateVoiceAndEaterPanel(canvasTransform, font, eater));
        panels.Add(CreateItemPanel(canvasTransform, font, fruit, bug));

        TMP_Text countdownText;
        panels.Add(CreateReadyPanel(canvasTransform, font, out countdownText));

        TMP_Text debugText;
        GameObject debugRoot = CreateDebugOverlay(canvasTransform, font, out debugText);

        GameObject flowObject = new GameObject("PreparationFlowController");
        PreparationFlowController flow = flowObject.AddComponent<PreparationFlowController>();
        ConfigureFlow(flow, panels, countdownText, debugRoot, debugText, cameraSurface);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddScenesToBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (canBuildAdditively && previousScene.IsValid() && previousScene.isLoaded)
            SceneManager.SetActiveScene(previousScene);
        if (canBuildAdditively)
            EditorSceneManager.CloseScene(scene, true);

        Debug.Log("Preparationシーンを生成しました: " + ScenePath);
    }

    private static void CreateCamera()
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.07f, 0.1f);
        camera.orthographic = true;
        cameraObject.AddComponent<AudioListener>();
    }

    private static void CreateEventSystem()
    {
        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }

    private static Canvas CreateCanvas()
    {
        GameObject canvasObject = new GameObject("PreparationCanvas", typeof(RectTransform));
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static PreparationCameraSurface CreateCameraSurface(Transform parent, TMP_FontAsset font)
    {
        GameObject root = CreateRect("CameraLayer", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        RawImage rawImage = root.AddComponent<RawImage>();
        rawImage.color = Color.white;

        GameObject fallback = CreateRect("CameraFallback", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Image background = fallback.AddComponent<Image>();
        background.color = new Color(0.11f, 0.16f, 0.2f);

        TMP_Text message = CreateText(
            fallback.transform,
            "FallbackMessage",
            "カメラ映像なし（開発モード）",
            font,
            34f,
            TextAlignmentOptions.Center,
            new Vector2(0.25f, 0.42f),
            new Vector2(0.75f, 0.58f));
        message.color = new Color(0.78f, 0.84f, 0.88f);

        PreparationCameraSurface surface = root.AddComponent<PreparationCameraSurface>();
        SerializedObject serialized = new SerializedObject(surface);
        serialized.FindProperty("cameraImage").objectReferenceValue = rawImage;
        serialized.FindProperty("fallbackRoot").objectReferenceValue = fallback;
        serialized.FindProperty("fallbackText").objectReferenceValue = message;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return surface;
    }

    private static GameObject CreateStartPanel(Transform parent, TMP_FontAsset font, Sprite carrierWait,
        Sprite carrierHold, Sprite carrierWalk1, Sprite carrierWalk2, Sprite eater)
    {
        GameObject panel = CreatePanel(parent, "01_Start", false);
        CreateText(panel.transform, "Prompt", "両手を上げるとはじまるよ", font, 64f,
            TextAlignmentOptions.Center, new Vector2(0.12f, 0.72f), new Vector2(0.88f, 0.9f));

        GameObject animationArea = CreateRect("MenuCharacterAnimationArea", panel.transform,
            new Vector2(0f, 0f), new Vector2(1f, 0.52f), Vector2.zero, Vector2.zero);
        Image eaterImage = CreateFixedSprite(animationArea.transform, "MenuEater", eater, 216f);
        Image carrierImage = CreateFixedSprite(animationArea.transform, "MenuCarrier", carrierWait, 216f);

        PreparationMenuCharacterAnimator animator = animationArea.AddComponent<PreparationMenuCharacterAnimator>();
        SerializedObject serialized = new SerializedObject(animator);
        serialized.FindProperty("carrierTransform").objectReferenceValue = carrierImage.rectTransform;
        serialized.FindProperty("eaterTransform").objectReferenceValue = eaterImage.rectTransform;
        serialized.FindProperty("carrierImage").objectReferenceValue = carrierImage;
        serialized.FindProperty("eaterImage").objectReferenceValue = eaterImage;
        serialized.FindProperty("holdFrontSprite").objectReferenceValue = carrierHold;
        serialized.FindProperty("waitFrontSprite").objectReferenceValue = carrierWait;
        SerializedProperty walkSprites = serialized.FindProperty("walkSprites");
        walkSprites.arraySize = 2;
        walkSprites.GetArrayElementAtIndex(0).objectReferenceValue = carrierWalk1;
        walkSprites.GetArrayElementAtIndex(1).objectReferenceValue = carrierWalk2;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return panel;
    }

    private static GameObject CreateTwoPlayerPanel(Transform parent, TMP_FontAsset font)
    {
        GameObject panel = CreatePanel(parent, "02_TwoPlayers", true);
        CreateText(panel.transform, "Explanation",
            "このゲームはふたりであそぶよ\n\n①　かおをうごかすひと\n②　こえをだすひと\n\nまずは、かおをうごかすひとをきめてね",
            font, 48f, TextAlignmentOptions.Center,
            new Vector2(0.13f, 0.16f), new Vector2(0.87f, 0.84f));
        return panel;
    }

    private static GameObject CreateFaceAndCarrierPanel(Transform parent, TMP_FontAsset font, Sprite carrier)
    {
        GameObject panel = CreatePanel(parent, "03_FaceAndCarrier", true);

        GameObject faceArea = CreateRect("FaceAlignmentArea_Replaceable", panel.transform,
            new Vector2(0.06f, 0.12f), new Vector2(0.48f, 0.76f), Vector2.zero, Vector2.zero);
        Image areaImage = faceArea.AddComponent<Image>();
        areaImage.color = new Color(0.25f, 0.65f, 0.82f, 0.18f);
        CreateText(faceArea.transform, "AreaLabel", "顔合わせ領域（仮）", font, 32f,
            TextAlignmentOptions.Center, Vector2.zero, Vector2.one);

        CreateText(panel.transform, "FacePrompt", "わくのなかに\nひとりだけ うつってね", font, 46f,
            TextAlignmentOptions.Center, new Vector2(0.06f, 0.7f), new Vector2(0.48f, 0.86f));
        CreateText(panel.transform, "CarrierExplanation",
            "顔を横にうごかして\nパクッチョを はこんであげよう",
            font, 44f, TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.12f), new Vector2(0.93f, 0.49f));
        return panel;
    }

    private static GameObject CreateVoiceAndEaterPanel(Transform parent, TMP_FontAsset font, Sprite eater)
    {
        GameObject panel = CreatePanel(parent, "04_VoiceAndEater", true);
        CreateSprite(panel.transform, "EaterImage", eater, new Vector2(0.08f, 0.25f), new Vector2(0.43f, 0.78f));
        CreateText(panel.transform, "EaterExplanation",
            "こえをだすと口をひらいて\nくだものをたべられるよ\n\nこえの高さをかえると\nなにかがかわるかも！",
            font, 44f, TextAlignmentOptions.Center,
            new Vector2(0.43f, 0.13f), new Vector2(0.94f, 0.72f));
        return panel;
    }

    private static GameObject CreateItemPanel(Transform parent, TMP_FontAsset font, Sprite fruit, Sprite bug)
    {
        GameObject panel = CreatePanel(parent, "05_Items", true);
        CreateText(panel.transform, "Title", "アイテムのせつめい", font, 54f,
            TextAlignmentOptions.Center, new Vector2(0.2f, 0.82f), new Vector2(0.8f, 0.95f));

        CreateItemColumn(panel.transform, "Fruit", fruit, "くだもの\n\nたべると\nとくてんアップ！", font, 0.05f, 0.32f);
        CreateItemColumn(panel.transform, "Bug", bug, "むし\n\nたべると\nとくてんダウン…", font, 0.355f, 0.645f);
        CreateItemColumn(panel.transform, "FeverFruit", fruit, "フィーバーくだもの\n\nとるとフィーバータイムが\nはじまるよ", font, 0.68f, 0.97f);
        return panel;
    }

    private static void CreateItemColumn(Transform parent, string name, Sprite sprite, string description,
        TMP_FontAsset font, float minX, float maxX)
    {
        GameObject root = CreateRect(name, parent, new Vector2(minX, 0.12f), new Vector2(maxX, 0.8f), Vector2.zero, Vector2.zero);
        CreateSprite(root.transform, "Image", sprite, new Vector2(0.15f, 0.48f), new Vector2(0.85f, 0.95f));
        CreateText(root.transform, "Description", description, font, 31f,
            TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(1f, 0.46f));
    }

    private static GameObject CreateReadyPanel(Transform parent, TMP_FontAsset font, out TMP_Text countdownText)
    {
        GameObject panel = CreatePanel(parent, "06_Ready", true);
        CreateText(panel.transform, "ReadyMessage", "じゅんびはいいかな？\nはじめるよ", font, 64f,
            TextAlignmentOptions.Center, new Vector2(0.15f, 0.52f), new Vector2(0.85f, 0.83f));
        countdownText = CreateText(panel.transform, "Countdown", string.Empty, font, 150f,
            TextAlignmentOptions.Center, new Vector2(0.3f, 0.12f), new Vector2(0.7f, 0.52f));
        return panel;
    }

    private static GameObject CreatePanel(Transform parent, string name, bool useReadingPanel)
    {
        GameObject panel = CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        if (useReadingPanel)
        {
            GameObject background = CreateRect("AdjustableReadingPanel", panel.transform,
                new Vector2(0.035f, 0.055f), new Vector2(0.965f, 0.945f), Vector2.zero, Vector2.zero);
            Image image = background.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.82f);
            background.transform.SetAsFirstSibling();
        }
        return panel;
    }

    private static void CreateSprite(Transform parent, string name, Sprite sprite, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject imageObject = CreateRect(name, parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
        Image image = imageObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.color = sprite != null ? Color.white : new Color(0.7f, 0.7f, 0.7f, 0.35f);
    }

    private static Image CreateFixedSprite(Transform parent, string name, Sprite sprite, float size)
    {
        GameObject imageObject = CreateRect(name, parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            Vector2.zero, Vector2.zero);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.one * size;
        Image image = imageObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private static GameObject CreateDebugOverlay(Transform parent, TMP_FontAsset font, out TMP_Text debugText)
    {
        GameObject root = CreateRect("DevelopmentOverlay", parent,
            new Vector2(0.01f, 0.01f), new Vector2(0.56f, 0.13f), Vector2.zero, Vector2.zero);
        Image image = root.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.78f);
        debugText = CreateText(root.transform, "DebugText", string.Empty, font, 22f,
            TextAlignmentOptions.MidlineLeft, new Vector2(0.025f, 0.08f), new Vector2(0.975f, 0.92f));
        debugText.color = Color.white;
        return root;
    }

    private static TMP_Text CreateText(Transform parent, string name, string content, TMP_FontAsset font,
        float fontSize, TextAlignmentOptions alignment, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject textObject = CreateRect(name, parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Max(16f, fontSize * 0.55f);
        text.fontSizeMax = fontSize;
        text.alignment = alignment;
        text.color = new Color(0.08f, 0.1f, 0.12f);
        text.raycastTarget = false;
        if (font != null)
            text.font = font;
        return text;
    }

    private static GameObject CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        rect.localScale = Vector3.one;
        return gameObject;
    }

    private static void ConfigureFlow(PreparationFlowController flow, IReadOnlyList<GameObject> panels,
        TMP_Text countdownText, GameObject debugRoot, TMP_Text debugText,
        PreparationCameraSurface cameraSurface)
    {
        SerializedObject serialized = new SerializedObject(flow);
        SerializedProperty steps = serialized.FindProperty("steps");
        steps.arraySize = panels.Count;

        string[] names =
        {
            "開始（両手）", "2人プレイ説明", "顔・トコミン", "声・パクッチョ", "アイテム説明", "準備・カウントダウン"
        };
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

        for (int i = 0; i < panels.Count; i++)
        {
            SerializedProperty step = steps.GetArrayElementAtIndex(i);
            step.FindPropertyRelative("displayName").stringValue = names[i];
            step.FindPropertyRelative("panel").objectReferenceValue = panels[i];
            step.FindPropertyRelative("requiredSignal").enumValueIndex = (int)signals[i];
            step.FindPropertyRelative("requiredHoldSeconds").floatValue = holdSeconds[i];
            step.FindPropertyRelative("showCountdown").boolValue = i == 1 || i == 4;
        }

        serialized.FindProperty("countdownText").objectReferenceValue = countdownText;
        serialized.FindProperty("debugOverlayRoot").objectReferenceValue = debugRoot;
        serialized.FindProperty("debugText").objectReferenceValue = debugText;
        serialized.FindProperty("cameraSurface").objectReferenceValue = cameraSurface;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AddScenesToBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };

        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.path != ScenePath)
                scenes.Add(scene);
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
#endif
