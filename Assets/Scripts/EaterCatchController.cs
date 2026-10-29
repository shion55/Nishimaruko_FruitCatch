using UnityEngine;

public class EaterCatchController : MonoBehaviour
{
    [SerializeField] private MicrophoneAnalysis microphone;
    [SerializeField] private GameManager gameManager;

    private float scaleVelocity = 0f;
    private void Awake()
    {
        if (microphone == null)
            microphone = GetComponentInParent<MicrophoneAnalysis>();
    }
    private void Update()
    {
        if (! (microphone.Gain < 0.1f))
            return;
        BounceByGain(microphone.Gain, ref scaleVelocity);
    }
    private void BounceByGain(float gain, ref float velocity)
    {
        // ‰¹—Ê‚É‰ž‚¶‚½–Ú•WƒTƒCƒY
        float targetScale = Mathf.Lerp(
            1f, 1.8f, Mathf.Clamp01(gain * 10f)
        );

        // ƒoƒl‚Ì‹­‚³‚Æ—h‚ê‚ÌŒ¸Š
        float stiffness = 100f;
        float damping = 12f;

        float currentScale = transform.localScale.x;
        float dt = Time.deltaTime;

        // ƒoƒl‰^“®
        float acceleration =
            (targetScale - currentScale) * stiffness
            - velocity * damping;

        velocity += acceleration * dt;

        float newScale = Mathf.Max(
            0.1f, currentScale + velocity * dt
        );

        // Collider2D‚àTransform‚É’Ç]‚µ‚ÄŠg‘åk¬
        transform.localScale = Vector3.one * newScale;
    }
    /*
    private void OnTriggerEnter2D(Collider2D other)
    {
        TryCatch(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryCatch(other);
    }

    /*private void TryCatch(Collider2D other)
    {
        if (microphone == null || gameManager == null)
            return;

        // º‚ðo‚µ‚Ä‚¢‚È‚¢‚ÆH‚×‚ç‚ê‚È‚¢
        if (!microphone.IsMouthOpen)
            return;

        CatchableItem item = other.GetComponent<CatchableItem>();

        if (item == null)
            return;

        if (item.TryConsume())
        {
            gameManager.AddScore(item.ScoreDelta);
        }
    }*/
}
