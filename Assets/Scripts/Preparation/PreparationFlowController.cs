using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FruitCatch.Preparation
{
    public enum PreparationSignal
    {
        Manual,
        HandsRaised,
        FaceAligned,
        VoiceDetected,
        Timed,
        MovementPractice,
        FruitPractice
    }

    [Serializable]
    public sealed class PreparationStep
    {
        [SerializeField] private string displayName = "説明";
        [SerializeField] private GameObject panel;
        [SerializeField] private PreparationSignal requiredSignal = PreparationSignal.Manual;
        [SerializeField, Min(0f)] private float requiredHoldSeconds = 0.8f;
        [SerializeField] private bool showCountdown;

        public string DisplayName => displayName;
        public GameObject Panel => panel;
        public PreparationSignal RequiredSignal => requiredSignal;
        public float RequiredHoldSeconds => requiredHoldSeconds;
        public bool ShowCountdown => showCountdown;
    }

    /// <summary>
    /// Preparationシーンの進行だけを管理します。
    /// 顔・手・声の検知器はReport～メソッドを呼び出すだけで接続できます。
    /// </summary>
    public sealed class PreparationFlowController : MonoBehaviour
    {
        [Header("画面構成")]
        [SerializeField] private List<PreparationStep> steps = new List<PreparationStep>();
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
        private bool practiceComplete;
        private int resumePracticeIndex = -1;
        private bool transitioning;
        private bool simulatedCameraAvailable;
        private readonly Dictionary<PreparationStep, PreparationCountdown> stepCountdowns = new();
        private PreparationCountdown startCountdown;
        private PreparationCrossfade crossfade;

        public int CurrentStepIndex => currentStepIndex;
        internal IReadOnlyList<PreparationStep> OperatorSteps => steps;
        internal string OperatorStage => currentStepIndex < steps.Count
            ? steps[currentStepIndex].DisplayName : "準備中";
        internal bool CanOperateStep => !leaving && !transitioning && currentStepIndex < steps.Count;
        internal void OperatorNext()
        {
            if (CanOperateStep) StartCoroutine(AdvanceRoutine());
        }
        internal void OperatorRestart(bool keepRegistration)
        {
            string target = keepRegistration ? gameplaySceneName : gameObject.scene.name;
            if (!Application.CanStreamedLevelBeLoaded(target)) return;
            FruitCatchGameSession.ChangeScene(target, () =>
            {
                StopForOperator();
                if (!keepRegistration) PreparationPythonBridge.ClearRegistration();
            });
        }
        public bool DevelopmentMode => developmentMode;
        private bool DebugEnabled => developmentMode && (Application.isEditor || Debug.isDebugBuild);
        public bool IsTransitioning => transitioning;
        internal bool IsLeaving => leaving;
        public float CurrentSignalProgress => steps.Count > 0 && currentStepIndex < steps.Count
            ? Mathf.Clamp01(signalHoldTime / Mathf.Max(0.001f, steps[currentStepIndex].RequiredHoldSeconds)) : 0f;
        public GameObject CurrentPanel => steps.Count > 0 && currentStepIndex < steps.Count
            ? steps[currentStepIndex].Panel : null;
        public PreparationSignal CurrentRequiredSignal =>
            steps.Count > 0 && currentStepIndex < steps.Count
                ? steps[currentStepIndex].RequiredSignal
                : PreparationSignal.Manual;

        private void Start()
        {
            if (countdownText != null)
                startCountdown = new PreparationCountdown(countdownText.transform.parent,
                    countdownText.font, true, countdownText);

            foreach (PreparationStep step in steps)
            {
                if (step.ShowCountdown && step.RequiredSignal == PreparationSignal.Timed && step.Panel != null)
                {
                    TMP_Text template = countdownText != null ? countdownText :
                        step.Panel.GetComponentInChildren<TMP_Text>(true);
                    stepCountdowns.Add(step, new PreparationCountdown(step.Panel.transform, template?.font, false));
                }
            }
            currentStepIndex = 0;
            ShowOnlyCurrentPanel();
            StartMainMenuBgm();

            if (countdownText != null)
                countdownText.text = string.Empty;

            SetSimulatedCameraAvailable(false);
            UpdateDebugOverlay();
        }

        private void Update()
        {
            if (leaving || FruitCatchGameSession.HandleOperatorInput(gameObject.scene.name, null, StopForOperator))
                return;

            HandleDevelopmentInput();
            UpdateSignalProgress();
            UpdateDebugOverlay();
        }

        private bool leaving;

        internal void StopForOperator()
        {
            leaving = true;
            transitioning = true;
            StopAllCoroutines();
            CancelCrossfade();
            if (TryGetComponent(out PreparationPythonBridge bridge))
                bridge.enabled = false;
            FruitCatchAudioManager.StopGameAudioIfAvailable();
        }

        private void OnDisable()
        {
            if (crossfade == null)
                return;
            StopAllCoroutines();
            CancelCrossfade();
            transitioning = false;
        }

        private void CancelCrossfade()
        {
            if (crossfade != null)
                crossfade.Cancel();
            crossfade = null;
        }

        public void ReportHandsRaised(bool detected) => handsRaised = detected;
        public void ReportFaceAligned(bool detected) => faceAligned = detected;
        public void ReportVoiceDetected(bool detected) => voiceDetected = detected;

        public void ReportPracticeComplete(GameObject panel)
        {
            if (!transitioning && panel == CurrentPanel && IsPractice)
                practiceComplete = true;
        }

        private bool IsPractice => CurrentRequiredSignal == PreparationSignal.MovementPractice ||
                                   CurrentRequiredSignal == PreparationSignal.FruitPractice;

        public void RetryRegistration()
        {
            if (!IsPractice || transitioning || practiceComplete) return;
            int faceIndex = steps.FindIndex(step => step.RequiredSignal == PreparationSignal.FaceAligned);
            if (faceIndex < 0) return;
            resumePracticeIndex = currentStepIndex;
            StartCoroutine(ChangeStepRoutine(faceIndex));
        }

        public void AdvanceForDebug()
        {
            if (!DebugEnabled || transitioning)
                return;

            StartCoroutine(AdvanceRoutine());
        }

        public void GoBackForDebug()
        {
            if (!DebugEnabled || transitioning || currentStepIndex <= 0)
                return;

            resumePracticeIndex = -1;
            StartCoroutine(ChangeStepRoutine(currentStepIndex - 1));
        }

        public void RestartForDebug()
        {
            if (!DebugEnabled || transitioning)
                return;

            StopAllCoroutines();
            transitioning = false;
            currentStepIndex = 0;
            signalHoldTime = 0f;
            resumePracticeIndex = -1;
            ClearDetectionSignals();
            ShowOnlyCurrentPanel();
            StartMainMenuBgm();

            if (countdownText != null)
                countdownText.text = string.Empty;
        }

        private void HandleDevelopmentInput()
        {
            if (!DebugEnabled || FruitCatchOperatorPanel.BlocksGameplayInput)
                return;

            Keyboard keyboard = Keyboard.current;

            if ((keyboard != null && keyboard.spaceKey.wasPressedThisFrame) ||
                (MouseDebugInput.LeftButtonPressed && !IsPractice && CurrentRequiredSignal != PreparationSignal.VoiceDetected))
                AdvanceForDebug();

            if ((keyboard != null && keyboard.backspaceKey.wasPressedThisFrame) ||
                MouseDebugInput.RightButtonPressed)
                GoBackForDebug();

            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                RestartForDebug();

            if (keyboard != null && keyboard.cKey.wasPressedThisFrame)
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
            UpdateStepCountdown(step);
            if (signalHoldTime >= step.RequiredHoldSeconds)
                StartCoroutine(AdvanceRoutine(true));
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
                case PreparationSignal.MovementPractice:
                case PreparationSignal.FruitPractice:
                    return practiceComplete;
                default:
                    return false;
            }
        }

        private IEnumerator AdvanceRoutine(bool signalConfirmed = false)
        {
            if (transitioning || steps.Count == 0)
                yield break;

            signalHoldTime = 0f;
            int nextIndex = currentStepIndex + 1;

            if (signalConfirmed && CurrentRequiredSignal == PreparationSignal.VoiceDetected &&
                TryGetComponent<PreparationVoiceDetector>(out var voice))
            {
                transitioning = true;
                yield return voice.ShowRecognition();
            }

            if (CurrentRequiredSignal == PreparationSignal.FaceAligned && CurrentPanel != null)
            {
                if (resumePracticeIndex >= 0)
                {
                    nextIndex = resumePracticeIndex;
                    resumePracticeIndex = -1;
                }
                PreparationFacePresentation presentation = CurrentPanel.GetComponentInChildren<PreparationFacePresentation>();
                if (presentation != null)
                {
                    transitioning = true;
                    yield return presentation.PlaySuccess();
                }
            }

            if (currentStepIndex >= steps.Count - 1)
            {
                yield return CountdownAndLoadRoutine();
                yield break;
            }

            yield return ChangeStepRoutine(nextIndex);
        }

        private IEnumerator ChangeStepRoutine(int nextIndex)
        {
            transitioning = true;

            bool leavingMainMenu = currentStepIndex == 0 && nextIndex != 0;
            if (leavingMainMenu)
                FruitCatchAudioManager.Instance.FadeOutMusic(fadeDuration);
            yield return CaptureCrossfade();

            currentStepIndex = Mathf.Clamp(nextIndex, 0, Mathf.Max(0, steps.Count - 1));
            signalHoldTime = 0f;
            ClearDetectionSignals();
            ShowOnlyCurrentPanel();

            if (currentStepIndex == 0)
                StartMainMenuBgm();

            if (crossfade != null)
                yield return crossfade.FadeOut(fadeDuration);
            crossfade = null;

            transitioning = false;
        }

        private IEnumerator CountdownAndLoadRoutine()
        {
            transitioning = true;

            if (startCountdown != null)
            {
                foreach (string label in countdownLabels)
                {
                    PlayCountdownSound();
                    float elapsed = 0f;
                    while (elapsed < countdownInterval)
                    {
                        startCountdown.Show(label, elapsed / countdownInterval);
                        yield return null;
                        elapsed += Time.deltaTime;
                    }
                }
                startCountdown.Hide();
            }

            if (string.IsNullOrWhiteSpace(gameplaySceneName) || !Application.CanStreamedLevelBeLoaded(gameplaySceneName))
            {
                Debug.LogError($"PreparationFlowController: ゲームシーンを読み込めません。'{gameplaySceneName}'");
                transitioning = false;
                yield break;
            }

            yield return CaptureCrossfade();
            PreparationCrossfade sceneCrossfade = crossfade;
            crossfade = null;
            if (sceneCrossfade != null)
                sceneCrossfade.FadeIntoScene(gameplaySceneName, fadeDuration);
            if (!FruitCatchGameSession.ChangeScene(gameplaySceneName, FruitCatchAudioManager.StopGameAudioIfAvailable))
            {
                if (sceneCrossfade != null)
                    sceneCrossfade.Cancel();
                transitioning = false;
            }
        }

        private IEnumerator CaptureCrossfade()
        {
            if (fadeDuration <= 0f || FruitCatchOperatorPanel.BlocksGameplayInput)
                yield break;
            crossfade = PreparationCrossfade.Create();
            yield return crossfade.Capture();
        }

        private void StartMainMenuBgm() => FruitCatchAudioManager.Instance.PlayMainMenuBgm();

        private void PlayCountdownSound() => FruitCatchAudioManager.Instance.PlayCountdown();

        private void ShowOnlyCurrentPanel()
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (i != currentStepIndex && steps[i].Panel != null)
                    steps[i].Panel.SetActive(false);
            }

            // Release the previous practice panel's UDP port before opening the next one.
            CurrentPanel?.SetActive(true);
            if (currentStepIndex < steps.Count)
                UpdateStepCountdown(steps[currentStepIndex]);
        }

        private void UpdateStepCountdown(PreparationStep step)
        {
            if (!stepCountdowns.TryGetValue(step, out PreparationCountdown dial))
                return;

            float remaining = step.RequiredHoldSeconds - signalHoldTime;
            int seconds = Mathf.Max(1, Mathf.CeilToInt(remaining));
            dial.Show(seconds.ToString(), seconds - remaining);
        }

        private void ClearDetectionSignals()
        {
            handsRaised = false;
            faceAligned = false;
            voiceDetected = false;
            practiceComplete = false;
        }

        private void SetSimulatedCameraAvailable(bool available)
        {
            simulatedCameraAvailable = available;
            if (cameraSurface != null)
                cameraSurface.SetCameraAvailable(available);
        }

        private void UpdateDebugOverlay()
        {
            bool visible = showDebugOverlay &&
                (DebugEnabled || FruitCatchOperatorSettings.HasOverride("prep.showDebugOverlay"));
            if (debugOverlayRoot != null)
                debugOverlayRoot.SetActive(visible);

            if (debugText == null || !visible)
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
