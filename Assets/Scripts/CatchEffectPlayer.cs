
using System.Collections;
using UnityEngine;

public class CatchEffectPlayer : MonoBehaviour
{
    [Header("Effect Prefabs")]
    [SerializeField] private GameObject fruitEffectPrefab;
    [SerializeField] private GameObject bugEffectPrefab;

    [Header("Eating Particles")]
    [SerializeField] private ParticleSystem eatingParticles;
    [SerializeField, Min(1)] private int eatingParticleCount = 10;

    [Header("Bounce Particles")]
    // 色・大きさ・上向きの広がり・寿命は子のParticleSystemで調整する。
    [SerializeField] private ParticleSystem bounceParticles;
    [SerializeField, Min(1)] private int bounceParticleCount = 8; // 弾き上げ1回あたりの粒数。

    [Header("Position")]
    [SerializeField] private Transform headPoint;
    [SerializeField] private Transform effectParent;

    [Header("Arrangement")]
    [SerializeField] private int effectCount = 7;
    [SerializeField] private float radius = 0.6f;
    [SerializeField] private float minSpacing = 0.3f;
    [SerializeField] private float startAngle = 20f;
    [SerializeField] private float endAngle = 160f;

    [Header("Rotation")]
    [SerializeField] private float rotationOffset = 0f;

    [Header("Animation")]
    [SerializeField] private float effectLifetime = 0.5f;

    private GameObject activeGroup;
    private Coroutine activeCoroutine;

    public void PlayFruitEffect(Vector3 worldPosition)
    {
        if (!isActiveAndEnabled)
            return;

        EmitParticles(eatingParticles, worldPosition, Quaternion.identity, eatingParticleCount);
        SpawnEffects(fruitEffectPrefab);
    }

    public void PlayBugEffect()
    {
        SpawnEffects(bugEffectPrefab);
    }

    public void PlayBounceEffect(Vector3 worldPosition, bool isLeft)
    {
        if (!isActiveAndEnabled)
            return;

        EmitParticles(bounceParticles, worldPosition,
            Quaternion.Euler(0f, isLeft ? 180f : 0f, 0f), bounceParticleCount);
    }

    private static void EmitParticles(ParticleSystem particles, Vector3 worldPosition, Quaternion rotation, int count)
    {
        if (particles == null)
            return;

        // World空間で再利用し、移動や連続取得でも放出済みの粒を動かしたり消したりしない。
        particles.transform.SetPositionAndRotation(worldPosition, rotation);
        if (!particles.isPlaying)
            particles.Play();
        particles.Emit(Mathf.Max(1, count));
    }

    private void SpawnEffects(GameObject prefab)
    {
        if (prefab == null) return;

        // 前のエフェクトを削除
        ClearEffects();

        Transform parent = effectParent != null
            ? effectParent
            : (headPoint != null ? headPoint : transform);

        // エフェクトをまとめる親を生成
        activeGroup = new GameObject("ActiveCatchEffects");
        activeGroup.transform.SetParent(parent, false);

        if (headPoint != null)
        {
            activeGroup.transform.position = headPoint.position;
            activeGroup.transform.rotation = headPoint.rotation;
        }

        int count = Mathf.Max(1, effectCount);

        float angleStep = count > 1
            ? Mathf.Abs(endAngle - startAngle) / (count - 1)
            : 0f;

        // 角度差がほぼゼロなら1個だけ生成
        if (count > 1 && angleStep < 0.01f)
            count = 1;

        // 最低間隔を守るように半径を調整
        float actualRadius = Mathf.Max(0f, radius);

        if (count > 1)
        {
            float stepRad = angleStep * Mathf.Deg2Rad;
            float chordFactor = 2f * Mathf.Sin(stepRad * 0.5f);

            if (chordFactor > 0.0001f)
            {
                float requiredRadius =
                    Mathf.Max(0f, minSpacing) / chordFactor;

                actualRadius = Mathf.Max(
                    actualRadius,
                    requiredRadius
                );
            }
        }

        for (int i = 0; i < count; i++)
        {
            float t = count == 1
                ? 0.5f
                : (float)i / (count - 1);

            float angle = Mathf.Lerp(
                startAngle, endAngle, t
            );

            float rad = angle * Mathf.Deg2Rad;

            Vector3 direction = new Vector3(
                Mathf.Cos(rad),
                Mathf.Sin(rad),
                0f
            );

            GameObject effect = Instantiate(
                prefab,
                activeGroup.transform
            );

            effect.transform.localPosition =
                direction * actualRadius;

            effect.transform.localRotation =
                Quaternion.Euler(
                    0f, 0f, angle + rotationOffset
                );
        }

        // フェードアウト開始
        activeCoroutine = StartCoroutine(
            FadeOut(activeGroup)
        );
    }

    private IEnumerator FadeOut(GameObject group)
    {
        SpriteRenderer[] renderers =
            group.GetComponentsInChildren<SpriteRenderer>(true);

        Color[] originalColors = new Color[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            originalColors[i] = renderers[i].color;
        }

        float duration = Mathf.Max(0.01f, effectLifetime);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float alpha = 1f - Mathf.Clamp01(elapsed / duration);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;

                Color color = originalColors[i];
                color.a *= alpha;

                renderers[i].color = color;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(group);

        if (activeGroup == group)
        {
            activeGroup = null;
            activeCoroutine = null;
        }
    }

    private void ClearEffects()
    {
        if (activeCoroutine != null)
        {
            StopCoroutine(activeCoroutine);
            activeCoroutine = null;
        }

        if (activeGroup != null)
        {
            Destroy(activeGroup);
            activeGroup = null;
        }
    }

    private void OnDisable()
    {
        ClearEffects();
        if (eatingParticles != null)
            eatingParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (bounceParticles != null)
            bounceParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
