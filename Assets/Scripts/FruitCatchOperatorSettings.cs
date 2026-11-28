using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using FruitCatch.Preparation;

internal static class FruitCatchOperatorSettings
{
    internal const int Balance = 1, TimeTab = 2, Audio = 3, Mic = 4, Visual = 5, SystemTab = 6;
    internal const string MicrophoneKey = "FruitCatch.SelectedMicrophone";
    internal const string BannerColorGroup = "バナー配色";
    private const string BannerBackgroundKey = "visual.bannerBackground";
    private const string BannerForegroundKey = "visual.bannerForeground";

    [Serializable] private sealed class Entry { public string key; public float value; }
    [Serializable] private sealed class Document
    {
        public int version;
        public List<Entry> values;
        public bool hasMicrophone;
        public string microphone;
    }
    [Serializable] private sealed class Defaults { public List<Setting> rows; }

    [Serializable]
    internal sealed class Setting
    {
        public string Key, Label, Group, ClipField;
        public int Tab, Part;
        public float Min, Max, Initial;
        public bool Switch, Integer, NextRun, IsColor;
        [NonSerialized] internal object Target;
        [NonSerialized] internal FieldInfo Field;
        [NonSerialized] internal List<FaceMovementController> TrackingTargets;
        internal float Value => values.TryGetValue(Key, out float value) ? Normalize(value) : Initial;

        internal float Normalize(float value)
        {
            value = Mathf.Clamp(value, Min, Max);
            if (Key == "time.registrationHold" && value > 0f) value = Mathf.Max(.1f, value);
            return Integer || Switch ? Mathf.Round(value) : value;
        }

        internal void Apply()
        {
            if (Field == null || Target == null || (Target is UnityEngine.Object o && o == null))
                return;
            float value = Value;
            if (TrackingTargets != null)
            {
                foreach (FaceMovementController tracking in TrackingTargets)
                    if (tracking != null) Field.SetValue(tracking, value);
                return;
            }
            if (Field.FieldType == typeof(Vector2))
            {
                var vector = (Vector2)Field.GetValue(Target);
                vector[Part] = value;
                Field.SetValue(Target, vector);
            }
            else if (Integer) Field.SetValue(Target, (int)value);
            else if (Switch) Field.SetValue(Target, value != 0);
            else Field.SetValue(Target, value);
        }
    }

    private static readonly Dictionary<string, float> values = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Setting> settings = new(StringComparer.Ordinal);
    private static readonly List<Setting> rows = new();
    private static readonly Dictionary<ItemDefinition, ItemDefinition> items = new();
    private static readonly Dictionary<PitchFallSpeedSettings, PitchFallSpeedSettings> pitchAssets = new();
    private static bool loaded, dirty;
#if UNITY_EDITOR
    private static bool collectingDefaults;
#endif
    private static float saveAt;
    private static string microphone;
    private static FruitCatchGameSession bannerGame;
    private static RoundedDashedPanelGraphic preparationBanner;
    private static FuriganaText preparationBannerText;
    internal static bool Muted { get; private set; }
    internal static int CompletedPlays { get; private set; }
    internal static string SaveError { get; private set; }
    internal static string PresetPath => Path.Combine(Application.persistentDataPath, "OperatorPreset.json");
    private static string SettingsPath => Path.Combine(Application.persistentDataPath, "OperatorSettings.json");
    internal static IEnumerable<Setting> Rows(int tab) => rows.Where(row =>
        (row.Tab == tab && row.Key != "system.mouse") ||
        (tab == Balance && row.Key.StartsWith("pitch.", StringComparison.Ordinal)) ||
        (tab == Mic && row.Key == "audio.voiceFeedbackVolume"));
    internal static float Get(string key) => settings.TryGetValue(key, out Setting setting) ? setting.Value : 0;
    internal static bool Enabled(string key) => Get(key) != 0;
    internal static bool HasSetting(string key) => settings.ContainsKey(key);
    internal static bool HasOverride(string key) => values.ContainsKey(key);
    internal static Color32 BannerBackground => GetColor(settings[BannerBackgroundKey]);
    internal static Color32 BannerForeground => GetColor(settings[BannerForegroundKey]);

    // RGB uses 24 bits, which fit exactly in the existing single-precision settings format.
    private static int PackColor(Color32 color) => color.r << 16 | color.g << 8 | color.b;

    internal static Color32 GetColor(Setting setting)
    {
        int rgb = (int)setting.Value;
        return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8 & 255), (byte)(rgb & 255), 255);
    }

    internal static void SetColor(Setting setting, Color32 color) => Set(setting, PackColor(color));
    internal static AudioClip GetClip(Setting setting) => setting.Target?.GetType()
        .GetField(setting.ClipField, BindingFlags.Instance | BindingFlags.NonPublic)
        ?.GetValue(setting.Target) as AudioClip;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        values.Clear();
        settings.Clear();
        rows.Clear();
        items.Clear();
        pitchAssets.Clear();
        loaded = dirty = Muted = false;
        microphone = null;
        bannerGame = null;
        preparationBanner = null;
        preparationBannerText = null;
        SaveError = null;
        CompletedPlays = 0;
    }

    internal static void Initialize()
    {
        if (loaded) return;
        loaded = true;
        Read(SettingsPath, out _);
        AddOptions();
        TextAsset defaults = Resources.Load<TextAsset>("FruitCatchOperatorDefaults");
        if (defaults != null)
        {
            foreach (Setting row in JsonUtility.FromJson<Defaults>(defaults.text).rows)
            {
                if (settings.ContainsKey(row.Key)) continue;
                settings.Add(row.Key, row);
                rows.Add(row);
            }
        }
        MouseDebugInput.SetEnabled(Enabled("system.mouse"));
        RefreshMaster();
    }

    internal static void FinishPlay() => CompletedPlays++;
    internal static void ToggleMute() { Muted = !Muted; RefreshMaster(); }
    internal static void SetMouseDebug(bool active)
    {
        MouseDebugInput.SetEnabled(active);
        values["system.mouse"] = active ? 1 : 0;
        MarkDirty();
    }
    private static void RefreshMaster() => AudioListener.volume = Muted ? 0f : Get("audio.master");

    internal static void Set(Setting setting, float value)
    {
        if (float.IsInfinity(value) || float.IsNaN(value)) return;
        value = setting.Normalize(value);
        if (setting.IsColor ? value == setting.Value : Mathf.Approximately(value, setting.Value)) return;
        values[setting.Key] = value;
        ConstrainPairs(setting.Key);
        ApplyLive();
        MarkDirty();
    }

    private static void ConstrainPairs(string key = null)
    {
        Pair("spawner.minFallSpeed", "spawner.maxFallSpeed", key);
        Pair("spawner.firstFeverWindow.x", "spawner.firstFeverWindow.y", key);
        Pair("tracking.inputMinX", "tracking.inputMaxX", key, 0.01f);
        Pair("microphone.minPitchHz", "microphone.maxPitchHz", key, 1);
        Pair("pitch.minPitchHz", "pitch.maxPitchHz", key, 1);
        Pair("eater.mouthOpenThreshold", "eater.maxGain", key, 0.001f);
        Pair("spawner.effectItemSpacing.x", "spawner.effectItemSpacing.y", key);
        Pair("spawner.intervalJitter.x", "spawner.intervalJitter.y", key);
    }

    private static void Pair(string lowKey, string highKey, string changed, float gap = 0)
    {
        if (!settings.TryGetValue(lowKey, out Setting low) || !settings.TryGetValue(highKey, out Setting high) ||
            (!values.ContainsKey(lowKey) && !values.ContainsKey(highKey)) ||
            low.Value + gap <= high.Value)
            return;
        if (changed == highKey) values[lowKey] = low.Normalize(high.Value - gap);
        else values[highKey] = high.Normalize(low.Value + gap);
    }

    internal static void ResetValues(int tab)
    {
        if (tab < 0) values.Clear();
        else foreach (Setting setting in Rows(tab)) values.Remove(setting.Key);
        if (tab < 0 || tab == SystemTab)
        {
            values.Remove("system.mouse");
            MouseDebugInput.SetEnabled(false);
        }
        if (tab < 0 || tab == Mic)
            SelectMicrophone(null, reset: true);
        ConstrainPairs();
        ApplyLive();
        MarkDirty();
    }

    internal static void Tick()
    {
        if (dirty && UnityEngine.Time.unscaledTime >= saveAt) Save();
    }

    private static void MarkDirty()
    {
        dirty = true;
        saveAt = UnityEngine.Time.unscaledTime + 0.4f;
    }

    internal static void Save()
    {
        if (!dirty) return;
        if (Write(SettingsPath, out string error)) dirty = false;
        else saveAt = UnityEngine.Time.unscaledTime + 5;
        SaveError = error;
    }

    internal static bool Export(out string error) => Write(PresetPath, out error);
    internal static bool Import(out string error)
    {
        if (!Read(PresetPath, out error)) return false;
        ConstrainPairs();
        ApplyLive();
        MouseDebugInput.SetEnabled(Enabled("system.mouse"));
        if (microphone == null) SelectMicrophone(null, reset: true);
        else RestoreMicrophone();
        MarkDirty();
        return true;
    }

    private static bool Write(string path, out string error)
    {
        try
        {
            var document = new Document
            {
                version = 1,
                hasMicrophone = microphone != null || PlayerPrefs.HasKey(MicrophoneKey),
                microphone = SelectedMicrophone,
                values = rows.Where(row => values.ContainsKey(row.Key))
                    .Select(row => new Entry { key = row.Key, value = row.Value }).ToList()
            };
            Directory.CreateDirectory(Application.persistentDataPath);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(document, true));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                   ex is ArgumentException || ex is NotSupportedException)
        { error = ex.Message; return false; }
    }

    private static bool Read(string path, out string error)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > 256 * 1024)
            { error = "ファイルが見つからないか、サイズが大きすぎます。"; return false; }
            Document document = JsonUtility.FromJson<Document>(File.ReadAllText(path));
            if (document == null || document.version != 1 || document.values == null)
            { error = "設定ファイルの形式が違います。"; return false; }
            values.Clear();
            foreach (Entry entry in document.values)
                if (entry != null && !string.IsNullOrEmpty(entry.key) && entry.key.Length <= 200 &&
                    !float.IsNaN(entry.value) && !float.IsInfinity(entry.value))
                    values[entry.key] = entry.value;
            microphone = document.hasMicrophone ? document.microphone ?? "" : null;
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                   ex is ArgumentException || ex is NotSupportedException)
        { error = ex.Message; return false; }
    }

    internal static string SelectedMicrophone => microphone ?? PlayerPrefs.GetString(MicrophoneKey, "");
    internal static string MicrophoneForScene(string sceneDefault) =>
        microphone ?? (PlayerPrefs.HasKey(MicrophoneKey) ? SelectedMicrophone : sceneDefault);

    internal static bool SelectMicrophone(string device, bool reset = false)
    {
        MicrophoneAnalysis input = InScene<MicrophoneAnalysis>();
        if (!reset && input != null && !input.SelectMicrophone(device))
            return false;
        microphone = reset ? null : device;
        if (reset) PlayerPrefs.DeleteKey(MicrophoneKey);
        else PlayerPrefs.SetString(MicrophoneKey, device ?? "");
        PlayerPrefs.Save();
        if (reset && input != null) input.SelectMicrophone(input.DefaultDeviceName);
        InScene<PreparationVoiceDetector>()?.RefreshMicrophoneLabel();
        MarkDirty();
        return true;
    }

    internal static void RestoreMicrophone()
    {
        if (microphone != null)
            PlayerPrefs.SetString(MicrophoneKey, microphone);
        MicrophoneAnalysis input = InScene<MicrophoneAnalysis>();
        if (input != null && PlayerPrefs.HasKey(MicrophoneKey) && input.ActiveDeviceName != SelectedMicrophone)
            input.SelectMicrophone(SelectedMicrophone);
        InScene<PreparationVoiceDetector>()?.RefreshMicrophoneLabel();
    }

    internal static T InScene<T>(bool activeOnly = false) where T : Component =>
        InSceneComponents<T>(activeOnly).FirstOrDefault();

    internal static IEnumerable<T> InSceneComponents<T>(bool activeOnly = false) where T : Component =>
        SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(!activeOnly))
            .Where(component => !activeOnly || component is not Behaviour behaviour || behaviour.enabled);

    internal static ItemDefinition Item(ItemDefinition source)
    {
        if (source == null) return null;
#if UNITY_EDITOR
        if (collectingDefaults)
        {
            BindItem(source);
            return source;
        }
#endif
        if (items.TryGetValue(source, out ItemDefinition runtime)) return runtime;
        Initialize();
        runtime = UnityEngine.Object.Instantiate(source);
        runtime.name = source.name;
        runtime.hideFlags = HideFlags.DontSave;
        items[source] = runtime;
        BindItem(runtime);
        return runtime;
    }

    internal static PitchFallSpeedSettings Pitch(PitchFallSpeedSettings source)
    {
        if (source == null) return null;
        if (pitchAssets.TryGetValue(source, out PitchFallSpeedSettings runtime)) return runtime;
        Initialize();
        runtime = UnityEngine.Object.Instantiate(source);
        runtime.hideFlags = HideFlags.DontSave;
        pitchAssets[source] = runtime;
        pitchAssets[runtime] = runtime;
        BindPitch(runtime);
        ConstrainPairs();
        foreach (Setting setting in rows)
            if (ReferenceEquals(setting.Target, runtime)) setting.Apply();
        return runtime;
    }

    internal static void ApplyNextRun()
    {
        ConstrainPairs();
        foreach (Setting setting in rows)
            if (setting.NextRun) setting.Apply();
    }

    internal static void ApplyLive()
    {
        foreach (Setting setting in rows)
            if (!setting.NextRun) setting.Apply();
        if (settings.TryGetValue("microphone.minPitchHz", out Setting pitch) &&
            pitch.Target is MicrophoneAnalysis input && input != null)
            input.RefreshPitchRange();
        RefreshMaster();
        Color background = BannerBackground;
        Color foreground = BannerForeground;
        if (bannerGame != null)
            bannerGame.ApplyAnnouncementColors(background, foreground);
        if (preparationBanner != null)
            FruitCatchUi.ApplyBannerColors(preparationBanner, background, foreground);
        if (preparationBannerText != null)
            preparationBannerText.SetDisplayColor(foreground);
    }

    private static void Option(int tab, string group, string key, string label, float initial,
        float max = 1, bool toggle = false, bool next = false)
    {
        rows.Add(new Setting { Tab = tab, Group = group, Key = key, Label = label, Max = max,
            Initial = initial, Switch = toggle, NextRun = next });
        settings.Add(key, rows[rows.Count - 1]);
    }

    private static void Field(object target, int tab, string group, string prefix, string name, string label,
        float min = 0, float max = 1, bool next = false, int part = -1, string clip = null)
    {
        if (target == null) return;
        string key = prefix + "." + name + (part < 0 ? "" : part == 0 ? ".x" : ".y");
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null) return;
        object raw = field.GetValue(target);
        float initial = part >= 0 ? ((Vector2)raw)[part] : Convert.ToSingle(raw);
        if (!settings.TryGetValue(key, out Setting row))
        {
            row = new Setting { Tab = tab, Group = group, Key = key, Label = label, Min = min, Max = max,
                Switch = raw is bool, Integer = raw is int, NextRun = next, Part = part, ClipField = clip };
            settings.Add(key, row);
            rows.Add(row);
        }
        if (!ReferenceEquals(row.Target, target)) row.Initial = initial;
        row.Field = field;
        row.Target = target;
        if (target is FaceMovementController tracking)
        {
            row.TrackingTargets ??= new List<FaceMovementController>();
            row.TrackingTargets.RemoveAll(item => item == null);
            if (!row.TrackingTargets.Contains(tracking)) row.TrackingTargets.Add(tracking);
        }
    }

    private static void AddOptions()
    {
        Option(Audio, "全体", "audio.master", "マスター音量", 1);
        Option(Audio, "全体", "audio.bgm", "BGM全体", 1);
        Option(Audio, "全体", "audio.se", "SE全体", 1);
        Option(Visual, "画面演出", "visual.rainbow", "フィーバーの虹", 1, toggle: true);
        Option(Visual, "画面演出", "visual.ripples", "口元から出る波紋", 1, toggle: true);
        Option(Visual, "画面演出", "visual.rotation", "落下中のアイテムの回転", 1, toggle: true);
        Option(Visual, "画面演出", "visual.rare", "レアフルーツのきらめき", 1, toggle: true);
        AddColorOption(BannerBackgroundKey, "背景色", FruitCatchUi.Cream);
        AddColorOption(BannerForegroundKey, "文字・枠の色", FruitCatchUi.DeepGreen);
        Option(SystemTab, "デバッグ", "system.mouse", "マウスデバッグ", 0, toggle: true);
        Option(TimeTab, "BGM連動", "time.feverSync", "フィーバーをBGMの長さに合わせる", 1, toggle: true, next: true);
        Option(TimeTab, "BGM連動", "time.resultsSync", "結果発表をBGMの長さに合わせる", 1, toggle: true, next: true);
        Option(TimeTab, "BGM連動", "time.feverDuration", "フィーバーの固定時間（秒）", .1f, 120, next: true);
        settings["time.feverDuration"].Min = .1f;
        Option(TimeTab, "顔登録", "time.registrationHold", "顔を保持する時間（秒、0＝既定）", 0, 15);
    }

    private static void AddColorOption(string key, string label, Color32 initial)
    {
        Option(Visual, BannerColorGroup, key, label, PackColor(initial), 0xFFFFFF);
        settings[key].IsColor = true;
        settings[key].Integer = true;
    }

    internal static void BindScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        bannerGame = null;
        preparationBanner = null;
        preparationBannerText = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
                BindComponent(component);
        ConstrainPairs();
        ApplyLive();
        RestoreMicrophone();
    }

    private static void BindComponent(MonoBehaviour component)
    {
        void Add(int tab, string group, string prefix, string field, string label, float min = 0,
            float max = 1, bool next = false, int part = -1) =>
            Field(component, tab, group, prefix, field, label, min, max, next, part);
        bool main = component != null && component.gameObject.scene.name == "MainGame";
        switch (component)
        {
            case PitchFallSpeedController pitch:
                if (main)
                {
                    Add(Balance, "出現・落下", "pitchController", "maxFallSpeed", "速度制御後の落下速度上限", 0, 40, true);
                    Add(Mic, "落下速度とピッチの対応", "pitchController", "applyToFallingObjects", "本編の落下速度にピッチを適用");
                }
#if UNITY_EDITOR
                if (collectingDefaults)
                {
                    if (pitch.OperatorPitchSettings != null) BindPitch(pitch.OperatorPitchSettings);
                    break;
                }
#endif
                _ = Pitch(pitch.OperatorPitchSettings);
                break;
            case FruitCatchGameSession session:
                bannerGame = session;
                Add(TimeTab, "本編", "game", "gameDuration", "制限時間（秒）", 1, 300, true);
                Add(TimeTab, "本編", "game", "startDisplayDuration", "開始演出（秒）", .1f, 15, true);
                Add(TimeTab, "本編", "game", "finishDisplayDuration", "終了演出（秒）", .1f, 15, true);
                Add(TimeTab, "本編", "game", "warningStartSeconds", "警告を始める残り時間（秒）", 0, 300, true);
                Add(TimeTab, "本編", "game", "resultDisplayDuration", "結果発表の固定時間（秒）", .1f, 120, true);
                break;
            case FallingItemSpawner spawner:
                Add(Balance, "出現・落下", "spawner", "minFallSpeed", "落下速度の最小値", .01f, 30, true);
                Add(Balance, "出現・落下", "spawner", "maxFallSpeed", "落下速度の最大値", .01f, 30, true);
                Add(Balance, "出現・落下", "spawner", "maxActiveItems", "同時に出せる最大個数", 1, 150, true);
                Add(Balance, "出現・落下", "spawner", "feverFruitsPerSecond", "フィーバー中の出現数／秒", .01f, 40, true);
                Add(Balance, "出現・落下", "spawner", "specialItemSpacing", "特殊アイテムの最小間隔（秒）", 0, 30, true);
                Add(Balance, "出現・落下", "spawner", "effectItemSpacing", "効果アイテム間隔の最小値（秒）", 0, 30, true, 0);
                Add(Balance, "出現・落下", "spawner", "effectItemSpacing", "効果アイテム間隔の最大値（秒）", 0, 30, true, 1);
                Add(Balance, "出現・落下", "spawner", "intervalJitter", "出現間隔の最小倍率", .05f, 5, true, 0);
                Add(Balance, "出現・落下", "spawner", "intervalJitter", "出現間隔の最大倍率", .05f, 5, true, 1);
                Add(TimeTab, "フィーバーアイテム", "spawner", "firstFeverWindow", "最も早い出現時刻（秒）", 0, 300, true, 0);
                Add(TimeTab, "フィーバーアイテム", "spawner", "firstFeverWindow", "最も遅い出現時刻（秒）", 0, 300, true, 1);
                for (int i = 0; i < spawner.OperatorPhases.Length; i++)
                {
                    SpawnPhaseSettings phase = spawner.OperatorPhases[i];
                    string group = $"フェーズ {i + 1}　{phase.label}", prefix = $"phase.{i}";
                    Field(phase, Balance, group, prefix, "endTime", "フェーズ終了時刻（秒）", 0, 300, true);
                    Field(phase, Balance, group, prefix, "fruitsPerSecond", "フルーツ出現数／秒", .01f, 40, true);
                    Field(phase, Balance, group, prefix, "bugSlots", "虫の個数", 0, 40, true);
                    Field(phase, Balance, group, prefix, "bananaSlots", "バナナの個数", 0, 40, true);
                    Field(phase, Balance, group, prefix, "chiliSlots", "唐辛子の個数", 0, 40, true);
                }
                spawner.BindOperatorItems();
                break;
            case CharacterPairController when main:
                Add(Balance, "キャラクター", "character", "moveSpeed", "移動速度", 0, 40, true);
                Add(Balance, "キャラクター", "character", "followSmoothTime", "追従の滑らかさ（秒）", .01f, 3, true);
                Add(Balance, "キャラクター", "character", "feverSpeedMultiplier", "フィーバー時の移動倍率", 1, 5, true);
                Add(Balance, "唐辛子・バナナ", "character", "chiliDuration", "唐辛子の効果時間（秒）", .1f, 30, true);
                Add(Balance, "唐辛子・バナナ", "character", "chiliSpeedMultiplier", "唐辛子の移動倍率", 1, 5, true);
                Add(Balance, "唐辛子・バナナ", "character", "chiliSwayAmplitude", "唐辛子の揺れ幅", 0, 4, true);
                Add(Balance, "唐辛子・バナナ", "character", "chiliSwayFrequency", "唐辛子の揺れ頻度（Hz）", 0, 15, true);
                Add(Balance, "唐辛子・バナナ", "character", "slideSpeed", "バナナの滑走速度", .1f, 45, true);
                break;
            case EaterCatchController:
                Add(Balance, "キャラクター", "eater", "maxScale", "最大開口の横幅倍率", 1, 4, true);
                Add(Balance, "キャラクター", "eater", "feverScaleMultiplier", "フィーバー時の拡大倍率", 1, 4, true);
                Add(Balance, "キャラクター", "eater", "bounceSpeed", "跳ね返り速度", .1f, 25, true);
                Add(Mic, "開口判定", "eater", "mouthOpenThreshold", "本編の開口しきい値", 0, .999f);
                Add(Mic, "開口判定", "eater", "maxGain", "最大開口に達するGain", .001f);
                break;
            case ItemEffectController:
                Add(Balance, "唐辛子・バナナ", "effects", "burstFruitCount", "壁衝突後のフルーツ追加数", 0, 50, true);
                break;
            case MicrophoneAnalysis:
                Add(Mic, "ピッチ検出", "microphone", "minPitchHz", "最低周波数（Hz）", 40, 1499);
                Add(Mic, "ピッチ検出", "microphone", "maxPitchHz", "最高周波数（Hz）", 100, 5000);
                Add(Mic, "ピッチ検出", "microphone", "pitchNoiseGate", "ノイズゲート", 0, .1f);
                Add(Mic, "ピッチ検出", "microphone", "yinThreshold", "YIN Threshold", .05f, .3f);
                break;
            case PreparationVoiceDetector:
                Add(Mic, "開口判定", "voice", "gainThreshold", "発声練習のしきい値", .001f);
                Add(Mic, "開口判定", "voice", "practiceMouthThreshold", "フルーツ練習の開口しきい値");
                Add(TimeTab, "準備・練習", "voice", "recognitionSeconds", "発声成功後の表示時間（秒）", .1f, 30);
                Add(Audio, "BGMフェード", "voice", "voiceBgmFadeSeconds", "発声練習BGMのフェード（秒）", 0, 10);
                break;
            case PreparationPractice:
                Add(TimeTab, "準備・練習", "practice", "successSeconds", "練習成功後の待機（秒）", .1f, 15);
                break;
            case PreparationFacePresentation:
                Add(TimeTab, "準備・練習", "face", "morphDuration", "変身演出（秒）", .1f, 30);
                Add(TimeTab, "準備・練習", "face", "carrierHoldSeconds", "変身後の待機（秒）", 0, 30);
                break;
            case PreparationFlowController flow:
                Transform startBanner = flow.OperatorSteps.FirstOrDefault(step =>
                    step.RequiredSignal == PreparationSignal.HandsRaised)?.Panel?.transform.Find("HandsStartBanner");
                if (startBanner != null)
                {
                    preparationBanner = startBanner.GetComponent<RoundedDashedPanelGraphic>();
                    preparationBannerText = startBanner.GetComponentInChildren<FuriganaText>(true);
                }
                Add(TimeTab, "準備・練習", "prep", "fadeDuration", "画面切替のフェード（秒）", 0, 10);
                Add(TimeTab, "準備・練習", "prep", "countdownInterval", "カウントダウン間隔（秒）", .05f, 10);
                Add(SystemTab, "デバッグ", "prep", "showDebugOverlay", "既存のデバッグオーバーレイ");
                for (int i = 0; i < flow.OperatorSteps.Count; i++)
                {
                    PreparationStep step = flow.OperatorSteps[i];
                    string label = step.RequiredSignal == PreparationSignal.FaceAligned ? "登録完了後に進むまで"
                        : step.RequiredSignal == PreparationSignal.VoiceDetected ? "発声練習の必要時間" : step.DisplayName;
                    Field(step, TimeTab, "説明ステップ", $"step.{i}", "requiredHoldSeconds", label + "（秒）", 0, 120);
                }
                break;
            case FaceMovementController:
                Add(SystemTab, "人物追跡", "tracking", "inputMinX", "カメラ入力の左端", 0, .99f);
                Add(SystemTab, "人物追跡", "tracking", "inputMaxX", "カメラ入力の右端", .01f, 1);
                Add(SystemTab, "人物追跡", "tracking", "trackingTimeout", "追跡タイムアウト（秒）", 0, 10);
                break;
            case VoiceRippleBackground:
                Add(Visual, "画面演出", "wind", "airflowEnabled", "背景の気流");
                Add(Visual, "画面演出", "wind", "ripplePitchTintEnabled", "波紋のピッチ連動色");
                break;
            case CatchEffectPlayer:
                Add(Visual, "画面演出", "catch", "eatingEffectsEnabled", "捕食エフェクト");
                break;
            case ScoreHud:
                Add(Visual, "画面演出", "score", "scoreAnimationEnabled", "スコアアニメーション");
                break;
            case ScorePopup:
                Add(Visual, "画面演出", "score", "popupEnabled", "口元の得点ポップアップ");
                break;
            case FruitResultPresentation:
                Add(Visual, "画面演出", "results", "animationEnabled", "結果発表アニメーション");
                break;
        }
    }

    private static void BindItem(ItemDefinition item)
    {
        string prefix = "item." + (int)item.Kind + "." + item.ItemId;
        string group = item.Kind switch
        {
            ItemKind.Bug => "虫",
            ItemKind.Banana => "バナナ",
            ItemKind.Chili => "唐辛子",
            ItemKind.FeverFruit => "フィーバーアイテム",
            _ => item.DisplayName
        };
        Field(item, Balance, group, prefix, "scoreDelta", "取得時の加減点", -1000, 1000, true);
        if (item.Kind != ItemKind.Fruit) return;
        Field(item, Balance, group, prefix, "normalSpawnWeight", "通常時の出現重み", 0, 1000, true);
        Field(item, Balance, group, prefix, "feverSpawnWeight", "フィーバー時の出現重み", 0, 1000, true);
        Field(item, Visual, "フルーツ別の演出", prefix, "rareVisual", group + "のきらめき");
    }

    private static void BindPitch(PitchFallSpeedSettings pitch)
    {
        const string group = "落下速度とピッチの対応";
        Field(pitch, Mic, group, "pitch", "minPitchHz", "最低周波数（Hz）", 40, 1499);
        Field(pitch, Mic, group, "pitch", "maxPitchHz", "最高周波数（Hz）", 100, 5000);
        Field(pitch, Mic, group, "pitch", "pitchHoldTime", "ピッチ保持時間（秒）", 0, 3);
        Field(pitch, Mic, group, "pitch", "smoothTime", "ピッチ平滑化時間（秒）", 0, 3);
        Field(pitch, Mic, group, "pitch", "minSpeedMultiplier", "落下速度の最小倍率", 0, 1);
        Field(pitch, Mic, group, "pitch", "highPitchFaster", "高い声ほど落下を速くする");
    }

    internal static void BindAudio(FruitCatchAudioSettings audio)
    {
        Initialize();
        Setting feverDuration = settings["time.feverDuration"];
        feverDuration.Initial = audio != null && audio.FeverBgm != null
            ? feverDuration.Normalize(audio.FeverBgm.length)
            : feverDuration.Normalize(Get("game.gameDuration") - Get("spawner.firstFeverWindow.x"));
        void Cue(string name, string label, bool bgm = false, string volume = null) =>
            Field(audio, Audio, bgm ? "BGM" : "効果音", "audio", volume ?? name + "Volume",
                label, clip: name);
        Cue("mainMenuBgm", "準備画面BGM", true);
        Cue("gameBgm", "ゲームBGM", true);
        Cue("feverBgm", "フィーバーBGM", true);
        Cue("resultsBgm", "結果発表BGM", true);
        Cue("transformationSound", "変身");
        Cue("transformationCompleteSound", "変身完了");
        Cue("movementPracticeSuccess", "移動練習成功", volume: "practiceSuccessVolume");
        if (audio != null)
        {
            Option(Audio, "効果音", "audio.fruitPracticeSuccessVolume", "フルーツ練習成功",
                audio.PracticeSuccessVolume);
            Setting fruitPractice = settings["audio.fruitPracticeSuccessVolume"];
            fruitPractice.ClipField = "fruitPracticeSuccess";
            fruitPractice.Target = audio;
        }
        Cue("countdownSound", "カウントダウン");
        Cue("startSound", "スタート");
        Cue("finishSound", "ゲーム終了");
        Cue("fruitEatSound", "フルーツ取得");
        Cue("fruitBounceSound", "跳ね返り");
        Cue("wallImpactSound", "壁への衝突");
        Cue("chiliSwaySound", "唐辛子の揺れ");
        Cue("bananaSlideSound", "バナナで滑走");
        Cue("bugEatSound", "虫の取得");
        Field(audio, Audio, "効果音", "audio", "chiliSwaySoundPitch", "唐辛子SEの再生速度", .1f, 3);
        Field(audio, Audio, "BGMフェード", "audio", "gameBgmFadeOutDuration", "ゲームBGMのフェードアウト（秒）", 0, 10);
        Field(audio, Audio, "BGMフェード", "audio", "feverBgmFadeOutDuration", "フィーバーBGM終端のフェード（秒）", 0, 10);
        Field(audio, Audio, "BGMフェード", "audio", "feverBgmInterruptedFadeOutDuration", "フィーバー中断時のフェード（秒）", 0, 10);
        Field(audio, Audio, "BGMフェード", "audio", "gameBgmResumeFadeInDuration", "ゲームBGM復帰時のフェード（秒）", 0, 10);
        Field(audio, Mic, "声のモニタリング", "audio", "voiceFeedbackEnabled", "声をスピーカーから再生する");
        Field(audio, Audio, "全体", "audio", "voiceFeedbackVolume", "マイクモニタリング音量");
        Field(audio, Mic, "声のモニタリング", "audio", "voiceFeedbackDry", "原音（Dry）");
        Field(audio, Mic, "声のモニタリング", "audio", "voiceFeedbackWet", "残響（Wet）");
        Field(audio, Mic, "声のモニタリング", "audio", "voiceFeedbackReverbTime", "残響時間（秒）", .1f, 2);
        ApplyLive();
    }

#if UNITY_EDITOR
    // The catalog makes settings from the other scene editable before it has been loaded.
    // Live fields still take their initial values from their actual scene or asset.
    [UnityEditor.MenuItem("FruitCatch/運営パネルの初期値カタログを更新")]
    private static void UpdateDefaultsCatalog()
    {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
        const string path = "Assets/Resources/FruitCatchOperatorDefaults.json";
        Reset();
        collectingDefaults = true;
        try
        {
            foreach (var entry in UnityEditor.EditorBuildSettings.scenes)
            {
                if (!entry.enabled) continue;
                Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene(entry.path);
                try
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                        foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
                            BindComponent(component);
                }
                finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
            }
            File.WriteAllText(path, JsonUtility.ToJson(new Defaults { rows = rows }, true) + "\n");
            UnityEditor.AssetDatabase.ImportAsset(path);
        }
        finally
        {
            collectingDefaults = false;
            Reset();
        }
    }
#endif
}
