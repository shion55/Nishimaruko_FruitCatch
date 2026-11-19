
using UnityEngine;

public class PitchFallSpeedController : MonoBehaviour
{
    [Header("Microphone")]
    [SerializeField] private MicrophoneAnalysis microphone;

    [Header("Pitch Range")]
    [SerializeField] private float minPitchHz = 120f;
    [SerializeField] private float maxPitchHz = 600f;

    [Header("Fall Speed")]
    [SerializeField] private float maxFallSpeed = 4f;

    [Range(0f, 1f)]
    [SerializeField] private float minSpeedMultiplier = 0.25f;

    [Tooltip("ON: 高い声ほど速い / OFF: 高い声ほど遅い")]
    [SerializeField] private bool highPitchFaster = false;

    [Header("Smoothing")]
    [SerializeField] private float smoothTime = 0.15f;
    [SerializeField] private float pitchHoldTime = 0.2f;

    private float currentMultiplier = 1f;
    private float targetMultiplier = 1f;
    private float lastPitchTime = -Mathf.Infinity;

    void OnEnable()
    {
        currentMultiplier = 1f;
        targetMultiplier = 1f;
        lastPitchTime = -Mathf.Infinity;

        ApplySpeed();
    }

    void Update()
    {
        float pitch = microphone != null
            ? microphone.PitchHz
            : 0f;

        if (pitch > 0f)
        {
            lastPitchTime = Time.time;

            float normalizedPitch = Mathf.InverseLerp(
                minPitchHz,
                Mathf.Max(minPitchHz + 1f, maxPitchHz),
                pitch
            );

            // ONなら高音ほど速い、OFFなら高音ほど遅い
            if (highPitchFaster)
            {
                targetMultiplier = Mathf.Lerp(
                    minSpeedMultiplier,
                    1f,
                    normalizedPitch
                );
            }
            else
            {
                targetMultiplier = Mathf.Lerp(
                    1f,
                    minSpeedMultiplier,
                    normalizedPitch
                );
            }
        }
        else if (Time.time - lastPitchTime > pitchHoldTime)
        {
            // 声がなくなったら通常速度へ
            targetMultiplier = 1f;
        }

        // 滑らかに速度を変化
        float t = 1f - Mathf.Exp(
            -Time.deltaTime /
            Mathf.Max(0.001f, smoothTime)
        );

        currentMultiplier = Mathf.Lerp(
            currentMultiplier,
            targetMultiplier,
            t
        );

        ApplySpeed();
    }

    private void ApplySpeed()
    {
        float maxSpeed = Mathf.Max(0f, maxFallSpeed);

        // すべての落下物に同じ設定を適用
        FallingObject.FruitMaxSpeed = maxSpeed;
        FallingObject.BugMaxSpeed = maxSpeed;

        FallingObject.FruitMultiplier = currentMultiplier;
        FallingObject.BugMultiplier = currentMultiplier;
    }

    void OnDisable()
    {
        FallingObject.FruitMultiplier = 1f;
        FallingObject.BugMultiplier = 1f;

        FallingObject.FruitMaxSpeed = float.PositiveInfinity;
        FallingObject.BugMaxSpeed = float.PositiveInfinity;
    }
}
