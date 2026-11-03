
using UnityEngine;

public class PitchFallSpeedController : MonoBehaviour
{
    [Header("Microphone")]
    [SerializeField] private MicrophoneAnalysis microphone;

    [Header("Pitch Range")]
    [SerializeField] private float minPitchHz = 120f;
    [SerializeField] private float maxPitchHz = 600f;

    [Header("Fruit")]
    [SerializeField] private float fruitMaxSpeed = 4f;

    [Range(0f, 1f)]
    [SerializeField] private float fruitMinMultiplier = 0.25f;

    [Header("Bug")]
    [SerializeField] private float bugMaxSpeed = 4f;

    [Range(0f, 1f)]
    [SerializeField] private float bugMinMultiplier = 0.25f;

    [Header("Smoothing")]
    [SerializeField] private float smoothTime = 0.15f;
    [SerializeField] private float pitchHoldTime = 0.2f;

    private float currentFruit = 1f;
    private float currentBug = 1f;

    private float targetFruit = 1f;
    private float targetBug = 1f;

    private float lastPitchTime = -Mathf.Infinity;

    void OnEnable()
    {
        currentFruit = 1f;
        currentBug = 1f;
        targetFruit = 1f;
        targetBug = 1f;

        FallingObject.FruitMultiplier = 1f;
        FallingObject.BugMultiplier = 1f;
    }

    void Update()
    {
        // それぞれの最高速度
        FallingObject.FruitMaxSpeed =
            Mathf.Max(0f, fruitMaxSpeed);

        FallingObject.BugMaxSpeed =
            Mathf.Max(0f, bugMaxSpeed);

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

            // 高い声ほどフルーツが遅くなる
            targetFruit = Mathf.Lerp(
                1f,
                fruitMinMultiplier,
                normalizedPitch
            );

            // 低い声ほど虫が遅くなる
            targetBug = Mathf.Lerp(
                bugMinMultiplier,
                1f,
                normalizedPitch
            );
        }
        else if (Time.time - lastPitchTime > pitchHoldTime)
        {
            // 声がなくなったら通常速度へ
            targetFruit = 1f;
            targetBug = 1f;
        }

        // 滑らかに速度を変化
        float t = 1f - Mathf.Exp(
            -Time.deltaTime /
            Mathf.Max(0.001f, smoothTime)
        );

        currentFruit = Mathf.Lerp(
            currentFruit, targetFruit, t
        );

        currentBug = Mathf.Lerp(
            currentBug, targetBug, t
        );

        FallingObject.FruitMultiplier = currentFruit;
        FallingObject.BugMultiplier = currentBug;
    }

    void OnDisable()
    {
        FallingObject.FruitMultiplier = 1f;
        FallingObject.BugMultiplier = 1f;
        FallingObject.FruitMaxSpeed = float.PositiveInfinity;
        FallingObject.BugMaxSpeed = float.PositiveInfinity;
    }
}
