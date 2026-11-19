using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FruitCatch.Preparation
{
    public enum PreparationSignal
    {
        Manual,
        HandsRaised,
        FaceAligned,
        VoiceDetected,
        Timed
    }

    [Serializable]
    public sealed class PreparationStep
    {
        [SerializeField] private string displayName = "説明";
        [SerializeField] private GameObject panel;
        [SerializeField] private PreparationSignal requiredSignal = PreparationSignal.Manual;
        [SerializeField, Min(0f)] private float requiredHoldSeconds = 0.8f;

        public string DisplayName => displayName;
        public GameObject Panel => panel;
        public PreparationSignal RequiredSignal => requiredSignal;
        public float RequiredHoldSeconds => requiredHoldSeconds;
    }

    /// <summary>
    /// Preparationシーンの進行だけを管理します。
    /// 顔・手・声の検知器はReport～メソッドを呼び出すだけで接続できます。
    /// </summary>
    public sealed class PreparationFlowController : MonoBehaviour
    {
        [Header("画面構成")]
        [SerializeField] private List<PreparationStep> steps = new List<PreparationStep>();
        [SerializeField] private CanvasGroup fadeOverlay;
        [SerializeField, Min(0f)] private float fadeDuration = 0.35f;

        [Header("ゲーム開始")]
        [SerializeField] private string gameplaySceneName = "MainGame";
        [SerializeField] private TMP_Text countdownText;
        [SerializeField] private string[] countdownLabels = { "3", "2", "1" };
        [SerializeField, Min(0.05f)] private float countdownInterval = 1f;

        [Header("開発・デバッグ")]
        [SerializeField] private bool developmentMode = true;
        [SerializeField] private bool showDebugOverlay = true;
        [SerializeField] private GameObject debugOverlayRoot;
        [SerializeField] private TMP_Text debugText;
        [SerializeField] private PreparationCameraSurface cameraSurface;

        private int currentStepIndex;
        private float signalHoldTime;
        private bool handsRaised;
        private bool faceAligned;
        private bool voiceDetected;
        private bool transitioning;
        private bool simulatedCameraAvailable;

        public int CurrentStepIndex => currentStepIndex;
        public bool DevelopmentMode => developmentMode;
        public PreparationSignal CurrentRequiredSignal =>
            steps.Count > 0 && currentStepIndex < steps.Count
                ? steps[currentStepIndex].RequiredSignal
                : PreparationSignal.Manual;

        private void Start()
        {
            currentStepIndex = 0;
            ShowOnlyCurrentPanel();

            if (fadeOverlay != null)
            {
                fadeOverlay.alpha = 0f;
                fadeOverlay.blocksRaycasts = false;
            }

            if (countdownText != null)
                countdownText.text = string.Empty;

            SetSimulatedCameraAvailable(false);
            UpdateDebugOverlay();
        }

        private void Update()
        {
            HandleDevelopmentInput();
            UpdateSignalProgress();
            UpdateDebugOverlay();
        }

        public void ReportHandsRaised(bool detected) => handsRaised = detected;
        public void ReportFaceAligned(bool detected) => faceAligned = detected;
        public void ReportVoiceDetected(bool detected) => voiceDetected = detected;

        public void AdvanceForDebug()
        {
            if (!developmentMode || transitioning)
                return;

            StartCoroutine(AdvanceRoutine());
        }

        public void GoBackForDebug()
        {
            if (!developmentMode || transitioning || currentStepIndex <= 0)
                return;

            StartCoroutine(ChangeStepRoutine(currentStepIndex - 1));
        }

        public void RestartForDebug()
        {
            if (!developmentMode || transitioning)
                return;

            StopAllCoroutines();
            transitioning = false;
            currentStepIndex = 0;
            signalHoldTime = 0f;
            ShowOnlyCurrentPanel();

            if (countdownText != null)
                countdownText.text = string.Empty;

            if (fadeOverlay != null)
            {
                fadeOverlay.alpha = 0f;
                fadeOverlay.blocksRaycasts = false;
            }
        }

        private void HandleDevelopmentInput()
        {
            if (!developmentMode || Keyboard.current == null)
                return;

            if (Keyboard.current.spaceKey.wasPressedThisFrame)
                AdvanceForDebug();

            if (Keyboard.current.backspaceKey.wasPressedThisFrame)
                GoBackForDebug();

            if (Keyboard.current.rKey.wasPressedThisFrame)
                RestartForDebug();

            if (Keyboard.current.cKey.wasPressedThisFrame)
                SetSimulatedCameraAvailable(!simulatedCameraAvailable);
        }

        private void UpdateSignalProgress()
        {
            if (transitioning || steps.Count == 0 || currentStepIndex >= steps.Count)
                return;

            PreparationStep step = steps[currentStepIndex];
            bool active = IsSignalActive(step.RequiredSignal);

            if (!active)
            {
                signalHoldTime = 0f;
                return;
            }

            signalHoldTime += Time.deltaTime;
            if (signalHoldTime >= step.RequiredHoldSeconds)
                StartCoroutine(AdvanceRoutine());
        }

        private bool IsSignalActive(PreparationSignal signal)
        {
            switch (signal)
            {
                case PreparationSignal.HandsRaised:
                    return handsRaised;
                case PreparationSignal.FaceAligned:
                    return faceAligned;
                case PreparationSignal.VoiceDetected:
                    return voiceDetected;
                case PreparationSignal.Timed:
                    return true;
                default:
                    return false;
            }
        }

        private IEnumerator AdvanceRoutine()
        {
            if (transitioning || steps.Count == 0)
                yield break;

            signalHoldTime = 0f;

            if (currentStepIndex >= steps.Count - 1)
            {
                yield return CountdownAndLoadRoutine();
                yield break;
            }

            yield return ChangeStepRoutine(currentStepIndex + 1);
        }

        private IEnumerator ChangeStepRoutine(int nextIndex)
        {
            transitioning = true;

            yield return FadeTo(1f);
            currentStepIndex = Mathf.Clamp(nextIndex, 0, Mathf.Max(0, steps.Count - 1));
            signalHoldTime = 0f;
            ClearDetectionSignals();
            ShowOnlyCurrentPanel();
            yield return FadeTo(0f);

            transitioning = false;
        }

        private IEnumerator CountdownAndLoadRoutine()
        {
            transitioning = true;

            if (countdownText != null)
            {
                foreach (string label in countdownLabels)
                {
                    countdownText.text = label;
                    yield return new WaitForSeconds(countdownInterval);
                }
                countdownText.text = string.Empty;
            }

            yield return FadeTo(1f);

            if (!string.IsNullOrWhiteSpace(gameplaySceneName))
                SceneManager.LoadScene(gameplaySceneName);
            else
                Debug.LogError("PreparationFlowController: ゲームシーン名が設定されていません。");
        }

        private IEnumerator FadeTo(float target)
        {
            if (fadeOverlay == null)
                yield break;

            fadeOverlay.blocksRaycasts = true;
            float start = fadeOverlay.alpha;
            float duration = Mathf.Max(0.01f, fadeDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                fadeOverlay.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            fadeOverlay.alpha = target;
            fadeOverlay.blocksRaycasts = target > 0.001f;
        }

        private void ShowOnlyCurrentPanel()
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Panel != null)
                    steps[i].Panel.SetActive(i == currentStepIndex);
            }
        }

        private void ClearDetectionSignals()
        {
            handsRaised = false;
            faceAligned = false;
            voiceDetected = false;
        }

        private void SetSimulatedCameraAvailable(bool available)
        {
            simulatedCameraAvailable = available;
            if (cameraSurface != null)
                cameraSurface.SetCameraAvailable(available);
        }

        private void UpdateDebugOverlay()
        {
            if (debugOverlayRoot != null)
                debugOverlayRoot.SetActive(developmentMode && showDebugOverlay);

            if (debugText == null || !developmentMode || !showDebugOverlay)
                return;

            string stepName = steps.Count > 0 && currentStepIndex < steps.Count
                ? steps[currentStepIndex].DisplayName
                : "未設定";

            debugText.text =
                "開発モード  |  " + (currentStepIndex + 1) + "/" + steps.Count + "  " + stepName + "\n" +
                "Space: 次へ   Backspace: 戻る   R: 最初から   C: カメラ模擬切替\n" +
                "カメラ: " + (simulatedCameraAvailable ? "あり（模擬）" : "なし");
        }
    }
}
