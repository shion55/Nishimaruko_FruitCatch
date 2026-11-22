
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

    // 演出も、落下速度に適用した平滑化済みの値を参照する。
    public float CurrentSpeedMultiplier => currentMultiplier;
    public float SlowdownStrength => Mathf.InverseLerp(1f, minSpeedMultiplier, currentMultiplier);
    public bool HasPitchInput { get; private set; }

    void OnEnable()
    {
        currentMultiplier = 1f;
        targetMultiplier = 1f;
        lastPitchTime = -Mathf.Infinity;
        HasPitchInput = false;

        ApplySpeed();
    }

    void Update()
    {
        float pitch = microphone != null
            ? microphone.PitchHz
            : 0f;

        if (MouseDebugInput.Enabled)
        {
            HasPitchInput = MouseDebugInput.TryGetNormalizedPitch(out float normalizedPitch);
            targetMultiplier = HasPitchInput ? GetSpeedMultiplier(normalizedPitch) : 1f;
            lastPitchTime = -Mathf.Infinity;
        }
        else if (pitch > 0f)
        {
            HasPitchInput = true;
            lastPitchTime = Time.time;

            float normalizedPitch = Mathf.InverseLerp(
                minPitchHz,
                Mathf.Max(minPitchHz + 1f, maxPitchHz),
                pitch
            );

            targetMultiplier = GetSpeedMultiplier(normalizedPitch);
        }
        else
        {
            HasPitchInput = Time.time - lastPitchTime <= pitchHoldTime;
            if (!HasPitchInput)
            {
                // 声がなくなったら通常速度へ
                targetMultiplier = 1f;
            }
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

    private float GetSpeedMultiplier(float normalizedPitch)
    {
        // マイクとマウスで、高低の向き・速度範囲を共通にする。
        return highPitchFaster
            ? Mathf.Lerp(minSpeedMultiplier, 1f, normalizedPitch)
            : Mathf.Lerp(1f, minSpeedMultiplier, normalizedPitch);
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
        HasPitchInput = false;
        currentMultiplier = 1f;
        targetMultiplier = 1f;
        FallingObject.FruitMultiplier = 1f;
        FallingObject.BugMultiplier = 1f;

        FallingObject.FruitMaxSpeed = float.PositiveInfinity;
        FallingObject.BugMaxSpeed = float.PositiveInfinity;
    }
}
