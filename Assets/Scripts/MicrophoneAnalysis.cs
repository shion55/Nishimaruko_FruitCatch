using UnityEngine;

public class MicrophoneAnalysis : MonoBehaviour
{

    [Header("Microphone")]
    [Tooltip("空欄ならOSのデフォルトマイク")]
    [SerializeField] private string deviceName = "";
    [SerializeField] private int sampleRate = 44100;
    [SerializeField, Min(0.02f)] private float updateInterval = 0.05f;

    [Header("Pitch (YIN)")]
    [SerializeField, Min(40f)] private float minPitchHz = 70f;
    [SerializeField, Min(100f)] private float maxPitchHz = 1000f;
    [SerializeField, Range(0.05f, 0.3f)] private float yinThreshold = 0.15f;
    [SerializeField, Range(0f, 0.1f)] private float pitchNoiseGate = 0.01f;

    public float Gain { get; private set; }      // RMS: 0～1程度
    public float GainDb { get; private set; }    // dBFS: 通常は負の値
    public float PitchHz { get; private set; }   // ピッチ検出不可なら0
    public float PitchConfidence { get; private set; } // 0～1

    private const int AnalysisFrames = 2048;

    private AudioClip microphoneClip;
    private string activeDevice;
    private float[] interleavedSamples;
    private float[] monoSamples;
    private float[] yinDifference;
    private int maxLag;
    private int minLag;
    private int readFrames;
    private int channels;
    private float elapsed;
    private bool bufferReady;

    private void Start()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("MicrophoneAnalyzer: マイクが見つかりません。");
            enabled = false;
            return;
        }

        activeDevice = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName;
        sampleRate = Mathf.Max(8000, sampleRate);

        float low = Mathf.Max(40f, minPitchHz);
        float high = Mathf.Max(low + 1f, maxPitchHz);
        minLag = Mathf.Max(2, Mathf.FloorToInt(sampleRate / high));
        maxLag = Mathf.CeilToInt(sampleRate / low);
        readFrames = AnalysisFrames + maxLag;

        microphoneClip = Microphone.Start(activeDevice, true, 1, sampleRate);
        if (microphoneClip == null || microphoneClip.samples < readFrames)
        {
            Debug.LogError("MicrophoneAnalyzer: マイクの開始またはバッファ確保に失敗しました。");
            enabled = false;
            return;
        }

        channels = microphoneClip.channels;
        interleavedSamples = new float[readFrames * channels];
        monoSamples = new float[readFrames];
        yinDifference = new float[maxLag + 1];
        GainDb = -120f;
    }

    private void Update()
    {
        if (microphoneClip == null) return;

        elapsed += Time.unscaledDeltaTime;
        if (elapsed < updateInterval) return;
        elapsed = 0f;

        int position = Microphone.GetPosition(activeDevice);
        if (position < 0) return;

        // 録音開始直後に未書き込み領域を読まないようにする。
        if (!bufferReady)
        {
            if (position < readFrames) return;
            bufferReady = true;
        }

        int offset = (position - readFrames + microphoneClip.samples)
                     % microphoneClip.samples;

        if (!microphoneClip.GetData(interleavedSamples, offset)) return;

        // ステレオ入力でもモノラルにまとめて解析する。
        for (int i = 0; i < readFrames; i++)
        {
            float sum = 0f;
            int start = i * channels;
            for (int c = 0; c < channels; c++)
                sum += interleavedSamples[start + c];
            monoSamples[i] = sum / channels;
        }

        // DCオフセットを除去。
        double mean = 0.0;
        for (int i = 0; i < readFrames; i++) mean += monoSamples[i];
        float dc = (float)(mean / readFrames);
        for (int i = 0; i < readFrames; i++) monoSamples[i] -= dc;

        double power = 0.0;
        for (int i = 0; i < AnalysisFrames; i++)
            power += monoSamples[i] * monoSamples[i];

        Gain = Mathf.Sqrt((float)(power / AnalysisFrames));
        GainDb = 20f * Mathf.Log10(Mathf.Max(Gain, 0.000001f));

        PitchHz = 0f;
        PitchConfidence = 0f;
        if (Gain < pitchNoiseGate) return;
        Debug.Log(Gain);
        AnalyzePitch();
    }

    private void AnalyzePitch()
    {
        // YINの差分関数。比較区間の長さを固定してラグ間の偏りを抑える。
        for (int tau = 1; tau <= maxLag; tau++)
        {
            double sum = 0.0;
            for (int i = 0; i < AnalysisFrames; i++)
            {
                float d = monoSamples[i] - monoSamples[i + tau];
                sum += d * d;
            }
            yinDifference[tau] = (float)sum;
        }

        // Cumulative Mean Normalized Difference Function (CMNDF)
        double runningSum = 0.0;
        for (int tau = 1; tau <= maxLag; tau++)
        {
            runningSum += yinDifference[tau];
            yinDifference[tau] = runningSum > 1e-12
                ? (float)(yinDifference[tau] * tau / runningSum)
                : 1f;
        }

        int bestLag = -1;
        for (int tau = minLag; tau < maxLag; tau++)
        {
            if (yinDifference[tau] < yinThreshold)
            {
                while (tau < maxLag && yinDifference[tau + 1] < yinDifference[tau])
                    tau++;
                bestLag = tau;
                break;
            }
        }

        if (bestLag < 0) return;

        // 放物線補間で整数サンプル以下の周期を推定。
        float refinedLag = bestLag;
        if (bestLag > 1 && bestLag < maxLag)
        {
            float left = yinDifference[bestLag - 1];
            float middle = yinDifference[bestLag];
            float right = yinDifference[bestLag + 1];
            float denominator = left - 2f * middle + right;
            if (Mathf.Abs(denominator) > 0.000001f)
            {
                float shift = 0.5f * (left - right) / denominator;
                refinedLag += Mathf.Clamp(shift, -1f, 1f);
            }
        }

        PitchHz = sampleRate / refinedLag;
        PitchConfidence = Mathf.Clamp01(1f - yinDifference[bestLag]);
    }

    private void OnDisable()
    {
        if (microphoneClip != null)
        {
            Microphone.End(activeDevice);
            microphoneClip = null;
        }
    }
}
    
