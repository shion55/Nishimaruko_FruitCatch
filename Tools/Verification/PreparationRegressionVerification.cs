#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FruitCatch.Preparation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Copy into the Editor folder of an isolated verification project.
public static class PreparationRegressionVerification
{
    private const string Running = "FruitCatch.PreparationRegression";
    private static readonly List<string> errors = new List<string>();

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                RunChecks();
        };
    }

    public static void RunBatch()
    {
        PlayerSettings.companyName = "FruitCatchVerification";
        PlayerSettings.productName = "PreparationRegression";
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Font/MochiyPopPOne-Regular SDF.asset");
        var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(1920, 1080);
        var camera = Rect("Camera", canvas.transform).gameObject.AddComponent<RawImage>();
        var face = Rect("Face", canvas.transform).gameObject;
        var voice = Rect("Voice", canvas.transform).gameObject;
        var done = Rect("Done", canvas.transform).gameObject;
        var backdrop = Rect("ReadingBackdrop", face.transform).gameObject.AddComponent<Image>();
        backdrop.color = new Color(1, 1, 1, 0.82f);
        var area = Rect("Area", face.transform);
        area.anchorMin = new Vector2(0.16f, 0.16f);
        area.anchorMax = new Vector2(0.38f, 0.68f);
        area.gameObject.AddComponent<Image>();
        Rect("Label", area).gameObject.AddComponent<TextMeshProUGUI>().font = font;
        var presentation = area.gameObject.AddComponent<PreparationFacePresentation>();
        Set(presentation, "cameraImage", camera);
        Set(presentation, "readingBackdrop", backdrop);
        Set(presentation, "carrierSprite", SpriteAt("Assets/Image/honbansozai/robo-wait.png"));
        Set(presentation, "morphDuration", 0.8f);
        Set(presentation, "carrierHoldSeconds", 0.2f);
        var eater = Rect("Eater", voice.transform).gameObject.AddComponent<Image>();
        eater.sprite = SpriteAt("Assets/Image/honbansozai/mochi-shomen.png");
        Rect("VoiceLabel", voice.transform).gameObject.AddComponent<TextMeshProUGUI>().font = font;
        var root = new GameObject("Flow");
        var flow = root.AddComponent<PreparationFlowController>();
        var mic = root.AddComponent<MicrophoneAnalysis>();
        mic.enabled = false; // Tests inject samples; never open a physical microphone.
        var detector = root.AddComponent<PreparationVoiceDetector>();
        Set(detector, "flow", flow);
        Set(detector, "microphone", mic);
        Set(detector, "eaterImage", eater);
        Set(detector, "mouthOpenSprite", SpriteAt("Assets/Image/honbansozai/mochi-shomenopen.png"));
        var steps = new List<PreparationStep>();
        foreach (var pair in new[] { (face, PreparationSignal.FaceAligned), (voice, PreparationSignal.VoiceDetected), (done, PreparationSignal.Manual) })
        {
            var step = new PreparationStep();
            Set(step, "panel", pair.Item1);
            Set(step, "requiredSignal", pair.Item2);
            Set(step, "requiredHoldSeconds", pair.Item2 == PreparationSignal.VoiceDetected ? 1f : 0.01f);
            steps.Add(step);
        }
        Set(flow, "steps", steps);
        face.SetActive(false);
        voice.SetActive(false);
        done.SetActive(false);
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Regression.unity");
        SessionState.SetBool(Running, true);
        EditorApplication.EnterPlaymode();
    }

    private static async void RunChecks()
    {
        Application.logMessageReceived += CollectErrors;
        try
        {
            await Task.Delay(200);
            var flow = UnityEngine.Object.FindFirstObjectByType<PreparationFlowController>();
            var presentation = flow.CurrentPanel.GetComponentInChildren<PreparationFacePresentation>();
            var camera = GameObject.Find("Camera").GetComponent<RawImage>();
            Vector3 cameraPosition = camera.transform.position;
            Rect cameraUv = camera.uvRect;
            var frame = new Texture2D(64, 64);
            frame.SetPixels(Enumerable.Repeat(Color.magenta, 4096).ToArray());
            frame.Apply();
            presentation.Capture(frame);
            flow.ReportFaceAligned(true);
            await Task.Delay(180);
            var cover = GameObject.Find("OvalCameraCutout").GetComponent<PreparationOvalGraphic>();
            Check(cover.solidRectangle && cover.color.a == 1f, "Camera cutout did not close opaquely during transformation.");
            Check(GameObject.Find("TransformationHalo") != null, "Transformation halo missing.");
            Check(flow.IsTransitioning && flow.CurrentStepIndex == 0, "Flow skipped the transformation.");
            Canvas.ForceUpdateCanvases();
            foreach (var graphic in flow.CurrentPanel.GetComponentsInChildren<Graphic>(true))
                Check(graphic.canvasRenderer != null, "Missing CanvasRenderer: " + graphic.name);
            await Task.Delay(1000);
            Check(flow.CurrentStepIndex == 1, "Transformation did not advance to voice step.");
            Check(camera.transform.position == cameraPosition && camera.uvRect == cameraUv, "Camera presentation changed.");

            var detector = flow.GetComponent<PreparationVoiceDetector>();
            var mic = flow.GetComponent<MicrophoneAnalysis>();
            var eater = GameObject.Find("Eater").GetComponent<Image>();
            Sprite closed = eater.sprite;
            // A quiet input below 0.015 must not move the flow.
            var clip = AudioClip.Create("Synthetic microphone", 44100, 1, 44100, false);
            Set(mic, "microphoneClip", clip);
            SetGain(mic, 0.005f);
            await Task.Delay(150);
            Check(!detector.IsVoiceDetected && flow.CurrentStepIndex == 1, "Silence falsely advanced voice step.");
            SetGain(mic, 0.025f); // Previously rejected by the old 0.1 threshold.
            await Task.Delay(250);
            Check(detector.IsVoiceDetected && eater.sprite != closed, "Eater did not open its mouth for a normal input.");
            Check(GameObject.Find("Fill").GetComponent<RectTransform>().anchorMax.x > 0f, "Voice meter did not react.");
            SetGain(mic, 0f);
            await Task.Delay(70);
            Check(detector.IsVoiceDetected, "Short breath gap reset detection.");
            SetGain(mic, 0.025f);
            await Task.Delay(1100);
            Check(flow.CurrentStepIndex == 2, "Voice input did not advance to the next step.");

            flow.GoBackForDebug();
            await Task.Delay(100);
            SetGain(mic, 0f);
            await Task.Delay(350);
            Check(!detector.IsVoiceDetected, "Sustained silence did not close the mouth.");
            Set(mic, "microphoneClip", null);
            await Task.Delay(100);
            Check(!detector.IsVoiceDetected && flow.CurrentStepIndex == 1, "Missing mic falsely advanced.");
            flow.GoBackForDebug();
            await Task.Delay(100);
            cover = GameObject.Find("OvalCameraCutout").GetComponent<PreparationOvalGraphic>();
            Check(!cover.solidRectangle && cover.cutout != null, "Returning to face step did not restore the guide.");
            if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
            SessionState.SetBool(Running, false);
            Debug.Log("PREPARATION_REGRESSION_PASS: opaque camera cover, burst, renderer lifecycle, camera invariance, mouth, meter, voice progress, breath gap, silence, no mic, re-entry.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            SessionState.SetBool(Running, false);
            Debug.LogError("PREPARATION_REGRESSION_FAIL: " + exception);
            EditorApplication.Exit(1);
        }
    }

    private static void CollectErrors(string message, string stack, LogType type)
    {
        // Unity's first-run Search indexing can throw before a default search
        // database exists in this temporary project. Keep it visible in the log,
        // but don't confuse this Editor-only startup issue with a runtime UI error.
        if (type == LogType.Exception && stack.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup"))
            return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message + "\n" + stack);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void SetGain(MicrophoneAnalysis mic, float gain) =>
        typeof(MicrophoneAnalysis).GetProperty("Gain").GetSetMethod(true).Invoke(mic, new object[] { gain });
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static Sprite SpriteAt(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().First();
    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        return rect;
    }
}
#endif
