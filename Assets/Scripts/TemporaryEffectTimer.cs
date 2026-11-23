using UnityEngine;

/// <summary>
/// 再発動時に残り時間を設定し直す、一時効果用の独立したタイマーです。
/// </summary>
public sealed class TemporaryEffectTimer
{
    public float Remaining { get; private set; }
    public bool IsActive => Remaining > 0f;

    public void Refresh(float duration)
    {
        Remaining = Mathf.Max(0f, duration);
    }

    public void Tick(float deltaTime)
    {
        Remaining = Mathf.Max(0f, Remaining - Mathf.Max(0f, deltaTime));
    }

    public void Clear()
    {
        Remaining = 0f;
    }
}
