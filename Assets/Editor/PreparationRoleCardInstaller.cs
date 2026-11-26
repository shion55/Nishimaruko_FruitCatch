#if UNITY_EDITOR
using System.IO;
using FruitCatch.Preparation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PreparationRoleCardInstaller
{
    private const string ScenePath = "Assets/Scenes/Preparation.unity";
    private static readonly Color Orange = new Color32(242, 132, 38, 255);
    private static readonly Color TokominBlue = new Color32(34, 167, 216, 255);
    private static readonly Color TokominOutline = new Color32(12, 79, 115, 255);
    private static readonly Color PakucchoCoral = new Color32(242, 107, 94, 255);
    private static readonly Color PakucchoOutline = new Color32(122, 48, 44, 255);

    [InitializeOnLoadMethod]
    private static void ApplyAfterScriptReload()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode)
                Apply();
        };
    }

    [MenuItem("FruitCatch/承認済み役割カードを適用")]
    public static void Apply()
    {
        if (!File.Exists(ScenePath))
            return;

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForInstall = !scene.IsValid() || !scene.isLoaded;
        if (openedForInstall)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        GameObject flowObject = FindInScene(scene, "PreparationFlowController");
        if (flowObject != null && flowObject.TryGetComponent(out PreparationFlowController flow))
        {
            SerializedObject serializedFlow = new SerializedObject(flow);
            serializedFlow.FindProperty("showDebugOverlay").boolValue = false;
            serializedFlow.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(flow);
        }

        GameObject debugOverlay = FindInScene(scene, "DevelopmentOverlay");
        if (debugOverlay != null)
            debugOverlay.SetActive(false);

        ConfigurePatternBackground(scene);

        GameObject facePanel = FindInScene(scene, "03_FaceAndCarrier");
        if (facePanel != null)
        {
            RemoveChild(facePanel.transform, "CarrierImage");
            ConfigureRoleText(
                facePanel.transform,
                "CarrierExplanation",
                "[体|からだ]を[左右|さゆう]に[動|うご]かして\nパクッチョを はこんであげよう");
            CreateActionBanner(
                facePanel.transform,
                "FaceActionBanner",
                "FacePrompt",
                "ここに顔を合わせてね",
                new Vector2(0.19f, 0.76f),
                new Vector2(0.81f, 0.91f));
            CreateRoleCard(
                facePanel.transform,
                "CarrierRoleCard",
                "CarrierExplanation",
                "トコミン",
                TokominBlue,
                TokominOutline,
                new Vector2(0.51f, 0.12f),
                new Vector2(0.93f, 0.66f));
        }

        GameObject voicePanel = FindInScene(scene, "04_VoiceAndEater");
        if (voicePanel != null)
        {
            RemoveChild(voicePanel.transform, "MicrophoneDirection");

            ConfigureRoleText(
                voicePanel.transform,
                "EaterExplanation",
                "[声|こえ]を[出|だ]すと[口|くち]をひらいて\nくだものを[食|た]べられるよ\n\n" +
                "[声|こえ]の[高|たか]さをかえると\nなにかがかわるかも！");

            Transform existingAction = FindChildRecursive(voicePanel.transform, "VoiceActionPrompt");
            TMP_Text actionText = existingAction != null
                ? existingAction.GetComponent<TMP_Text>()
                : CreateText(voicePanel.transform, "VoiceActionPrompt", "マイクにむかって、こえをだしてね");
            actionText.text = "マイクにむかって、こえをだしてね";
            CreateActionBanner(
                voicePanel.transform,
                "VoiceActionBanner",
                actionText,
                new Vector2(0.16f, 0.76f),
                new Vector2(0.84f, 0.91f));
            CreateRoleCard(
                voicePanel.transform,
                "EaterRoleCard",
                "EaterExplanation",
                "パクッチョ",
                PakucchoCoral,
                PakucchoOutline,
                new Vector2(0.46f, 0.12f),
                new Vector2(0.93f, 0.7f));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (openedForInstall)
            EditorSceneManager.CloseScene(scene, true);

        Debug.Log("承認済みの役割カード・強調表示・デバッグ初期非表示を適用しました。");
    }

    private static void ConfigurePatternBackground(Scene scene)
    {
        GameObject canvas = FindInScene(scene, "PreparationCanvas");
        GameObject cameraLayer = FindInScene(scene, "CameraLayer");
        if (canvas == null || cameraLayer == null)
            return;

        GameObject root = FindInScene(scene, "PreparationPatternBackground");
        if (root == null)
            root = CreateRect("PreparationPatternBackground", canvas.transform, Vector2.zero, Vector2.one);
        root.transform.SetSiblingIndex(cameraLayer.transform.GetSiblingIndex() + 1);

        PreparationPatternBackground pattern = root.GetComponent<PreparationPatternBackground>();
        if (pattern == null)
            pattern = root.AddComponent<PreparationPatternBackground>();

        SerializedObject serialized = new SerializedObject(pattern);
        serialized.FindProperty("fruitSprite").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Image/honbansozai/fruit1.png");
        serialized.FindProperty("roboHoldSprite").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Image/honbansozai/robo-hold.png");
        serialized.FindProperty("roboWaitSprite").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Image/honbansozai/robo-wait.png");
        serialized.FindProperty("mochiSprite").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Image/honbansozai/mochi-shomen.png");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        pattern.RefreshPreview();
        EditorUtility.SetDirty(pattern);
    }

    private static void CreateActionBanner(Transform panel, string bannerName, string existingTextName,
        string content, Vector2 anchorMin, Vector2 anchorMax)
    {
        Transform existing = FindChildRecursive(panel, existingTextName);
        TMP_Text text = existing != null ? existing.GetComponent<TMP_Text>() : null;
        if (text == null)
            text = CreateText(panel, existingTextName, content);
        text.text = content;
        CreateActionBanner(panel, bannerName, text, anchorMin, anchorMax);
    }

    private static void CreateActionBanner(Transform panel, string bannerName, TMP_Text text,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        Transform current = FindChildRecursive(panel, bannerName);
        GameObject banner = current != null
            ? current.gameObject
            : CreateRect(bannerName, panel, anchorMin, anchorMax);

        RoundedDashedPanelGraphic graphic = banner.GetComponent<RoundedDashedPanelGraphic>();
        EnsureCanvasRenderer(banner);
        if (graphic == null)
            graphic = banner.AddComponent<RoundedDashedPanelGraphic>();
        graphic.FillColor = Orange;
        graphic.BorderThickness = 0f;
        graphic.CornerRadius = 34f;

        if (banner.GetComponent<PreparationInstructionPulse>() == null)
            banner.AddComponent<PreparationInstructionPulse>();

        text.transform.SetParent(banner.transform, false);
        SetStretch(text.rectTransform, new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.92f));
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.fontSizeMax = 56f;
        text.fontSizeMin = 30f;
        EditorUtility.SetDirty(text);
    }

    private static void CreateRoleCard(Transform panel, string cardName, string textName,
        string titleContent, Color titleFill, Color titleOutline,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        Transform textTransform = FindChildRecursive(panel, textName);
        if (textTransform == null)
            return;

        Transform existingCard = FindChildRecursive(panel, cardName);
        GameObject card = existingCard != null
            ? existingCard.gameObject
            : CreateRect(cardName, panel, anchorMin, anchorMax);

        RoundedDashedPanelGraphic graphic = card.GetComponent<RoundedDashedPanelGraphic>();
        EnsureCanvasRenderer(card);
        if (graphic == null)
            graphic = card.AddComponent<RoundedDashedPanelGraphic>();
        graphic.FillColor = Color.white;
        graphic.BorderColor = Color.black;
        graphic.CornerRadius = 34f;
        graphic.BorderThickness = 5f;
        graphic.BottomBorderExtra = 10f;
        graphic.GapLength = 0f;

        textTransform.SetParent(card.transform, false);
        SetStretch(textTransform.GetComponent<RectTransform>(), new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.78f));
        CreateRoleTitle(card.transform, textTransform.GetComponent<TMP_Text>(), titleContent, titleFill, titleOutline);
    }

    private static void CreateRoleTitle(Transform card, TMP_Text bodyText, string content,
        Color fillColor, Color outlineColor)
    {
        Transform currentBadge = FindChildRecursive(card, "RoleTitleBadge");
        GameObject badge = currentBadge != null
            ? currentBadge.gameObject
            : CreateRect("RoleTitleBadge", card, new Vector2(0.12f, 0.82f), new Vector2(0.88f, 1.08f));
        SetStretch(badge.GetComponent<RectTransform>(), new Vector2(0.12f, 0.82f), new Vector2(0.88f, 1.08f));
        EnsureCanvasRenderer(badge);

        RoundedDashedPanelGraphic badgeGraphic = badge.GetComponent<RoundedDashedPanelGraphic>();
        if (badgeGraphic == null)
            badgeGraphic = badge.AddComponent<RoundedDashedPanelGraphic>();
        badgeGraphic.FillColor = fillColor;
        badgeGraphic.BorderThickness = 0f;
        badgeGraphic.CornerRadius = 30f;

        Shadow shadow = badge.GetComponent<Shadow>();
        if (shadow == null)
            shadow = badge.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.22f);
        shadow.effectDistance = new Vector2(0f, -6f);
        shadow.useGraphicAlpha = true;

        Transform currentTitle = FindChildRecursive(badge.transform, "RoleTitle");
        TMP_Text title = currentTitle != null ? currentTitle.GetComponent<TMP_Text>() : null;
        if (title == null)
        {
            GameObject titleObject = CreateRect("RoleTitle", badge.transform, Vector2.zero, Vector2.one);
            title = titleObject.AddComponent<TextMeshProUGUI>();
        }

        SetStretch(title.rectTransform, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f));
        title.text = content;
        title.font = bodyText != null ? bodyText.font : title.font;
        title.fontStyle = FontStyles.Bold;
        title.enableAutoSizing = true;
        title.fontSize = 72f;
        title.fontSizeMin = 48f;
        title.fontSizeMax = 72f;
        title.alignment = TextAlignmentOptions.Center;
        title.color = Color.white;
        title.outlineColor = outlineColor;
        title.outlineWidth = 0.18f;
        title.raycastTarget = false;
        badge.transform.SetAsLastSibling();
        EditorUtility.SetDirty(badgeGraphic);
        EditorUtility.SetDirty(shadow);
        EditorUtility.SetDirty(title);
    }

    private static void ConfigureRoleText(Transform panel, string textName, string annotatedText)
    {
        Transform textTransform = FindChildRecursive(panel, textName);
        if (textTransform == null || !textTransform.TryGetComponent(out TMP_Text roleText))
            return;

        roleText.richText = true;
        roleText.enableAutoSizing = false;
        roleText.fontSize = 44f;
        roleText.text = annotatedText;
        EditorUtility.SetDirty(roleText);

        if (textTransform.TryGetComponent(out FuriganaText furigana))
        {
            furigana.SetAnnotatedText(annotatedText);
            EditorUtility.SetDirty(furigana);
        }
    }

    private static TMP_Text CreateText(Transform parent, string name, string content)
    {
        GameObject gameObject = CreateRect(name, parent, Vector2.zero, Vector2.one);
        TextMeshProUGUI text = gameObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.enableAutoSizing = true;
        text.fontSizeMin = 30f;
        text.fontSizeMax = 56f;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;

        TMP_Text reference = parent.GetComponentInChildren<TMP_Text>(true);
        if (reference != null)
            text.font = reference.font;
        return text;
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

    private static void SetStretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        if (rect == null)
            return;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void EnsureCanvasRenderer(GameObject target)
    {
        if (target.GetComponent<CanvasRenderer>() == null)
            target.AddComponent<CanvasRenderer>();
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
}
#endif
