using UnityEngine;

public sealed class PitchFallSpeedSettings : ScriptableObject
{
    [SerializeField] internal float minPitchHz = 120f;
    [SerializeField] internal float maxPitchHz = 600f;
    [SerializeField, Range(0f, 1f)] internal float minSpeedMultiplier = 0.25f;
    [SerializeField, Tooltip("ON: 高い声ほど速い / OFF: 高い声ほど遅い")]
    internal bool highPitchFaster;
    [SerializeField] internal float smoothTime = 0.15f;
    [SerializeField] internal float pitchHoldTime = 0.2f;
}
