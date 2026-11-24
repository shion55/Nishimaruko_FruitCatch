#if UNITY_EDITOR
using System.IO;
using FruitCatch.Preparation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PreparationRoleCardInstaller
{
    private const string ScenePath = "Assets/Scenes/Preparation.unity";
    private static readonly Color CardFill = new Color32(221, 244, 255, 248);
    private static readonly Color Orange = new Color32(242, 132, 38, 255);

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

        GameObject facePanel = FindInScene(scene, "03_FaceAndCarrier");
        if (facePanel != null)
        {
            RemoveChild(facePanel.transform, "CarrierImage");
            ConfigureRoleText(
                facePanel.transform,
                "CarrierExplanation",
                "<size=64><b><color=#F28426>トコミン</color></b></size>\n\n" +
                "[顔|かお]を[横|よこ]に[動|うご]かして\nパクッチョを はこんであげよう");
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
                "<size=64><b><color=#F28426>パクッチョ</color></b></size>\n\n" +
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
                new Vector2(0.46f, 0.12f),
                new Vector2(0.93f, 0.7f));
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (openedForInstall)
            EditorSceneManager.CloseScene(scene, true);

        Debug.Log("承認済みの役割カード・強調表示・デバッグ初期非表示を適用しました。");
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
        graphic.FillColor = CardFill;
        graphic.BorderColor = Orange;
        graphic.CornerRadius = 34f;
        graphic.BorderThickness = 7f;
        graphic.DashLength = 22f;
        graphic.GapLength = 14f;

        textTransform.SetParent(card.transform, false);
        SetStretch(textTransform.GetComponent<RectTransform>(), new Vector2(0.07f, 0.07f), new Vector2(0.93f, 0.93f));
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
