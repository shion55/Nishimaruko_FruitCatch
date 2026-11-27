using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TextMeshProUGUI))]
public sealed class ScoreHud : MonoBehaviour
{
    [Header("Score Animation")]
    [SerializeField] private bool scoreAnimationEnabled = true;
    [SerializeField, Range(1f, 1.6f)] private float gainScale = 1.22f;
    [SerializeField, Min(0.01f)] private float gainDuration = 0.23f;
    [SerializeField] private Color lossColor = new Color(1f, 0.38f, 0.44f);
    [SerializeField, Min(0f)] private float lossShakeDistance = 8f;
    [SerializeField, Min(0.01f)] private float lossDuration = 0.28f;

    private TextMeshProUGUI number;
    private RectTransform numberRect;
    private Vector3 restScale;
    private Vector2 restPosition;
    private Color restColor;
    private Material outlineMaterial;
    private int direction;
    private float elapsed;

    private void Awake()
    {
        number = GetComponent<TextMeshProUGUI>();
        numberRect = number.rectTransform;
        restPosition = numberRect.anchoredPosition;
        restScale = numberRect.localScale;
        restColor = number.color;

        if (number.fontSharedMaterial != null)
        {
            outlineMaterial = new Material(number.fontSharedMaterial);
            outlineMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(31, 17, 57, 255));
            outlineMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.19f);
            number.fontSharedMaterial = outlineMaterial;
            number.UpdateMeshPadding();
        }
    }

    internal void PlayScoreChange(int amount)
    {
        if (!isActiveAndEnabled || !scoreAnimationEnabled || amount == 0)
            return;

        direction = amount > 0 ? 1 : -1;
        elapsed = 0f;
        ApplyAnimation(0f);
    }

    private void LateUpdate()
    {
        if (direction == 0)
            return;

        elapsed += Time.unscaledDeltaTime;
        float duration = Mathf.Max(0.01f, direction > 0 ? gainDuration : lossDuration);
        if (!scoreAnimationEnabled || elapsed >= duration)
        {
            ResetAnimation();
            return;
        }
        ApplyAnimation(elapsed / duration);
    }

    private void ApplyAnimation(float progress)
    {
        float envelope = (1f - progress) * (1f - progress);
        float scale = 1f;
        float shake = 0f;
        Color color = restColor;
        if (direction > 0)
        {
            scale += (gainScale - 1f) * Mathf.Cos(progress * Mathf.PI * 2.5f) * envelope;
        }
        else
        {
            shake = Mathf.Sin(progress * Mathf.PI * 5f) * lossShakeDistance * envelope;
            color = Color.Lerp(lossColor, restColor, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.16f, 1f, progress)));
        }

        numberRect.anchoredPosition = restPosition + Vector2.right * shake;
        numberRect.localScale = restScale * scale;
        number.color = color;
    }

    internal void ResetAnimation()
    {
        direction = 0;
        if (numberRect == null)
            return;
        numberRect.anchoredPosition = restPosition;
        numberRect.localScale = restScale;
        number.color = restColor;
    }

    private void OnDisable() => ResetAnimation();

    private void OnDestroy()
    {
        if (outlineMaterial != null)
            Destroy(outlineMaterial);
    }
}
