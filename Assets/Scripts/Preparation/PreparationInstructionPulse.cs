using UnityEngine;

namespace FruitCatch.Preparation
{
    public sealed class PreparationInstructionPulse : MonoBehaviour
    {
        [SerializeField, Range(0f, 0.15f)] private float scaleAmount = 0.035f;
        [SerializeField, Min(0.1f)] private float cycleSeconds = 1.2f;

        private Vector3 baseScale;

        private void OnEnable()
        {
            baseScale = transform.localScale;
        }

        private void Update()
        {
            float phase = Time.unscaledTime * (Mathf.PI * 2f / Mathf.Max(0.1f, cycleSeconds));
            float amount = 1f + scaleAmount * (0.5f + 0.5f * Mathf.Sin(phase));
            transform.localScale = baseScale * amount;
        }

        private void OnDisable()
        {
            transform.localScale = baseScale;
        }
    }
}
