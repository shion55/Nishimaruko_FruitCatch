using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class FruitSpriteRotation : MonoBehaviour
{
    [SerializeField, Min(0f)] private float minDegreesPerSecond = 30f;
    [SerializeField, Min(0f)] private float maxDegreesPerSecond = 90f;

    // Keep visual randomness independent of the spawner's random sequence.
    private static readonly System.Random rotationRandom = new System.Random();
    private float degreesPerSecond;

    private void OnEnable()
    {
        float min = Mathf.Max(0f, Mathf.Min(minDegreesPerSecond, maxDegreesPerSecond));
        float max = Mathf.Max(min, Mathf.Max(minDegreesPerSecond, maxDegreesPerSecond));
        float direction = rotationRandom.Next(2) == 0 ? -1f : 1f;
        degreesPerSecond = direction * Mathf.Lerp(min, max, (float)rotationRandom.NextDouble());
        transform.localRotation = Quaternion.Euler(0f, 0f, (float)rotationRandom.NextDouble() * 360f);
    }

    private void Update()
    {
        transform.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
