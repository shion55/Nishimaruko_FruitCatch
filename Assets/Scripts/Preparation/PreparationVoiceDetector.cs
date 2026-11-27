using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

namespace FruitCatch.Preparation
{
    /// <summary>
    /// Preparationの声入力を既存のMicrophoneAnalysisから進行信号へ変換します。
    /// 開口・音量表示と進行で同じ声検出を使用します。
    /// </summary>
    public sealed class PreparationVoiceDetector : MonoBehaviour
    {
        [SerializeField] private PreparationFlowController flow;
        [SerializeField] private MicrophoneAnalysis microphone;
        [SerializeField, Min(0.001f)] private float gainThreshold = 0.015f;
        [SerializeField, Min(0f)] private float releaseGraceSeconds = 0.2f;
        [SerializeField] private Image eaterImage;
        [SerializeField] private Sprite mouthOpenSprite;
        [SerializeField] private TMP_Text microphoneLabel, practiceMicrophoneLabel;
        [SerializeField, Min(0f)] private float reactionScale = 0.28f;
        [SerializeField, Range(0f, 1f)] private float voiceBgmVolume = 0f;
        [SerializeField, Min(0f)] private float voiceBgmFadeSeconds = 0.3f;
        [SerializeField, Range(2f, 3f)] private float recognitionSeconds = 2.5f;
        [SerializeField, Min(0f)] private float practiceMouthThreshold = 0.1f;

        private Sprite closedSprite;
        private Vector3 restScale;
        private bool detected;
        private float quietSeconds;
        private float displayedVolume;
        private Image levelFill;
        private PreparationOvalGraphic recognition;
        private bool voiceMusicPlaying;
        private bool voiceFeedbackRequested;
        private const string MicrophonePreferenceKey = "FruitCatch.SelectedMicrophone";

        public bool IsVoiceDetected => detected;

        private void Reset()
        {
            flow = GetComponent<PreparationFlowController>();
            microphone = GetComponent<MicrophoneAnalysis>();
        }

        private void Start()
        {
            if (flow == null) flow = GetComponent<PreparationFlowController>();
            if (microphone == null) microphone = GetComponent<MicrophoneAnalysis>();
            if (eaterImage != null)
            {
                closedSprite = eaterImage.sprite;
                restScale = eaterImage.transform.localScale;
                BuildFeedback();
            }
            string saved = PlayerPrefs.GetString(MicrophonePreferenceKey, "");
            if (microphone != null && !string.IsNullOrEmpty(saved) &&
                System.Array.IndexOf(Microphone.devices, saved) >= 0)
                microphone.SelectMicrophone(saved);
            RefreshMicrophoneLabel();
        }

        public void CycleMicrophone()
        {
            if (microphone == null) return;
            string[] devices = Microphone.devices;
            if (devices.Length > 0)
            {
                int next = (System.Array.IndexOf(devices, microphone.ActiveDeviceName) + 1) % devices.Length;
                if (microphone.SelectMicrophone(devices[next]))
                {
                    PlayerPrefs.SetString(MicrophonePreferenceKey, devices[next]);
                    PlayerPrefs.Save();
                }
            }
            RefreshMicrophoneLabel();
        }

        private void RefreshMicrophoneLabel()
        {
            string label = microphone != null && microphone.IsCapturing ?
                "マイク切替  " + microphone.ActiveDeviceName : "マイクを接続して、ここを押してね";
            if (microphoneLabel != null) microphoneLabel.text = label;
            if (practiceMicrophoneLabel != null) practiceMicrophoneLabel.text = label;
        }

        private void Update()
        {
            if (flow == null)
                return;

            bool activePanel = flow.CurrentPanel != null && flow.CurrentPanel.activeInHierarchy;
            bool voiceStep = flow.CurrentRequiredSignal == PreparationSignal.VoiceDetected && activePanel;
            bool fruitStep = flow.CurrentRequiredSignal == PreparationSignal.FruitPractice;
            SetVoiceFeedback(flow.isActiveAndEnabled && !flow.IsLeaving && activePanel && (voiceStep || fruitStep));
            SetVoiceMusic(voiceStep && voiceBgmVolume > 0f);
            if (!voiceStep && !fruitStep)
            {
                UpdateDetection(0f, false, 0f);
                displayedVolume = 0f;
                if (eaterImage != null)
                {
                    eaterImage.sprite = closedSprite;
                    eaterImage.transform.localScale = restScale;
                }
                flow.ReportVoiceDetected(false);
                return;
            }
            if (flow.IsTransitioning) return;

            bool available = microphone != null && microphone.IsCapturing;
            float gain = available ? microphone.Gain : 0f;
            bool debugVoice = MouseDebugInput.Enabled && MouseDebugInput.LeftButtonHeld;
            if (fruitStep)
                detected = debugVoice || available && gain > practiceMouthThreshold;
            else
                UpdateDetection(debugVoice ? gainThreshold * 2f : gain, available || debugVoice, Time.unscaledDeltaTime);
            flow.ReportVoiceDetected(voiceStep && detected);
            if (voiceStep) UpdateFeedback(gain);
        }

        private void UpdateDetection(float gain, bool available, float deltaTime)
        {
            if (!available)
            {
                detected = false;
                quietSeconds = 0f;
                return;
            }
            float threshold = detected ? gainThreshold * 0.65f : gainThreshold;
            if (gain >= threshold)
            {
                detected = true;
                quietSeconds = 0f;
            }
            else
            {
                quietSeconds += deltaTime;
                if (quietSeconds >= releaseGraceSeconds) detected = false;
            }
        }

        private void UpdateFeedback(float gain)
        {
            float volume = Mathf.Clamp01(gain / Mathf.Max(0.001f, gainThreshold * 4f));
            displayedVolume = Mathf.Lerp(displayedVolume, volume, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 15f));
            if (eaterImage != null)
            {
                eaterImage.sprite = detected && mouthOpenSprite != null ? mouthOpenSprite : closedSprite;
                float reaction = detected ? Mathf.Max(0.55f, displayedVolume) * reactionScale : 0f;
                eaterImage.transform.localScale = Vector3.Scale(restScale,
                    new Vector3(1f + reaction, 1f + reaction * 0.65f, 1f));
            }
            if (levelFill == null) return;
            levelFill.rectTransform.anchorMax = new Vector2(displayedVolume, 1f);
            levelFill.color = detected ? new Color(0.1f, 0.75f, 0.35f) : new Color(0.1f, 0.65f, 0.9f);
        }

        private void BuildFeedback()
        {
            Transform panel = eaterImage.transform.parent;
            var circle = CreateRect("VoiceRecognized", eaterImage.rectTransform, Vector2.one * .5f, Vector2.one * .5f);
            recognition = circle.gameObject.AddComponent<PreparationOvalGraphic>();
            recognition.color = new Color32(230, 35, 57, 255);
            recognition.raycastTarget = false;
            recognition.enabled = false;
            Image track = CreateRect("VoiceLevel", panel, new Vector2(0.06f, 0.06f), new Vector2(0.43f, 0.085f))
                .gameObject.AddComponent<Image>();
            track.color = new Color(0.85f, 0.9f, 0.92f);
            track.raycastTarget = false;
            levelFill = CreateRect("Fill", track.transform, Vector2.zero, new Vector2(0f, 1f)).gameObject.AddComponent<Image>();
            levelFill.raycastTarget = false;
            Image marker = CreateRect("DetectionThreshold", track.transform, new Vector2(0.25f, 0f), new Vector2(0.25f, 1f))
                .gameObject.AddComponent<Image>();
            marker.rectTransform.sizeDelta = new Vector2(3f, 0f);
            marker.color = new Color(0.15f, 0.25f, 0.3f);
            marker.raycastTarget = false;
            UpdateFeedback(0f);
        }

        public IEnumerator ShowRecognition()
        {
            if (recognition != null)
            {
                Rect eaterRect = eaterImage.rectTransform.rect;
                float diameter = Mathf.Min(eaterRect.width, eaterRect.height) * .94f;
                recognition.rectTransform.sizeDelta = Vector2.one * diameter;
                recognition.thickness = diameter * .085f;
                recognition.enabled = true;
            }
            yield return new WaitForSecondsRealtime(recognitionSeconds);
            if (recognition != null) recognition.enabled = false;
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.sizeDelta = Vector2.zero;
            return rect;
        }

        private void OnDisable()
        {
            SetVoiceFeedback(false);
            SetVoiceMusic(false);
            UpdateDetection(0f, false, 0f);
            flow?.ReportVoiceDetected(false);
        }

        private void SetVoiceFeedback(bool play)
        {
            if (play == voiceFeedbackRequested)
                return;
            voiceFeedbackRequested = play;
            if (play && microphone != null)
                FruitCatchAudioManager.Instance.SetVoiceFeedbackInput(microphone);
            else
                FruitCatchAudioManager.ClearVoiceFeedbackInputIfAvailable(microphone);
        }

        private void SetVoiceMusic(bool play)
        {
            if (play == voiceMusicPlaying) return;
            voiceMusicPlaying = play;
            if (play)
                FruitCatchAudioManager.Instance.PlayQuietPreparationBgm(voiceBgmVolume, voiceBgmFadeSeconds);
            else
                FruitCatchAudioManager.Instance.FadeOutPreparationBgm(voiceBgmFadeSeconds);
        }
    }
}
