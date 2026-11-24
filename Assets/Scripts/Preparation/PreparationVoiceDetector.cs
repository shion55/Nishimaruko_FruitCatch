using UnityEngine;
using UnityEngine.UI;

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

        private Sprite closedSprite;
        private Vector3 restScale;
        private bool detected;
        private float quietSeconds;
        private float displayedVolume;
        private Image levelFill;
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
        }

        private void Update()
        {
            if (flow == null)
                return;

            bool voiceStep = flow.CurrentRequiredSignal == PreparationSignal.VoiceDetected &&
                flow.CurrentPanel != null && flow.CurrentPanel.activeInHierarchy;
            if (!voiceStep)
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
            UpdateDetection(gain, available, Time.unscaledDeltaTime);
            flow.ReportVoiceDetected(detected);
            UpdateFeedback(gain);
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
                eaterImage.transform.localScale = Vector3.Scale(restScale,
                    new Vector3(1f + displayedVolume * 0.25f, 1f + displayedVolume * 0.1f, 1f));
            }
            if (levelFill == null) return;
            levelFill.rectTransform.anchorMax = new Vector2(displayedVolume, 1f);
            levelFill.color = detected ? new Color(0.1f, 0.75f, 0.35f) : new Color(0.1f, 0.65f, 0.9f);
        }

        private void BuildFeedback()
        {
            Transform panel = eaterImage.transform.parent;
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
            UpdateDetection(0f, false, 0f);
            flow?.ReportVoiceDetected(false);
        }
    }
}
