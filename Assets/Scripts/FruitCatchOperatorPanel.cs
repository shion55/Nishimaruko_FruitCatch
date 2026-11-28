using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FruitCatch.Preparation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Settings = FruitCatchOperatorSettings;

[DefaultExecutionOrder(-10000)]
internal sealed class FruitCatchOperatorPanel : MonoBehaviour
{
    private static readonly string[] Tabs = { "運営", "ゲームバランス", "時間設定", "音響", "マイク", "演出", "システム" };
    private static readonly Color Surface = new(.055f, .09f, .13f, .99f);
    private static readonly Color Control = new(.13f, .22f, .29f);
    private static readonly Color ParameterSurface = new(.08f, .14f, .19f);
    private static readonly Color NumberSurface = new(.032f, .065f, .09f);
    private static readonly Color SliderTrack = new(.22f, .32f, .39f);
    private static readonly Color Accent = new(.24f, .59f, .57f);
    private static readonly Color SelectedControl = new(.12f, .37f, .35f);
    private static readonly Color Ink = new(.93f, .96f, .98f);
    private static readonly Color Secondary = new(.70f, .81f, .87f);
    private static FruitCatchOperatorPanel instance;
    private static int closedFrame = -1;
    internal static bool BlocksGameplayInput => instance != null &&
        (instance.visible || (closedFrame >= 0 && Time.frameCount <= closedFrame + 1));

    private bool visible, needsRebuild;
    private BaseInputModule textInputModule;
    private BaseInput previousTextInput, operatorTextInput;
    private int tab;
    private readonly int[] selectedPages = new int[Tabs.Length];
    private int microphonePage;
    private GameObject overlay, modal, fallbackEventSystem;
    private RectTransform panel, content;
    private CanvasGroup panelControls;
    private TMP_FontAsset font;
    private TMP_Text status, notice, gainInfo;
    private RectTransform gainFill;
    private readonly List<RectTransform> marks = new();
    private readonly List<Action> refreshRows = new();
    private readonly List<Button> tabButtons = new();
    private string[] microphones;
    private string message;
    private float updateAt, averageFrameTime;
    private FruitCatchGameSession game;
    private PreparationFlowController preparation;
    private PreparationPythonBridge bridge;
    private PreparationVoiceDetector detector;
    private FaceMovementController[] trackingControllers;
    private FallingItemSpawner spawner;
    private GameManager score;
    private MicrophoneAnalysis mic;
    private EaterCatchController eater;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Create()
    {
        closedFrame = -1;
        instance = new GameObject("FruitCatchOperatorPanel").AddComponent<FruitCatchOperatorPanel>();
        DontDestroyOnLoad(instance.gameObject);
        Settings.Initialize();
        SceneManager.sceneLoaded += instance.OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive) return;
        SetVisible(false);
        Settings.BindScene(scene);
        game = Settings.InScene<FruitCatchGameSession>();
        preparation = Settings.InScene<PreparationFlowController>();
        bridge = Settings.InScene<PreparationPythonBridge>();
        detector = Settings.InScene<PreparationVoiceDetector>();
        trackingControllers = Settings.InSceneComponents<FaceMovementController>().ToArray();
        spawner = Settings.InScene<FallingItemSpawner>();
        score = Settings.InScene<GameManager>();
        mic = Settings.InScene<MicrophoneAnalysis>();
        eater = Settings.InScene<EaterCatchController>();
        needsRebuild = true;
    }

    private void Update()
    {
        Settings.Tick();
        averageFrameTime = averageFrameTime <= 0 ? Time.unscaledDeltaTime :
            Mathf.Lerp(averageFrameTime, Time.unscaledDeltaTime, .08f);
        if (Application.isFocused && Keyboard.current?.f4Key.wasPressedThisFrame == true)
            SetVisible(!visible);
        if (!visible) return;
        InstallTextInput();
        if (needsRebuild) Rebuild();
        if (Time.unscaledTime < updateAt) return;
        updateAt = Time.unscaledTime + .15f;
        UpdateStatus();
        foreach (Action refresh in refreshRows) refresh();
    }

    private void OnApplicationQuit() => Settings.Save();
    private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void SetVisible(bool active)
    {
        if (visible == active) return;
        visible = active;
        EventSystem.current?.SetSelectedGameObject(null);
        if (active)
        {
            if (EventSystem.current == null)
            {
                fallbackEventSystem = new GameObject("Operator EventSystem", typeof(EventSystem),
                    typeof(InputSystemUIInputModule));
                fallbackEventSystem.transform.SetParent(transform, false);
            }
            if (overlay == null) Build();
            overlay.SetActive(true);
            needsRebuild = true;
        }
        else
        {
            closedFrame = Time.frameCount;
            overlay.SetActive(false);
            RestoreTextInput();
            if (fallbackEventSystem != null)
            {
                fallbackEventSystem.SetActive(false);
                Destroy(fallbackEventSystem);
            }
            CloseModal();
            FruitCatchAudioManager.Instance.StopPreview();
            Settings.Save();
        }
    }

    private void InstallTextInput()
    {
        BaseInputModule module = EventSystem.current?.currentInputModule;
        if (module == null || module == textInputModule) return;
        RestoreTextInput();
        if (operatorTextInput == null) operatorTextInput = gameObject.AddComponent<FruitCatchOperatorTextInput>();
        textInputModule = module;
        previousTextInput = module.inputOverride;
        module.inputOverride = operatorTextInput;
    }

    private void RestoreTextInput()
    {
        if (textInputModule != null && textInputModule.inputOverride == operatorTextInput)
            textInputModule.inputOverride = previousTextInput;
        textInputModule = null;
        previousTextInput = null;
    }

    private static RectTransform Node(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static Image ImageOn(RectTransform rect, Color color, bool raycast = false)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    private static void Layout(RectTransform rect, float height = -1, float width = -1, float flex = -1)
    {
        var layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
        layout.preferredWidth = width;
        layout.minWidth = width;
        layout.flexibleWidth = flex;
    }

    private TMP_Text Label(string text, Transform parent, float size = 18, Color? color = null)
    {
        var label = Node("Label", parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = color ?? Ink;
        label.alignment = TextAlignmentOptions.Left;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.richText = false;
        label.raycastTarget = false;
        return label;
    }

    private RectTransform Row(Transform parent, float height = 32)
    {
        RectTransform row = Node("Row", parent);
        Layout(row, height);
        var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        return row;
    }

    private Button Button(string text, Transform parent, Action action, float width = -1, bool active = true,
        float height = 28, float fontSize = 16)
    {
        RectTransform rect = Node(text, parent);
        Layout(rect, height, width, width < 0 ? 1 : 0);
        Image image = ImageOn(rect, Control, true);
        var button = rect.gameObject.AddComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = image;
        button.interactable = active;
        button.onClick.AddListener(() => action());
        TMP_Text label = Label(text, rect, fontSize);
        label.alignment = TextAlignmentOptions.Center;
        label.fontWeight = FontWeight.Medium;
        label.enableAutoSizing = true;
        label.fontSizeMin = Mathf.Min(13, fontSize);
        label.fontSizeMax = fontSize;
        label.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(3, 0), new Vector2(-3, 0));
        return button;
    }

    private void Build()
    {
        font = Resources.Load<FruitCatchOperatorFont>("FruitCatchOperatorFont")?.CreateRuntimeFont() ??
            Settings.InScene<TMP_Text>()?.font;
        RectTransform root = Node("Operator Canvas", transform);
        overlay = root.gameObject;
        var canvas = overlay.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32750;
        var scaler = overlay.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        overlay.AddComponent<GraphicRaycaster>();
        RectTransform scrim = Node("Input Shield", root);
        Stretch(scrim, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ImageOn(scrim, new Color(0, 0, 0, .49f), true);
        panel = Node("Panel", root);
        panel.sizeDelta = new Vector2(1184, 668);
        ImageOn(panel, Surface, true);
        panelControls = panel.gameObject.AddComponent<CanvasGroup>();
        var stack = Vertical(panel);
        stack.padding = new RectOffset(18, 18, 12, 12);
        stack.spacing = 6;

        RectTransform heading = Row(panel, 32);
        TMP_Text title = Label("FruitCatch　運営パネル", heading, 23);
        title.fontStyle = FontStyles.Bold;
        Layout(title.rectTransform, 32, 0, 1);
        Button("F4　閉じる", heading, () => SetVisible(false), 94);
        RectTransform tabs = Row(panel, 28);
        for (int i = 0; i < Tabs.Length; i++)
        {
            int index = i;
            tabButtons.Add(Button(Tabs[i], tabs, () =>
            {
                tab = index;
                message = null;
                FruitCatchAudioManager.Instance.StopPreview();
                needsRebuild = true;
            }, i == Settings.Balance ? 140 : 95, height: 27));
        }
        content = Node("Settings", panel);
        content.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        Vertical(content).spacing = 5;

        RectTransform footer = Row(panel, 28);
        notice = Label("", footer, 13, Secondary);
        notice.overflowMode = TextOverflowModes.Ellipsis;
        Layout(notice.rectTransform, 25, 0, 1);
        Button("タブを初期値に", footer, () => Confirm("このタブの設定を初期値に戻しますか？",
            () => { Settings.ResetValues(tab); needsRebuild = true; }), 122, height: 25, fontSize: 13);
        Button("全設定を初期値に", footer, () => Confirm("すべての設定を初期値に戻しますか？",
            () => { Settings.ResetValues(-1); needsRebuild = true; }), 132, height: 25, fontSize: 13);
        Button("JSON 書き出し", footer, Export, 110, height: 25, fontSize: 13);
        Button("JSON 読み込み", footer, () => Confirm("OperatorPreset.json を読み込みますか？", Import),
            110, height: 25, fontSize: 13);
    }

    private static VerticalLayoutGroup Vertical(RectTransform rect)
    {
        var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return layout;
    }

    private void Rebuild()
    {
        needsRebuild = false;
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            GameObject previous = content.GetChild(i).gameObject;
            previous.SetActive(false);
            Destroy(previous);
        }
        refreshRows.Clear();
        marks.Clear();
        status = gainInfo = null;
        for (int i = 0; i < tabButtons.Count; i++)
            tabButtons[i].image.color = i == tab ? SelectedControl : Control;
        if (tab == 0) BuildOperations();
        if (tab == Settings.SystemTab) BuildSystem();
        BuildParameterPages();
        UpdateStatus();
        foreach (Action refresh in refreshRows) refresh();
    }

    private const int ParametersPerPage = 14;
    private const int CategoriesPerRow = 6;
    private const int ParameterHeight = 54;
    private const int ColorParameterHeight = 94;

    private void BuildParameterPages()
    {
        if (tab == 0) return;
        var settings = Settings.Rows(tab).ToArray();
        bool hasDevices = tab == Settings.Mic;
        var pages = new List<(string Label, Settings.Setting[] Settings)>();
        if (hasDevices) pages.Add(("入力デバイス", null));
        if (!hasDevices && settings.Length <= ParametersPerPage && tab != Settings.Audio && tab != Settings.Visual)
        {
            pages.Add((Tabs[tab], settings));
        }
        else
        {
            foreach (var group in settings.GroupBy(setting =>
                         setting.Key.StartsWith("item.", StringComparison.Ordinal) && tab == Settings.Balance
                             ? "フルーツ・特殊アイテム" : setting.Group))
            {
                int count = group.Count();
                for (int start = 0; start < count; start += ParametersPerPage)
                {
                    string suffix = count <= ParametersPerPage ? "" : $" {start / ParametersPerPage + 1}";
                    pages.Add((group.Key + suffix, group.Skip(start).Take(ParametersPerPage).ToArray()));
                }
            }
        }

        int index = Mathf.Clamp(selectedPages[tab], 0, pages.Count - 1);
        if (pages.Count > 1)
        {
            RectTransform categories = null;
            for (int i = 0; i < pages.Count; i++)
            {
                if (i % CategoriesPerRow == 0) categories = Row(content, 27);
                int page = i;
                Button category = Button(pages[i].Label, categories, () =>
                {
                    if (selectedPages[tab] == page) return;
                    selectedPages[tab] = page;
                    if (tab == Settings.Audio) FruitCatchAudioManager.Instance.StopPreview();
                    needsRebuild = true;
                }, 184, height: 26, fontSize: 15);
                if (i == index) category.image.color = SelectedControl;
            }
        }

        var current = pages[index];
        if (hasDevices) BuildMicrophoneMeter();
        if (current.Settings == null)
        {
            BuildMicrophoneDevices();
            return;
        }

        var header = Row(content, 25);
        TMP_Text heading = Label(current.Label, header, 19);
        heading.fontStyle = FontStyles.Bold;
        Layout(heading.rectTransform, 25, 0, 1);
        if (current.Settings.Any(setting => setting.NextRun))
            Layout(Label("［次回］は次の本編から反映", header, 14, Secondary).rectTransform, 23, 230);
        if (tab == Settings.Audio)
            Button("試聴停止", header, () => FruitCatchAudioManager.Instance.StopPreview(), 90, height: 24);
        if (current.Settings.Any(setting => setting.Key == "time.registrationHold"))
        {
            TMP_Text trackerHelp = TextRow("", 23, Secondary);
            refreshRows.Add(() => trackerHelp.text = bridge?.TrackerRegistrationHoldDefault is float seconds
                ? $"顔を保持する既定時間　{seconds:0.##} 秒"
                : "顔を保持する時間の変更には対応するトラッカーが必要です。");
        }
        if (current.Label == Settings.BannerColorGroup)
            BuildBannerColorPreview();

        for (int i = 0; i < current.Settings.Length; i += 2)
        {
            int height = current.Settings[i].IsColor ? ColorParameterHeight : ParameterHeight;
            RectTransform row = Row(content, height);
            BuildSetting(current.Settings[i], row);
            if (i + 1 < current.Settings.Length)
                BuildSetting(current.Settings[i + 1], row);
            else
                Layout(Node("Empty column", row), height, 0, 1);
        }
    }

    private void BuildBannerColorPreview()
    {
        RectTransform row = Row(content, 58);
        RectTransform sample = Node("BannerPreview", row);
        Layout(sample, 48, 180);
        Image border = ImageOn(sample, Settings.BannerForeground);
        RectTransform inset = Node("Fill", sample);
        Stretch(inset, Vector2.zero, Vector2.one, Vector2.one * 2f, -Vector2.one * 2f);
        Image fill = ImageOn(inset, Settings.BannerBackground);
        TMP_Text text = Label("スタート！", inset, 20, Settings.BannerForeground);
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        TMP_Text scope = Label("両手をあげる案内・スタート！・そこまで！", row, 15, Secondary);
        Layout(scope.rectTransform, 28, 0, 1);
        refreshRows.Add(() =>
        {
            border.color = text.color = Settings.BannerForeground;
            fill.color = Settings.BannerBackground;
        });
    }

    private TMP_Text TextRow(string text, float height = 28, Color? color = null)
    {
        TMP_Text label = Label(text, content, 17, color);
        Layout(label.rectTransform, height);
        return label;
    }

    private void Section(string name) => TextRow(name, 37, new Color(.53f, .88f, .83f)).fontSize = 19;

    private void BuildSetting(Settings.Setting setting, RectTransform columns)
    {
        RectTransform cell = Node(setting.Key, columns);
        Layout(cell, setting.IsColor ? ColorParameterHeight : ParameterHeight, 0, 1);
        ImageOn(cell, ParameterSurface);
        TMP_Text label = Label(SettingCaption(setting), cell, 17);
        label.fontStyle = FontStyles.Bold;
        Stretch(label.rectTransform, Vector2.up, Vector2.one, new Vector2(12, -27), new Vector2(-12, -1));

        if (setting.IsColor)
        {
            BuildColorSetting(setting, cell);
            return;
        }

        RectTransform row = Row(cell, 26);
        Stretch(row, Vector2.zero, Vector2.right, new Vector2(12, 2), new Vector2(-12, 27));
        if (setting.Switch)
        {
            Button button = Button("", row, () => Settings.Set(setting, setting.Value == 0 ? 1 : 0), 88, height: 24);
            TMP_Text valueLabel = button.GetComponentInChildren<TMP_Text>();
            void RefreshSwitch()
            {
                valueLabel.text = setting.Value == 0 ? "無効" : "有効";
                button.image.color = setting.Value == 0 ? Control : SelectedControl;
            }
            RefreshSwitch();
            refreshRows.Add(RefreshSwitch);
            return;
        }
        Slider slider = BuildSlider(row, setting.Min, setting.Max, setting.Integer);
        slider.onValueChanged.AddListener(value => Settings.Set(setting, value));
        TMP_InputField field = BuildValueField(row, setting.Integer ? TMP_InputField.ContentType.IntegerNumber
            : TMP_InputField.ContentType.DecimalNumber, 96, 15);
        field.onEndEdit.AddListener(input =>
        {
            if (float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                Settings.Set(setting, value);
            field.SetTextWithoutNotify(Format(setting.Value));
        });
        void Refresh()
        {
            slider.SetValueWithoutNotify(setting.Value);
            if (!field.isFocused) field.SetTextWithoutNotify(Format(setting.Value));
        }
        Refresh();
        refreshRows.Add(Refresh);
        if (setting.ClipField == null) return;
        AudioClip clip = Settings.GetClip(setting);
        Button(clip != null ? "試聴" : "未設定", row, () => FruitCatchAudioManager.Instance.Preview(
            clip, () => setting.Value, setting.ClipField == "chiliSwaySound" ?
                Settings.Get("audio.chiliSwaySoundPitch") : 1, setting.Group == "BGM"), 62, clip != null, 23, 13);
        Button("停止", row, () => FruitCatchAudioManager.Instance.StopPreview(), 48, clip != null, 23, 13);
    }

    private void BuildColorSetting(Settings.Setting setting, RectTransform cell)
    {
        RectTransform hexRow = Row(cell, 25);
        Stretch(hexRow, Vector2.zero, Vector2.right, new Vector2(12, 36), new Vector2(-12, 61));
        Image swatch = ImageOn(Node("ColorSwatch", hexRow), Settings.GetColor(setting));
        Layout(swatch.rectTransform, 25, 35);
        TMP_InputField hex = BuildValueField(hexRow, TMP_InputField.ContentType.Standard, 112, 7);
        hex.textComponent.alignment = TextAlignmentOptions.Center;
        hex.onEndEdit.AddListener(input =>
        {
            string value = input.StartsWith("#", StringComparison.Ordinal) ? input : "#" + input;
            if (value.Length == 7 && ColorUtility.TryParseHtmlString(value, out Color color))
                Settings.SetColor(setting, color);
            else
                message = "色は #FFF8E1 のように6桁で入力してください。";
            hex.SetTextWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(Settings.GetColor(setting)));
        });

        RectTransform rgbRow = Row(cell, 25);
        Stretch(rgbRow, Vector2.zero, Vector2.right, new Vector2(12, 5), new Vector2(-12, 30));
        var sliders = new Slider[3];
        for (int i = 0; i < sliders.Length; i++)
        {
            int channel = i;
            Layout(Label("RGB"[i].ToString(), rgbRow, 14).rectTransform, 22, 17);
            sliders[i] = BuildSlider(rgbRow, 0, 255, true);
            sliders[i].onValueChanged.AddListener(value =>
            {
                Color32 color = Settings.GetColor(setting);
                color[channel] = (byte)value;
                Settings.SetColor(setting, color);
            });
        }
        refreshRows.Add(() =>
        {
            Color32 color = Settings.GetColor(setting);
            swatch.color = color;
            if (!hex.isFocused) hex.SetTextWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(color));
            for (int i = 0; i < sliders.Length; i++)
                sliders[i].SetValueWithoutNotify(color[i]);
        });
    }

    private static Slider BuildSlider(RectTransform row, float min, float max, bool wholeNumbers)
    {
        Slider slider = DefaultControls.CreateSlider(default).GetComponent<Slider>();
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
        slider.transform.SetParent(row, false);
        Layout((RectTransform)slider.transform, 23, 0, 1);
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = wholeNumbers;
        slider.transform.Find("Background").GetComponent<Image>().color = SliderTrack;
        slider.fillRect.GetComponent<Image>().color = Accent;
        slider.handleRect.GetComponent<Image>().color = Ink;
        return slider;
    }

    private TMP_InputField BuildValueField(RectTransform row, TMP_InputField.ContentType type, float width, int limit)
    {
        TMP_InputField field = TMP_DefaultControls.CreateInputField(default).GetComponent<TMP_InputField>();
        field.navigation = new Navigation { mode = Navigation.Mode.None };
        field.transform.SetParent(row, false);
        Layout((RectTransform)field.transform, 25, width);
        field.contentType = type;
        field.characterLimit = limit;
        field.textViewport.offsetMin = new Vector2(8, 0);
        field.textViewport.offsetMax = new Vector2(-8, 0);
        field.GetComponent<Image>().color = NumberSurface;
        if (font != null) field.fontAsset = font;
        field.textComponent.fontSize = 17;
        field.textComponent.color = Ink;
        field.textComponent.alignment = TextAlignmentOptions.MidlineRight;
        field.customCaretColor = true;
        field.caretColor = Ink;
        field.selectionColor = SelectedControl;
        field.placeholder.gameObject.SetActive(false);
        return field;
    }

    private string SettingCaption(Settings.Setting setting)
    {
        string prefix = tab == Settings.Balance && setting.Key.StartsWith("item.", StringComparison.Ordinal)
            ? setting.Group + "　/　" : "";
        string units = setting.Key switch
        {
            "spawner.minFallSpeed" or "spawner.maxFallSpeed" or "pitchController.maxFallSpeed" or
                "character.moveSpeed" or "character.slideSpeed" or "eater.bounceSpeed" => "（ユニット／秒）",
            "spawner.feverFruitsPerSecond" => "（個／秒）",
            "audio.master" or "audio.bgm" or "audio.se" => "（0〜1）",
            _ when setting.Key.EndsWith(".fruitsPerSecond", StringComparison.Ordinal) => "（個／秒）",
            _ when setting.Key.EndsWith(".scoreDelta", StringComparison.Ordinal) => "（点）",
            _ when setting.Key.EndsWith("Volume", StringComparison.Ordinal) => "（0〜1）",
            _ => ""
        };
        string name = setting.Label;
        if (units == "（個／秒）" && name.EndsWith("／秒", StringComparison.Ordinal))
            name = name.Substring(0, name.Length - 2);
        return prefix + name + units + (setting.NextRun ? "　［次回］" : "");
    }

    private void BuildOperations()
    {
        Section("接続と進行");
        status = TextRow("", 110);
        status.textWrappingMode = TextWrappingModes.Normal;
        Section("画面とプレイ");
        var scenes = Row(content);
        Button("Preparationへ戻る（F1）", scenes, () => Navigate(false), active: game != null || preparation != null);
        Button("登録を維持して本編を再スタート", scenes, () => Navigate(true), active: game != null || preparation != null);
        var prep = Row(content);
        Button("顔登録を解除", prep, () =>
        {
            PreparationPythonBridge.ClearRegistration();
            bridge?.ForgetRegistration();
            if (trackingControllers != null)
                foreach (FaceMovementController controller in trackingControllers) controller.ForgetPosition();
        });
        Button next = Button("準備の次のステップへ", prep, () => preparation?.OperatorNext(), active: preparation != null);
        refreshRows.Add(() => next.interactable = preparation != null && preparation.CanOperateStep);
        Button("チュートリアルをスキップ", prep, () => Navigate(true), active: preparation != null);
        var play = Row(content);
        Button start = Button("フィーバー開始", play, () => game?.OperatorFever(true), active: game != null);
        Button end = Button("フィーバー終了", play, () => game?.OperatorFever(false), active: game != null);
        Button finish = Button("本編を終了して結果発表へ", play, () => game?.OperatorFinish(), active: game != null);
        refreshRows.Add(() =>
        {
            bool canOperate = game != null && game.CanOperatePlay;
            start.interactable = canOperate && spawner != null && !spawner.IsFever;
            end.interactable = canOperate && spawner != null && spawner.IsFever;
            finish.interactable = canOperate;
        });
        Section("運営操作");
        var ops = Row(content);
        Button("ランキング初期化", ops, () => Confirm("保存済みのランキングをすべて消去しますか？", () =>
        {
            if (game != null) game.ResetLocalRanking();
            else FruitCatchRanking.ClearSavedScores();
            message = "ランキングを初期化しました。";
        }));
        Button("ゲームを終了", ops, () => Confirm("ゲームを終了しますか？", () =>
        {
            SetVisible(false);
            game?.StopForOperator();
            preparation?.StopForOperator();
            FruitCatchAudioManager.StopGameAudioIfAvailable();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }));
        Button mute = Button("", ops, Settings.ToggleMute);
        TMP_Text muteText = mute.GetComponentInChildren<TMP_Text>();
        refreshRows.Add(() =>
        {
            muteText.text = Settings.Muted ? "緊急ミュートを解除" : "緊急ミュート";
            mute.image.color = Settings.Muted ? new Color(.68f, .23f, .22f) : Control;
        });
    }

    private void Navigate(bool replay)
    {
        SetVisible(false);
        if (game != null)
        {
            if (replay) game.OperatorReplay();
            else game.OperatorPreparation();
        }
        else if (preparation != null) preparation.OperatorRestart(replay);
    }

    private void BuildMicrophoneDevices()
    {
        TMP_Text device = TextRow("", 27);
        refreshRows.Add(() => device.text = mic == null ? "マイク入力：取得できません"
            : "使用中　" + (mic.ActiveDeviceName.Length == 0 ? "OSの既定" : mic.ActiveDeviceName));
        var actions = Row(content);
        Button("デバイスを再検索", actions, () =>
        {
            microphones = Microphone.devices;
            microphonePage = 0;
            needsRebuild = true;
        }, 148);
        Button("再接続", actions, () => ChangeMic(Settings.SelectedMicrophone), 90, active: mic != null);
        Button("OSの既定を使用", actions, () => ChangeMic(""), 148, active: mic != null);
        microphones ??= Microphone.devices;
        const int devicesPerPage = 12;
        int pageCount = Mathf.Max(1, (microphones.Length + devicesPerPage - 1) / devicesPerPage);
        microphonePage = Mathf.Clamp(microphonePage, 0, pageCount - 1);
        if (pageCount > 1)
        {
            RectTransform navigation = Row(content);
            Button("前の一覧", navigation, () => { microphonePage--; needsRebuild = true; }, 90, microphonePage > 0);
            Layout(Label($"{microphonePage + 1} / {pageCount}", navigation, 14).rectTransform, 24, 56);
            Button("次の一覧", navigation, () => { microphonePage++; needsRebuild = true; }, 90, microphonePage + 1 < pageCount);
        }
        if (microphones.Length == 0)
            TextRow("利用できるマイクが見つかりません。", 23, Secondary);
        int start = microphonePage * devicesPerPage;
        int end = Mathf.Min(start + devicesPerPage, microphones.Length);
        for (int i = start; i < end; i += 2)
        {
            RectTransform row = Row(content);
            string first = microphones[i];
            Button(first, row, () => ChangeMic(first), 550, active: mic != null);
            if (i + 1 < end)
            {
                string second = microphones[i + 1];
                Button(second, row, () => ChangeMic(second), 550, active: mic != null);
            }
        }
    }

    private void BuildMicrophoneMeter()
    {
        gainInfo = TextRow("", 25);
        RectTransform track = Node("Gain", content);
        Layout(track, 13);
        ImageOn(track, Control);
        gainFill = Node("Gain fill", track);
        ImageOn(gainFill, Accent);
        for (int i = 0; i < 4; i++)
        {
            RectTransform mark = Node("Threshold", track);
            ImageOn(mark, Color.Lerp(Color.white, new Color(1, .55f, .24f), i / 3f));
            marks.Add(mark);
        }
        TextRow("目印は発声練習・フルーツ練習・本編の開口・最大開口の各しきい値です。", 20, Secondary)
            .fontSize = 13;
    }

    private void ChangeMic(string name) => message = Settings.SelectMicrophone(name)
        ? "マイクの接続を開始しました。" : "マイクに接続できませんでした。";

    private void BuildSystem()
    {
        Section("実測値");
        status = TextRow("", 150);
        status.textWrappingMode = TextWrappingModes.Normal;
        var button = Button("", Row(content), () => Settings.SetMouseDebug(!MouseDebugInput.Current));
        TMP_Text label = button.GetComponentInChildren<TMP_Text>();
        refreshRows.Add(() => label.text = MouseDebugInput.Current ? "マウスデバッグ　ON" : "マウスデバッグ　OFF");
    }

    private void UpdateStatus()
    {
        if (notice != null)
            notice.text = Settings.Muted ? "緊急ミュート中" : Settings.SaveError != null
                ? "設定を保存できませんでした。 " + Settings.SaveError : message ?? "設定はこの端末に保存されます。";
        FaceMovementController tracking = trackingControllers?.FirstOrDefault(controller =>
            controller != null && controller.isActiveAndEnabled);
        float? age = tracking?.LastPositionAge;
        string tracked = tracking == null ? "取得できません" :
            tracking.TryGetNormalizedPosition(out _) ? "追跡中" : "現在は追跡なし";
        string camera = bridge == null ? "この画面では取得できません" : bridge.HasPreview ? "受信中"
            : bridge.HasPreviewConnection ? "トラッカー接続済み・映像なし" : "トラッカー未接続";
        string microphone = mic == null ? "取得できません" : mic.GetCapturePosition() >= 0 ? "受信中" : "入力なし";
        if (status != null && tab == 0)
            status.text = $"シーン　{SceneManager.GetActiveScene().name}　/　{preparation?.OperatorStage ?? game?.OperatorStage ?? "—"}\n" +
                $"カメラ　{camera}　　マイク　{microphone}\n" +
                $"人物登録　{(PreparationPythonBridge.RegistrationStatus is bool registered ? registered ? "登録済み" : "未登録" : "取得できません")}　　人物追跡　{tracked}";
        if (status != null && tab == Settings.SystemTab)
        {
            string fps = bridge != null && bridge.HasPreview && bridge.ReceivedFramesPerSecond is float received
                ? $"{received:F1} FPS" : "—";
            string udp = tracking == null ? "取得できません" : !tracking.HasReceiver ? "受信ポートを開けません"
                : age.HasValue ? $"最終受信 {age.Value:F2} 秒前" : "受信待ち";
            status.text = $"Unity　{(averageFrameTime > 0f ? $"{1f / averageFrameTime:F1}" : "—")} FPS　　カメラ　{fps}\n" +
                $"Python映像　{camera}　　追跡UDP　{udp}\n人物追跡　{tracked}\n" +
                $"落下物　{spawner?.ActiveItemCount.ToString() ?? "—"}　　スコア　{score?.Score.ToString() ?? "—"}　　起動後のプレイ完了　{Settings.CompletedPlays} 回";
        }
        if (gainInfo == null) return;
        float gain = mic?.Gain ?? 0;
        string pitch = mic != null && mic.PitchHz > 0 ? $"{mic.PitchHz:F1} Hz" : "—";
        string voice = detector != null ? (detector.IsVoiceDetected ? "検出中" : "検出なし") :
            mic != null && Settings.HasSetting("voice.gainThreshold") ? (gain >= Settings.Get("voice.gainThreshold") ? "検出中" : "検出なし") : "—";
        bool? mouthOpen = eater != null && eater.isActiveAndEnabled ? eater.OperatorMouthOpen :
            preparation != null && preparation.CurrentRequiredSignal == PreparationSignal.FruitPractice &&
            detector != null ? detector.IsVoiceDetected : null;
        gainInfo.text = $"Gain　{(mic == null ? "—" : $"{gain:F4}")}　　ピッチ　{pitch}　　発声　{voice}　　開口　" +
            (mouthOpen.HasValue ? (mouthOpen.Value ? "開" : "閉") : "—");
        string[] keys = { "voice.gainThreshold", "voice.practiceMouthThreshold", "eater.mouthOpenThreshold", "eater.maxGain" };
        float scale = Mathf.Max(.1f, gain);
        foreach (string key in keys) scale = Mathf.Max(scale, Settings.Get(key) * 1.1f);
        Stretch(gainFill, Vector2.zero, new Vector2(gain / scale, 1), Vector2.zero, Vector2.zero);
        for (int i = 0; i < marks.Count; i++)
        {
            float position = Settings.Get(keys[i]) / scale;
            marks[i].gameObject.SetActive(Settings.HasSetting(keys[i]));
            Stretch(marks[i], new Vector2(position, 0), new Vector2(position, 1), new Vector2(-1, 0), new Vector2(1, 0));
        }
    }

    private void Confirm(string question, Action action)
    {
        CloseModal();
        panelControls.interactable = false;
        RectTransform shade = Node("Confirmation", overlay.transform);
        modal = shade.gameObject;
        Stretch(shade, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ImageOn(shade, new Color(0, 0, 0, .7f), true);
        RectTransform box = Node("Dialog", shade);
        box.sizeDelta = new Vector2(560, 155);
        ImageOn(box, Surface, true);
        var layout = Vertical(box);
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.childForceExpandHeight = true;
        Label(question, box, 20).alignment = TextAlignmentOptions.Center;
        var buttons = Row(box);
        Button("キャンセル", buttons, CloseModal);
        Button("実行する", buttons, () => { CloseModal(); action(); });
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void CloseModal()
    {
        if (modal == null) return;
        modal.SetActive(false);
        Destroy(modal);
        modal = null;
        panelControls.interactable = true;
    }

    private void Export()
    {
        if (Settings.Export(out string error))
        {
            GUIUtility.systemCopyBuffer = Settings.PresetPath;
            message = "OperatorPreset.json を保存しました。場所をクリップボードにコピーしました。";
        }
        else message = "書き出せませんでした。 " + error;
    }

    private void Import()
    {
        if (Settings.Import(out string error))
        {
            message = "OperatorPreset.json を読み込みました。";
            needsRebuild = true;
        }
        else message = "読み込めませんでした。 " + error;
    }

    private static string Format(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
