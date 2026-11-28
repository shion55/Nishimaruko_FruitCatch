#if UNITY_EDITOR
using System.IO;
using FruitCatch.Preparation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class FruitCatchSessionInstaller
{
    private const string GameScenePath = "Assets/Scenes/MainGame.unity";
    private const string PreparationScenePath = "Assets/Scenes/Preparation.unity";
    private const string DynamicFontPath = "Assets/Font/MochiyPopPOne-Regular SDF-Dynamic.asset";

    [InitializeOnLoadMethod]
    private static void InstallAfterScriptReload()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
                Install();
        };
    }

    [MenuItem("FruitCatch/ゲーム時間・結果・ふりがなを適用")]
    public static void Install()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DynamicFontPath);
        if (font == null)
        {
            Debug.LogError("FruitCatchSessionInstaller: Dynamicフォントが見つかりません。");
            return;
        }

        ApplyGameSession(font);
        ApplyPreparationFurigana(font);
        AssetDatabase.SaveAssets();
        Debug.Log("ゲーム時間・開始終了演出・結果画面・ふりがなを適用しました。");
    }

    private static void ApplyGameSession(TMP_FontAsset font)
    {
        if (!File.Exists(GameScenePath))
            return;

        Scene scene = OpenSceneAdditivelyIfNeeded(GameScenePath, out bool openedForInstall);
        GameManager gameManager = FindComponentInScene<GameManager>(scene);
        FallingItemSpawner spawner = FindComponentInScene<FallingItemSpawner>(scene);
        EaterCatchController eater = FindComponentInScene<EaterCatchController>(scene);
        Canvas canvas = FindComponentInScene<Canvas>(scene);

        if (gameManager == null || spawner == null || eater == null || canvas == null)
        {
            Debug.LogError("FruitCatchSessionInstaller: FruitCatchシーンの必須オブジェクトが見つかりません。");
            CloseIfNeeded(scene, openedForInstall);
            return;
        }

        GameObject sessionRoot = FindInScene(scene, "GameSessionUI");
        FruitCatchGameSession session;
        if (sessionRoot == null)
        {
            sessionRoot = CreateRect("GameSessionUI", canvas.transform, Vector2.zero, Vector2.one);
            session = sessionRoot.AddComponent<FruitCatchGameSession>();
            sessionRoot.transform.SetAsLastSibling();
        }
        else
        {
            session = sessionRoot.GetComponent<FruitCatchGameSession>();
            if (session == null)
                session = sessionRoot.AddComponent<FruitCatchGameSession>();
        }

        TMP_Text timer = GetOrCreateText(sessionRoot.transform, "RemainingTimeText", font,
            new Vector2(0.7f, 0.87f), new Vector2(0.98f, 0.98f), 48f, TextAlignmentOptions.TopRight);
        timer.text = "のこり 50びょう";
        timer.color = Color.white;

        TMP_Text announcement = GetOrCreateText(sessionRoot.transform, "GameAnnouncementText", font,
            new Vector2(0.12f, 0.3f), new Vector2(0.88f, 0.7f), 150f, TextAlignmentOptions.Center);
        announcement.gameObject.SetActive(false);

        GameObject resultsRoot = FindChildRecursive(sessionRoot.transform, "ResultsRoot")?.gameObject;
        if (resultsRoot == null)
            resultsRoot = CreateResults(font, sessionRoot.transform, out _, out _, out _, out _, out _);

        FuriganaText title = FindChildRecursive(resultsRoot.transform, "ResultTitle")?.GetComponent<FuriganaText>();
        FuriganaText score = FindChildRecursive(resultsRoot.transform, "ResultScore")?.GetComponent<FuriganaText>();
        FuriganaText fruit = FindChildRecursive(resultsRoot.transform, "ResultFruitCount")?.GetComponent<FuriganaText>();
        FuriganaText bug = FindChildRecursive(resultsRoot.transform, "ResultBugCount")?.GetComponent<FuriganaText>();
        FuriganaText message = FindChildRecursive(resultsRoot.transform, "ResultMessage")?.GetComponent<FuriganaText>();

        SerializedObject serialized = new SerializedObject(session);
        serialized.FindProperty("gameManager").objectReferenceValue = gameManager;
        serialized.FindProperty("itemSpawner").objectReferenceValue = spawner;
        serialized.FindProperty("eaterController").objectReferenceValue = eater;
        serialized.FindProperty("timerText").objectReferenceValue = timer;
        serialized.FindProperty("announcementText").objectReferenceValue = announcement;
        serialized.FindProperty("resultsRoot").objectReferenceValue = resultsRoot;
        serialized.FindProperty("resultTitle").objectReferenceValue = title;
        serialized.FindProperty("scoreResult").objectReferenceValue = score;
        serialized.FindProperty("fruitResult").objectReferenceValue = fruit;
        serialized.FindProperty("bugResult").objectReferenceValue = bug;
        serialized.FindProperty("resultMessage").objectReferenceValue = message;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        resultsRoot.SetActive(false);
        EditorUtility.SetDirty(session);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        CloseIfNeeded(scene, openedForInstall);
    }

    private static GameObject CreateResults(TMP_FontAsset font, Transform parent,
        out FuriganaText title, out FuriganaText score, out FuriganaText fruit,
        out FuriganaText bug, out FuriganaText message)
    {
        GameObject root = CreateRect("ResultsRoot", parent, Vector2.zero, Vector2.one);
        Image shade = root.AddComponent<Image>();
        shade.color = new Color(0.03f, 0.08f, 0.12f, 0.78f);
        shade.raycastTarget = true;

        GameObject card = CreateRect("ResultCard", root.transform, new Vector2(0.16f, 0.08f), new Vector2(0.84f, 0.92f));
        card.AddComponent<CanvasRenderer>();
        RoundedDashedPanelGraphic cardGraphic = card.AddComponent<RoundedDashedPanelGraphic>();
        cardGraphic.FillColor = new Color32(225, 247, 255, 255);
        cardGraphic.BorderColor = new Color32(242, 132, 38, 255);
        cardGraphic.CornerRadius = 38f;
        cardGraphic.BorderThickness = 8f;
        cardGraphic.DashLength = 26f;
        cardGraphic.GapLength = 15f;
        card.AddComponent<FruitResultPresentation>();

        title = CreateFuriganaText(card.transform, "ResultTitle", font,
            new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.955f), 62f, TextAlignmentOptions.Center,
            "けっか[発表|はっぴょう]");
        score = CreateFuriganaText(card.transform, "ResultScore", font,
            new Vector2(0.11f, 0.11f), new Vector2(0.89f, 0.263f), 82f, TextAlignmentOptions.Center,
            "とく[点|てん]　0");

        Sprite bugSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Image/honbansozai/spider.png");
        CreateImage(card.transform, "ResultBugImage", bugSprite,
            new Vector2(0.565f, 0.284f), new Vector2(0.665f, 0.346f));

        fruit = CreateFuriganaText(card.transform, "ResultFruitCount", font,
            new Vector2(0.078f, 0.28f), new Vector2(0.52f, 0.35f), 36f, TextAlignmentOptions.Center,
            "くだもの ぜんぶで ×0");
        bug = CreateFuriganaText(card.transform, "ResultBugCount", font,
            new Vector2(0.67f, 0.28f), new Vector2(0.92f, 0.35f), 36f, TextAlignmentOptions.Center,
            "むし　×0");
        message = CreateFuriganaText(card.transform, "ResultMessage", font,
            new Vector2(0.09f, 0.037f), new Vector2(0.91f, 0.103f), 38f, TextAlignmentOptions.Center,
            "いいちょうし！");
        return root;
    }

    private static void ApplyPreparationFurigana(TMP_FontAsset font)
    {
        if (!File.Exists(PreparationScenePath))
            return;

        Scene scene = OpenSceneAdditivelyIfNeeded(PreparationScenePath, out bool openedForInstall);

        ApplyAnnotatedText(scene, "01_Start", "Prompt", "[両手|りょうて]を[上|あ]げるとはじまるよ", font);
        ApplyAnnotatedText(scene, "02_TwoPlayers", "Explanation",
            "このゲームは[二人|ふたり]で[遊|あそ]ぶよ\n\n①　[顔|かお]を[動|うご]かす[人|ひと]\n②　[声|こえ]を[出|だ]す[人|ひと]\n\nまずは、[顔|かお]を[動|うご]かす[人|ひと]をきめてね", font);
        ApplyAnnotatedText(scene, "03_FaceAndCarrier", "FacePrompt", "[顔|かお]をわくに[合|あ]わせてね", font);
        ApplyAnnotatedText(scene, "03_FaceAndCarrier", "AreaLabel", "かおをあわせるところ", font);
        ApplyAnnotatedText(scene, "03_FaceAndCarrier", "CarrierExplanation",
            "[顔|かお]を[横|よこ]に[動|うご]かして\nパクッチョを はこんであげよう", font);
        ApplyAnnotatedText(scene, "04_VoiceAndEater", "VoiceActionPrompt",
            "マイクにむかって、[声|こえ]を[出|だ]してね", font);
        ApplyAnnotatedText(scene, "04_VoiceAndEater", "EaterExplanation",
            "[声|こえ]を[出|だ]すと[口|くち]をひらいて\nくだものを[食|た]べられるよ\n\n[声|こえ]の[高|たか]さを[変|か]えると\nくだもののはやさが[変|か]わるよ", font);
        ApplyAnnotatedText(scene, "05_Items", "Title", "アイテムのせつめい", font);
        ApplyAnnotatedText(scene, "05_Items/Fruit", "Description", "くだもの\n\n[食|た]べると\nとく[点|てん]アップ！", font);
        ApplyAnnotatedText(scene, "05_Items/Bug", "Description", "むし\n\n[食|た]べると\nとく[点|てん]ダウン…", font);
        ApplyAnnotatedText(scene, "05_Items/FeverFruit", "Description",
            "フィーバーくだもの\n\nとるとフィーバータイムが\nはじまるよ", font);
        ApplyAnnotatedText(scene, "06_Ready", "ReadyMessage", "じゅんびはいいかな？\nはじめるよ", font);
        ApplyAnnotatedText(scene, "CameraLayer", "FallbackMessage", "カメラなし（かいはつモード）", font);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        CloseIfNeeded(scene, openedForInstall);
    }

    private static void ApplyAnnotatedText(Scene scene, string parentPath, string textName,
        string annotated, TMP_FontAsset font)
    {
        Transform parent = FindPathInScene(scene, parentPath);
        Transform target = parent != null ? FindChildRecursive(parent, textName) : null;
        if (target == null || !target.TryGetComponent(out TMP_Text text))
            return;

        text.font = font;
        FuriganaText furigana = target.GetComponent<FuriganaText>();
        bool needsInitialText = furigana == null;
        if (furigana == null)
            furigana = target.gameObject.AddComponent<FuriganaText>();

        SerializedObject serialized = new SerializedObject(furigana);
        SerializedProperty annotatedProperty = serialized.FindProperty("annotatedText");
        if (needsInitialText || string.IsNullOrWhiteSpace(annotatedProperty.stringValue))
            annotatedProperty.stringValue = annotated;
        serialized.FindProperty("rubyFontScale").floatValue = 0.5f;
        serialized.FindProperty("rubyColor").colorValue = text.color;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        furigana.RefreshPreview();
        EditorUtility.SetDirty(text);
        EditorUtility.SetDirty(furigana);
    }

    private static FuriganaText CreateFuriganaText(Transform parent, string name, TMP_FontAsset font,
        Vector2 anchorMin, Vector2 anchorMax, float fontSize, TextAlignmentOptions alignment, string annotated)
    {
        TMP_Text text = GetOrCreateText(parent, name, font, anchorMin, anchorMax, fontSize, alignment);
        text.color = new Color(0.08f, 0.1f, 0.12f);
        FuriganaText furigana = text.GetComponent<FuriganaText>();
        if (furigana == null)
            furigana = text.gameObject.AddComponent<FuriganaText>();
        SerializedObject serialized = new SerializedObject(furigana);
        serialized.FindProperty("annotatedText").stringValue = annotated;
        serialized.FindProperty("rubyFontScale").floatValue = 0.5f;
        serialized.FindProperty("rubyColor").colorValue = text.color;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return furigana;
    }

    private static TMP_Text GetOrCreateText(Transform parent, string name, TMP_FontAsset font,
        Vector2 anchorMin, Vector2 anchorMax, float fontSize, TextAlignmentOptions alignment)
    {
        Transform existing = FindChildRecursive(parent, name);
        TextMeshProUGUI text;
        if (existing != null && existing.TryGetComponent(out TextMeshProUGUI found))
            text = found;
        else
            text = CreateRect(name, parent, anchorMin, anchorMax).AddComponent<TextMeshProUGUI>();

        SetRect(text.rectTransform, anchorMin, anchorMax);
        text.font = font;
        text.fontSize = fontSize;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Max(16f, fontSize * 0.55f);
        text.fontSizeMax = fontSize;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    private static void CreateImage(Transform parent, string name, Sprite sprite,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject gameObject = CreateRect(name, parent, anchorMin, anchorMax);
        Image image = gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private static GameObject CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetRect(rect, anchorMin, anchorMax);
        return gameObject;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static Scene OpenSceneAdditivelyIfNeeded(string path, out bool opened)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        opened = !scene.IsValid() || !scene.isLoaded;
        return opened ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : scene;
    }

    private static void CloseIfNeeded(Scene scene, bool opened)
    {
        if (opened && scene.IsValid() && scene.isLoaded)
            EditorSceneManager.CloseScene(scene, true);
    }

    private static T FindComponentInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null)
                return component;
        }
        return null;
    }

    private static GameObject FindInScene(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform result = FindChildRecursive(root.transform, name);
            if (result != null)
                return result.gameObject;
        }
        return null;
    }

    private static Transform FindPathInScene(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        GameObject rootMatch = FindInScene(scene, parts[0]);
        if (rootMatch == null)
            return null;

        Transform current = rootMatch.transform;
        for (int i = 1; i < parts.Length; i++)
        {
            current = FindChildRecursive(current, parts[i]);
            if (current == null)
                return null;
        }
        return current;
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name)
            return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform result = FindChildRecursive(parent.GetChild(i), name);
            if (result != null)
                return result;
        }
        return null;
    }
}
#endif
