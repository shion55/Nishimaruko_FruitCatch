using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CatchableItem))]
public sealed class RareFruitSparkles : MonoBehaviour
{
    [SerializeField] private Material sparkleMaterial;

    private ParticleSystem sparkles;

    private void OnEnable()
    {
        CatchableItem item = GetComponent<CatchableItem>();
        if (item.Definition == null || item.Kind != ItemKind.Fruit || !item.Definition.RareVisual ||
            sparkleMaterial == null)
            return;

        if (sparkles == null)
        {
            Sprite sprite = item.DisplaySprite;
            SpriteRenderer renderer = GetComponentInChildren<SpriteRenderer>();
            if (sprite == null || renderer == null)
                return;
            sparkles = CreateSparkles(sprite.bounds, renderer);
        }
        sparkles.Play(false);
    }

    private ParticleSystem CreateSparkles(Bounds bounds, SpriteRenderer spriteRenderer)
    {
        var glow = new GameObject("RareFruitSparkles");
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = spriteRenderer.transform.localPosition + bounds.center;

        ParticleSystem particles = glow.AddComponent<ParticleSystem>();
        particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = 8;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.56f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.22f);
        main.startRotation = Mathf.PI * 0.25f;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.78f, 0.35f, 0.85f), new Color(1f, 0.97f, 0.73f, 0.82f));

        var emission = particles.emission;
        emission.rateOverTime = 10f;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1f;
        shape.radiusThickness = 0f;
        shape.scale = new Vector3(bounds.extents.x + 0.12f, bounds.extents.y + 0.12f, 1f);

        var fade = particles.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.22f), new GradientAlphaKey(0f, 1f) });
        fade.color = new ParticleSystem.MinMaxGradient(gradient);

        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = sparkleMaterial;
        renderer.sortingLayerID = spriteRenderer.sortingLayerID;
        renderer.sortingOrder = spriteRenderer.sortingOrder - 1;
        return particles;
    }

    private void OnDisable()
    {
        if (sparkles != null)
            sparkles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
