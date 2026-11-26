#if UNITY_EDITOR
using System;
using System.Linq;
using FruitCatch.Preparation;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PreparationPracticeInstaller
{
    private const string ScenePath = "Assets/Scenes/Preparation.unity";
    private const string Art = "Assets/Image/honbansozai/";
    private static readonly Color Blue = new Color32(12, 79, 115, 255);
    private static readonly Color Coral = new Color32(160, 67, 51, 255);

    [MenuItem("FruitCatch/Preparationの操作練習を追加")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var flow = scene.GetRootGameObjects().Select(root => root.GetComponent<PreparationFlowController>())
                .First(value => value != null);
            var serialized = new SerializedObject(flow);
            var steps = serialized.FindProperty("steps");
            if (steps.arraySize == 8)
            {
                Validate(steps);
                return;
            }
            if (steps.arraySize != 6)
                throw new InvalidOperationException("想定した6ステップのPreparationが見つかりません。");

            var roles = Panel(steps, 1);
            var voicePanel = Panel(steps, 3);
            var face = Panel(steps, 2);
            var font = roles.GetComponentInChildren<TMP_Text>(true).font;
            var voice = flow.GetComponent<PreparationVoiceDetector>();
            var voiceSettings = new SerializedObject(voice);
            var mouth = (Sprite)voiceSettings.FindProperty("mouthOpenSprite").objectReferenceValue;
            var closed = ((Image)voiceSettings.FindProperty("eaterImage").objectReferenceValue).sprite;
            var fruitSprite = PrefabSprite("Fruit");

            var explanation = roles.GetComponentsInChildren<TMP_Text>(true).First(text => text.name == "Explanation");
            explanation.text = "ふたりで あそぼう！";
            if (explanation.TryGetComponent<FuriganaText>(out var ruby))
                ruby.SetAnnotatedText(explanation.text);
            StyleRoleText(explanation, 98, new Color32(24, 49, 68, 255));
            Place(explanation.rectTransform, .5f, .80f, 1320, 145);
            AddRole(roles.transform, font, SpriteAt("robo-wait"), .27f, "トコミン", "からだを 左右へ", new Color32(14, 58, 83, 255));
            AddRole(roles.transform, font, closed, .73f, "パクッチョ", "こえを 出して\n口をひらく", new Color32(117, 38, 45, 255));

            var movement = AddPractice(face.transform, flow, voice, font, closed, mouth, fruitSprite, false);
            var eating = AddPractice(voicePanel.transform, flow, voice, font, closed, mouth, fruitSprite, true);

            steps.GetArrayElementAtIndex(1).FindPropertyRelative("requiredHoldSeconds").floatValue = 4.5f;
            steps.GetArrayElementAtIndex(3).FindPropertyRelative("requiredHoldSeconds").floatValue = 0.8f;
            steps.GetArrayElementAtIndex(4).FindPropertyRelative("requiredHoldSeconds").floatValue = 5f;
            Insert(steps, 3, movement, "トコミンの移動練習", PreparationSignal.MovementPractice);
            Insert(steps, 5, eating, "ふたりでくだもの練習", PreparationSignal.FruitPractice);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Validate(new SerializedObject(flow).FindProperty("steps"));
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Preparationを保存できませんでした。");
            Debug.Log("Preparation tutorial: saved and verified eight configured steps.");
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void Validate(SerializedProperty steps)
    {
        var expected = new[] { PreparationSignal.HandsRaised, PreparationSignal.Timed,
            PreparationSignal.FaceAligned, PreparationSignal.MovementPractice, PreparationSignal.VoiceDetected,
            PreparationSignal.FruitPractice, PreparationSignal.Timed, PreparationSignal.Timed };
        for (int i = 0; i < expected.Length; i++)
            if ((PreparationSignal)steps.GetArrayElementAtIndex(i).FindPropertyRelative("requiredSignal").enumValueIndex != expected[i] ||
                Panel(steps, i) == null)
                throw new InvalidOperationException($"Preparationのステップ {i + 1} の設定が不正です。");
        foreach (int index in new[] { 3, 5 })
        {
            var panel = Panel(steps, index);
            if (!panel.TryGetComponent<PreparationPractice>(out _) ||
                panel.GetComponentInChildren<FaceMovementController>(true) == null || panel.activeSelf)
                throw new InvalidOperationException($"練習パネル {index + 1} の設定が不正です。");
        }
        Debug.Log("Preparation tutorial: scene wiring valid.");
    }

    private static GameObject AddPractice(Transform template, PreparationFlowController flow, PreparationVoiceDetector voice,
        TMP_FontAsset font, Sprite closed, Sprite mouth, Sprite fruitSprite, bool eating)
    {
        var panel = Rect(template.parent, eating ? "FruitPractice" : "MovementPractice");
        panel.SetSiblingIndex(template.GetSiblingIndex() + 1);
        panel.anchorMax = Vector2.one;
        panel.anchorMin = Vector2.zero;
        panel.sizeDelta = Vector2.zero;
        panel.gameObject.SetActive(false);
        var backdrop = Rect(panel, "AdjustableReadingPanel");
        backdrop.anchorMin = new Vector2(.10f, .11f);
        backdrop.anchorMax = new Vector2(.90f, .91f);
        backdrop.sizeDelta = Vector2.zero;
        var templateImage = template.Find("AdjustableReadingPanel")?.GetComponent<Image>();
        var background = backdrop.gameObject.AddComponent<Image>();
        background.color = templateImage == null ? new Color32(255, 251, 235, 245) : templateImage.color;
        background.raycastTarget = false;
        TextLabel(panel, font, "Prompt", eating ? "ふたりで くだものを たべよう！" : "からだを 左右に うごかそう！",
            .5f, .82f, 56, eating ? Coral : Blue);
        var feedback = TextLabel(panel, font, "Feedback", "", .5f, .195f, 36, Blue);
        var retry = ButtonLabel(panel, font, "RetryRegistration", "顔を あわせ直す",
            .29f, .115f, .25f, .06f, flow.RetryRegistration).transform.parent.gameObject;
        retry.SetActive(false);
        var character = Rect(panel, "Character");
        Place(character, .5f, .36f, 0, 0);
        var carrier = Picture(character, "Tokomin", SpriteAt(eating ? "robo-hold" : "robo-wait"), .5f, .5f, 203);
        Image eater = null, fruit = null;
        Graphic left = null, right = null;
        var celebration = Star(panel, "SuccessEffect", .5f, .65f, 150);
        if (eating)
        {
            eater = Picture(character, "Pakuccho", closed, .5f, .5f, 164);
            eater.rectTransform.anchoredPosition = new Vector2(0f, 112f);
            fruit = Picture(panel, "PracticeFruit", fruitSprite, .5f, .73f, 90);
        }
        else
        {
            left = Star(panel, "LeftGoal", .17f, .51f, 100);
            right = Star(panel, "RightGoal", .83f, .51f, 100);
        }

        var driver = new GameObject("PracticeMovement");
        driver.transform.SetParent(panel, false);
        var receiver = new SerializedObject(driver.AddComponent<FaceMovementController>());
        receiver.FindProperty("movefacetoggle").boolValue = false;
        receiver.ApplyModifiedPropertiesWithoutUndo();
        var movement = driver.AddComponent<CharacterPairController>();
        var motion = new SerializedObject(movement);
        motion.FindProperty("allowKeyboardMovement").boolValue = false;
        motion.FindProperty("moveSpeed").floatValue = 5f;
        motion.ApplyModifiedPropertiesWithoutUndo();
        var animation = new SerializedObject(driver.AddComponent<CarrierAnimator>());
        Set(animation, "uiImage", carrier);
        Set(animation, "frontSprite", carrier.sprite);
        var walk = animation.FindProperty("walkSprites");
        walk.arraySize = 2;
        walk.GetArrayElementAtIndex(0).objectReferenceValue = SpriteAt("robo-walk1");
        walk.GetArrayElementAtIndex(1).objectReferenceValue = SpriteAt("robo-walk2");
        animation.FindProperty("spritesFaceRight").boolValue = false;
        animation.ApplyModifiedPropertiesWithoutUndo();
        var practice = new SerializedObject(panel.gameObject.AddComponent<PreparationPractice>());
        Set(practice, "flow", flow);
        Set(practice, "voice", voice);
        Set(practice, "movement", movement);
        Set(practice, "character", character);
        Set(practice, "carrier", carrier);
        if (eating) Set(practice, "gameplayFruitPrefab",
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Fruit.prefab"));
        else practice.FindProperty("minimumTravel").floatValue = .32f;
        Set(practice, "leftGoal", left);
        Set(practice, "rightGoal", right);
        Set(practice, "eater", eater);
        Set(practice, "fruit", fruit);
        Set(practice, "openMouth", mouth);
        Set(practice, "celebration", celebration);
        Set(practice, "feedback", feedback);
        Set(practice, "registrationRetry", retry);
        practice.ApplyModifiedPropertiesWithoutUndo();
        return panel.gameObject;
    }

    private static void AddRole(Transform panel, TMP_FontAsset font, Sprite sprite, float x,
        string title, string action, Color theme)
    {
        Picture(panel, title, sprite, x, .50f, 330);
        var name = TextLabel(panel, font, title + "Name", title, x, .30f, 68, theme);
        var description = TextLabel(panel, font, title + "Action", action, x, .19f, 48, theme);
        Place(name.rectTransform, x, .30f, 720, 94);
        Place(description.rectTransform, x, .19f, 720, 126);
        StyleRoleText(name, 68, theme);
        StyleRoleText(description, 48, theme);
    }

    private static void StyleRoleText(TMP_Text label, float size, Color textColor)
    {
        label.enableAutoSizing = false;
        label.fontSize = size;
        label.fontStyle = FontStyles.Bold;
        label.color = textColor;
        label.extraPadding = true;
    }

    private static TMP_Text ButtonLabel(Transform parent, TMP_FontAsset font, string name, string caption,
        float x, float y, float width, float height, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(parent, name);
        rect.anchorMin = new Vector2(x - width / 2, y - height / 2);
        rect.anchorMax = new Vector2(x + width / 2, y + height / 2);
        rect.sizeDelta = Vector2.zero;
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color32(233, 248, 254, 255);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        UnityEventTools.AddPersistentListener(button.onClick, action);
        var label = TextLabel(rect, font, "Label", caption, .5f, .5f, 26, Blue);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.sizeDelta = Vector2.zero;
        label.enableAutoSizing = true;
        label.fontSizeMin = 13;
        label.fontSizeMax = 26;
        return label;
    }

    private static TMP_Text TextLabel(Transform parent, TMP_FontAsset font, string name,
        string value, float x, float y, float size, Color color)
    {
        var rect = Rect(parent, name);
        Place(rect, x, y, parent.name == "02_TwoPlayers" ? 680 : 1160, 100);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = value;
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    private static Graphic Star(Transform parent, string name, float x, float y, float size)
    {
        var rect = Rect(parent, name);
        Place(rect, x, y, size, size);
        var star = rect.gameObject.AddComponent<PreparationOvalGraphic>();
        star.star = true;
        star.color = new Color32(255, 215, 88, 255);
        star.raycastTarget = false;
        return star;
    }

    private static Image Picture(Transform parent, string name, Sprite sprite, float x, float y, float size)
    {
        if (sprite == null) throw new InvalidOperationException(name + " のSpriteがありません。");
        var rect = Rect(parent, name);
        Place(rect, x, y, size, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private static RectTransform Rect(Transform parent, string name)
    {
        var obj = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
        obj.transform.SetParent(parent, false);
        return (RectTransform)obj.transform;
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(x, y);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void Insert(SerializedProperty steps, int index, GameObject panel, string name, PreparationSignal signal)
    {
        steps.InsertArrayElementAtIndex(index);
        var step = steps.GetArrayElementAtIndex(index);
        step.FindPropertyRelative("displayName").stringValue = name;
        step.FindPropertyRelative("panel").objectReferenceValue = panel;
        step.FindPropertyRelative("requiredSignal").enumValueIndex = (int)signal;
        step.FindPropertyRelative("requiredHoldSeconds").floatValue = 0;
        step.FindPropertyRelative("showCountdown").boolValue = false;
    }

    private static GameObject Panel(SerializedProperty steps, int i) =>
        (GameObject)steps.GetArrayElementAtIndex(i).FindPropertyRelative("panel").objectReferenceValue;

    private static void Set(SerializedObject obj, string name, UnityEngine.Object value) =>
        obj.FindProperty(name).objectReferenceValue = value;

    private static Sprite SpriteAt(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png");

    private static Sprite PrefabSprite(string name) => AssetDatabase
        .LoadAssetAtPath<GameObject>("Assets/Prefab/" + name + ".prefab")
        .GetComponentInChildren<SpriteRenderer>(true).sprite;
}
#endif
