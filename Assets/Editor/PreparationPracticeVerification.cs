#if UNITY_EDITOR
using System;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using FruitCatch.Preparation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PreparationPracticeVerification
{
    private const string Running = "FruitCatch.PreparationPracticeVerification";
    private static int phase;
    private static double deadline;
    private static UdpClient sender;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Preparation.unity");
        DisableExternalInput();
        SessionState.SetBool(Running, true);
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void Attach()
    {
        if (!SessionState.GetBool(Running, false)) return;
        phase = 0;
        deadline = EditorApplication.timeSinceStartup + 100;
        SceneManager.sceneLoaded += SceneLoaded;
        EditorApplication.update += Tick;
    }

    private static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MainGame")
        {
            var session = UnityEngine.Object.FindAnyObjectByType<FruitCatchGameSession>();
            var scores = UnityEngine.Object.FindAnyObjectByType<GameManager>();
            Require(scores.Score == 0 && scores.FruitCount == 0 && scores.BugCount == 0);
            // Let the real results transition run without saving a test ranking entry.
            Set(session, "gameManager", null);
            Set(session, "gameDuration", 0.1f);
            Set(session, "resultDisplayDuration", 0.1f);
        }
        else if (scene.name == "Preparation") DisableExternalInput();
    }

    private static void DisableExternalInput()
    {
        var flow = UnityEngine.Object.FindAnyObjectByType<PreparationFlowController>();
        flow.GetComponent<PreparationPythonBridge>().enabled = false;
        flow.GetComponent<PreparationVoiceDetector>().enabled = false;
        flow.GetComponent<MicrophoneAnalysis>().enabled = false;
        Set(flow.GetComponent<FruitCatchTrackerProcess>(), "startAutomatically", false);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException($"Phase {phase}");
            var flow = UnityEngine.Object.FindAnyObjectByType<PreparationFlowController>();
            if (phase == 8)
            {
                if (SceneManager.GetActiveScene().name == "MainGame")
                {
                    Debug.Log("Preparation verification: MainGame entered.");
                    phase++;
                }
                return;
            }
            if (flow == null || flow.IsTransitioning) return;
            var practice = flow.CurrentPanel.GetComponent<PreparationPractice>();
            switch (phase)
            {
                case 0:
                    Require(flow.CurrentRequiredSignal == PreparationSignal.HandsRaised);
                    flow.AdvanceForDebug();
                    phase++;
                    break;
                case 1:
                    flow.GoBackForDebug();
                    phase++;
                    break;
                case 2:
                    Require(flow.CurrentStepIndex == 0);
                    flow.RestartForDebug();
                    flow.AdvanceForDebug();
                    phase++;
                    break;
                case 3:
                    flow.AdvanceForDebug();
                    phase++;
                    break;
                case 4:
                    Require(flow.CurrentRequiredSignal == PreparationSignal.FaceAligned);
                    var image = new Texture2D(128, 72);
                    var face = flow.CurrentPanel.GetComponentInChildren<PreparationFacePresentation>();
                    face.Capture(image, new Rect(.1f, .1f, .5f, .6f));
                    UnityEngine.Object.Destroy(image);
                    flow.AdvanceForDebug();
                    phase++;
                    break;
                case 5:
                    if (flow.CurrentRequiredSignal == PreparationSignal.VoiceDetected)
                    {
                        Debug.Log("Preparation verification: transformation and both movement targets completed.");
                        flow.ReportVoiceDetected(true);
                        phase++;
                    }
                    else
                    {
                        Require(practice != null);
                        Send((bool)Field(practice, "leftReached").GetValue(practice) ? "0.68" : "0.32");
                    }
                    break;
                case 6:
                    if (flow.CurrentRequiredSignal == PreparationSignal.VoiceDetected)
                        flow.ReportVoiceDetected(true);
                    else if (flow.CurrentRequiredSignal == PreparationSignal.FruitPractice)
                    {
                        int attempt = (int)Field(practice, "attempt").GetValue(practice);
                        Send(attempt == 0 ? "0.50" : "0.36");
                        if (attempt > 0)
                            Set(flow.GetComponent<PreparationVoiceDetector>(), "detected", true);
                    }
                    else
                    {
                        Require(flow.CurrentStepIndex == 6);
                        Debug.Log("Preparation verification: voice hold, missed fruit, retry and catch completed.");
                        phase++;
                    }
                    break;
                case 7:
                    if (flow.CurrentStepIndex < 7) flow.AdvanceForDebug();
                    else phase++;
                    break;
                case 9:
                    Require(flow.CurrentStepIndex == 0 && flow.CurrentRequiredSignal == PreparationSignal.HandsRaised);
                    Debug.Log("Preparation verification: returned to title for another round.");
                    Finish(0);
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void Send(string x)
    {
        sender ??= new UdpClient();
        var bytes = Encoding.ASCII.GetBytes(x);
        sender.Send(bytes, bytes.Length, "127.0.0.1", 5015);
    }

    private static FieldInfo Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException($"Unexpected tutorial state during phase {phase}.");
    }

    private static void Finish(int code)
    {
        sender?.Dispose();
        SessionState.SetBool(Running, false);
        EditorApplication.update -= Tick;
        SceneManager.sceneLoaded -= SceneLoaded;
        EditorApplication.Exit(code);
    }
}
#endif
