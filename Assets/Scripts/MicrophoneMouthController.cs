using UnityEngine;

public class MicrophoneMouthController : MonoBehaviour
{
    [SerializeField] private SpriteRenderer targetSprite;
    [SerializeField] private float threshold = 0.02f;
    [SerializeField] private Color activeColor = Color.red;

    public bool IsMouthOpen { get; private set; }

    private Color normalColor;
    private AudioClip microphoneClip;
    private float[] samples = new float[1024];

    void Start()
    {
        if (targetSprite == null)
            targetSprite = GetComponent<SpriteRenderer>();

        normalColor = targetSprite.color;

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("ƒ}ƒCƒN‚ªŒ©‚Â‚©‚è‚Ü‚¹‚ñ");
            return;
        }

        microphoneClip = Microphone.Start(null, true, 1, 44100);
    }

    void Update()
    {
        if (microphoneClip == null) return;

        int position = Microphone.GetPosition(null);
        if (position <= 0) return;

        int offset = (position - samples.Length
                      + microphoneClip.samples)
                      % microphoneClip.samples;

        microphoneClip.GetData(samples, offset);

        float sum = 0f;

        foreach (float sample in samples)
            sum += sample * sample;

        float rms = Mathf.Sqrt(sum / samples.Length);

        IsMouthOpen = rms > threshold;

        targetSprite.color =
            IsMouthOpen ? activeColor : normalColor;
    }

    void OnDestroy()
    {
        if (microphoneClip != null)
            Microphone.End(null);
    }
}
