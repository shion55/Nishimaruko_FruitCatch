using UnityEngine;

public class MicrophoneAnalysis : MonoBehaviour
{
    [Header("Microphone")]
    [Tooltip("Empty = OS default microphone")]
    [SerializeField] private string deviceName = "";
    [SerializeField] private int sampleRate = 44100;
    [SerializeField, Min(0.02f)] private float updateInterval = 0.05f;

    [Header("Pitch (YIN)")]
    [SerializeField, Min(40f)] private float minPitchHz = 70f;
    [SerializeField, Min(100f)] private float maxPitchHz = 1000f;
    [SerializeField, Range(0.05f, 0.3f)] private float yinThreshold = 0.15f;
    [SerializeField, Range(0f, 0.1f)] private float pitchNoiseGate = 0.01f;

    public float Gain { get; private set; }
    public float GainDb { get; private set; } = -120f;
    public float PitchHz { get; private set; }
    public float PitchConfidence { get; private set; }

    // Empty means the OS default microphone.
    public string ActiveDeviceName => activeDevice ?? "";
    public bool IsCapturing => microphoneClip != null;

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

    private void Awake()
    {
        sampleRate = Mathf.Max(8000, sampleRate);

        float low = Mathf.Max(40f, minPitchHz);
        float high = Mathf.Max(low + 1f, maxPitchHz);
        minLag = Mathf.Max(2, Mathf.FloorToInt(sampleRate / high));
        maxLag = Mathf.CeilToInt(sampleRate / low);
        readFrames = AnalysisFrames + maxLag;
        monoSamples = new float[readFrames];
        yinDifference = new float[maxLag + 1];
    }

    private void OnEnable()
    {
        SelectMicrophone(deviceName);
    }

    // Called by the debug UI. It changes the microphone without restarting Play mode.
    // Returns false if the selected device cannot be opened.
    public bool SelectMicrophone(string requestedDeviceName)
    {
        requestedDeviceName = string.IsNullOrWhiteSpace(requestedDeviceName)
            ? "" : requestedDeviceName;

        if (Microphone.devices.Length == 0)
        {
            StopMicrophone();
            Debug.LogWarning("MicrophoneAnalysis: No microphone devices found.");
            return false;
        }

        if (requestedDeviceName.Length > 0)
        {
            bool found = false;
            foreach (string name in Microphone.devices)
            {
                if (name == requestedDeviceName)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                Debug.LogWarning("MicrophoneAnalysis: Device not found: " + requestedDeviceName);
                return false;
            }
        }

        // End the previous session before starting the next device.
        StopMicrophone();
        string requested = requestedDeviceName.Length == 0 ? null : requestedDeviceName;

        try
        {
            AudioClip clip = Microphone.Start(requested, true, 1, sampleRate);
            if (clip == null)
            {
                Debug.LogError("MicrophoneAnalysis: Failed to start microphone.");
                return false;
            }
            if (clip.samples < readFrames)
            {
                Microphone.End(requested);
                Debug.LogError("MicrophoneAnalysis: Microphone buffer is too small.");
                return false;
            }

            microphoneClip = clip;
            activeDevice = requested;
            deviceName = requestedDeviceName;
            channels = microphoneClip.channels;
            interleavedSamples = new float[readFrames * channels];
            elapsed = 0f;
            bufferReady = false;
            Gain = 0f;
            GainDb = -120f;
            PitchHz = 0f;
            PitchConfidence = 0f;
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError("MicrophoneAnalysis: Failed to open microphone: " + e.Message);
            return false;
        }
    }

    private void Update()
    {
        if (microphoneClip == null) return;

        elapsed += Time.unscaledDeltaTime;
        if (elapsed < updateInterval) return;
        elapsed = 0f;

        int position = Microphone.GetPosition(activeDevice);
        if (position < 0) return;

        if (!bufferReady)
        {
            if (position < readFrames) return;
            bufferReady = true;
        }

        int offset = (position - readFrames + microphoneClip.samples)
                     % microphoneClip.samples;
        if (!microphoneClip.GetData(interleavedSamples, offset)) return;

        for (int i = 0; i < readFrames; i++)
        {
            float sum = 0f;
            int start = i * channels;
            for (int c = 0; c < channels; c++)
                sum += interleavedSamples[start + c];
            monoSamples[i] = sum / channels;
        }

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

        AnalyzePitch();
    }

    private void AnalyzePitch()
    {
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

    private void StopMicrophone()
    {
        if (microphoneClip != null)
        {
            Microphone.End(activeDevice);
            microphoneClip = null;
        }
        activeDevice = null;
        interleavedSamples = null;
        bufferReady = false;
        Gain = 0f;
        GainDb = -120f;
        PitchHz = 0f;
        PitchConfidence = 0f;
    }

    private void OnDisable()
    {
        StopMicrophone();
    }
}
