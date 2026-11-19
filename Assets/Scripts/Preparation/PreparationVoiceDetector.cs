using UnityEngine;

namespace FruitCatch.Preparation
{
    /// <summary>
    /// Preparationの声入力を既存のMicrophoneAnalysisから進行信号へ変換します。
    /// 閾値と継続時間はFlow側のStep設定から調整できます。
    /// </summary>
    public sealed class PreparationVoiceDetector : MonoBehaviour
    {
        [SerializeField] private PreparationFlowController flow;
        [SerializeField] private MicrophoneAnalysis microphone;
        [SerializeField, Min(0f)] private float gainThreshold = 0.1f;

        private void Reset()
        {
            flow = GetComponent<PreparationFlowController>();
            microphone = GetComponent<MicrophoneAnalysis>();
        }

        private void Update()
        {
            if (flow == null)
                return;

            bool detected = microphone != null &&
                            microphone.IsCapturing &&
                            microphone.Gain >= gainThreshold;
            flow.ReportVoiceDetected(detected);
        }
    }
}
