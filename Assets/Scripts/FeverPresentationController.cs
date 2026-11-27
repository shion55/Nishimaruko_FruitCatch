using UnityEngine;

/// <summary>フィーバー中の粒子、残像、全画面のオーロラを制御します。</summary>
[DisallowMultipleComponent]
public sealed class FeverPresentationController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private FallingItemSpawner spawner;
    [SerializeField] private EaterCatchController eater;
    [SerializeField] private CatchEffectPlayer catchEffectPlayer;
    [SerializeField] private SpriteRenderer eaterRenderer;
    [SerializeField] private SpriteRenderer carrierRenderer;
    [SerializeField] private MeshRenderer auroraRenderer;
    [SerializeField] private Material sparkleMaterial;
    [SerializeField] private Material dustMaterial;

    [Header("Gold Sparkles")]
    [SerializeField] private Color goldColor = new Color(1f, 0.76f, 0.18f);
    [SerializeField] private Color lightGoldColor = new Color(1f, 0.94f, 0.55f);
    [SerializeField, Min(0.02f)] private float sparkleInterval = 0.06f;
    [SerializeField, Min(1)] private int startSparkleCount = 28;
    [SerializeField, Min(1)] private int catchSparkleCount = 10;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveThreshold = 0.05f;
    [SerializeField, Min(0.03f)] private float trailInterval = 0.08f;
    [SerializeField, Range(0f, 0.5f)] private float afterimageAlpha = 0.18f;
    [SerializeField, Range(0.05f, 0.3f)] private float afterimageLifetime = 0.24f;

    [Header("Aurora")]
    [SerializeField, Range(0f, 0.55f)] private float auroraOpacity = 0.42f;
    [SerializeField, Min(0.01f)] private float fadeInSeconds = 0.42f;
    [SerializeField, Min(0.01f)] private float fadeOutSeconds = 0.9f;

    private static readonly int AuroraAlphaId = Shader.PropertyToID("_AuroraAlpha");
    private readonly SpriteRenderer[] afterimages = new SpriteRenderer[4];
    private readonly float[] afterimageAges = new float[4];
    private ParticleSystem auraParticles;
    private ParticleSystem catchParticles;
    private ParticleSystem dustParticles;
    private Mesh starMesh;
    private Mesh auroraMesh;
    private Camera auroraCamera;
    private MaterialPropertyBlock auroraProperties;
    private float auroraFade;
    private Vector3 previousCarrierPosition;
    private bool presenting;
    private float sparkleTimer;
    private float trailTimer;
    private int nextAfterimage;

    private bool IsFever => spawner != null && spawner.IsFever;
    private bool EatingEffectsEnabled => catchEffectPlayer == null || catchEffectPlayer.EatingEffectsEnabled;

    private void Awake()
    {
        if (auroraRenderer != null)
        {
            auroraCamera = auroraRenderer.GetComponentInParent<Camera>();
            auroraProperties = new MaterialPropertyBlock();
            auroraMesh = new Mesh
            {
                name = "Fever Aurora Quad",
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
                    new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f)
                },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            auroraRenderer.GetComponent<MeshFilter>().sharedMesh = auroraMesh;
            auroraRenderer.enabled = false;
        }

        // 生成は起動時の一度だけ。イベントごとにGameObjectやMaterialを増やさない。
        starMesh = CreateStarMesh();
        auraParticles = CreateParticles("FeverAura", sparkleMaterial, 64, true);
        catchParticles = CreateParticles("FeverCatchStars", sparkleMaterial, 192, true);
        dustParticles = CreateParticles("FeverFootDust", dustMaterial, 48, false);
        for (int i = 0; i < afterimages.Length; i++)
        {
            var ghost = new GameObject("CarrierAfterimage");
            ghost.transform.SetParent(transform, false);
            afterimages[i] = ghost.AddComponent<SpriteRenderer>();
            afterimages[i].enabled = false;
        }
    }

    private void OnEnable()
    {
        if (eater != null)
            eater.ItemConsumed += HandleConsumption;
        if (carrierRenderer != null)
            previousCarrierPosition = carrierRenderer.transform.position;
    }

    private void LateUpdate()
    {
        Vector3 position = carrierRenderer != null ? carrierRenderer.transform.position : Vector3.zero;
        float speed = Mathf.Abs(position.x - previousCarrierPosition.x) / Mathf.Max(Time.deltaTime, 0.0001f);
        previousCarrierPosition = position;

        bool fever = IsFever;
        float fadeSeconds = fever ? fadeInSeconds : fadeOutSeconds;
        auroraFade = Mathf.MoveTowards(auroraFade, fever ? 1f : 0f,
            Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));
        UpdateAurora();

        if (!fever)
        {
            if (presenting)
                StopPresentation();
            return;
        }
        if (!presenting)
            StartPresentation();
        if (!EatingEffectsEnabled && catchParticles.isPlaying)
            catchParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (Time.deltaTime <= 0f)
            return;

        sparkleTimer -= Time.deltaTime;
        if (sparkleTimer <= 0f)
        {
            EmitAura(1);
            sparkleTimer = Mathf.Max(0.02f, sparkleInterval);
        }

        trailTimer = Mathf.Max(0f, trailTimer - Time.deltaTime);
        if (speed > moveThreshold && trailTimer <= 0f && carrierRenderer != null)
        {
            EmitMovementTrail(position.x);
            trailTimer = Mathf.Max(0.03f, trailInterval);
        }
        FadeAfterimages();
    }

    private void StartPresentation()
    {
        presenting = true;
        sparkleTimer = trailTimer = 0f;
        auraParticles.Play();
        dustParticles.Play();
        EmitAura(Mathf.Max(1, startSparkleCount));
    }

    private void HandleConsumption(ItemConsumption consumption)
    {
        if (!IsFever)
            return;
        // フィーバー果物を取った瞬間から反応する。
        if (!presenting)
            StartPresentation();
        if (consumption.Category != ItemCategory.Fruit || !EatingEffectsEnabled)
            return;

        if (!catchParticles.isPlaying)
            catchParticles.Play();
        Vector3 origin = eaterRenderer != null ? eaterRenderer.bounds.center : consumption.WorldPosition;
        for (int i = 0; i < Mathf.Max(1, catchSparkleCount); i++)
        {
            float angle = Random.Range(15f, 165f) * Mathf.Deg2Rad;
            Vector3 velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * Random.Range(1.1f, 2.5f);
            EmitStar(catchParticles, origin, velocity, Random.Range(0.12f, 0.22f));
        }
    }

    private void EmitAura(int count)
    {
        if (eaterRenderer == null)
            return;
        Bounds bounds = eaterRenderer.bounds;
        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 offset = new Vector3(Mathf.Cos(angle) * (bounds.extents.x + 0.18f),
                Mathf.Sin(angle) * (bounds.extents.y + 0.12f), 0f);
            EmitStar(auraParticles, bounds.center + offset,
                new Vector3(offset.x * 0.15f, Random.Range(0.2f, 0.65f), 0f), Random.Range(0.1f, 0.19f));
        }
    }

    private void EmitStar(ParticleSystem particles, Vector3 position, Vector3 velocity, float size)
    {
        var emit = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = velocity,
            startSize = size,
            startLifetime = Random.Range(0.4f, 0.7f),
            startColor = Color.Lerp(goldColor, lightGoldColor, Random.value),
            rotation = Random.Range(-25f, 25f)
        };
        particles.Emit(emit, 1);
    }

    private void EmitMovementTrail(float x)
    {
        Bounds bounds = carrierRenderer.bounds;
        for (int i = 0; i < 3; i++)
        {
            var dust = new ParticleSystem.EmitParams
            {
                position = new Vector3(x + Random.Range(-0.1f, 0.1f), bounds.min.y + 0.04f,
                    carrierRenderer.transform.position.z),
                velocity = new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.1f, 0.4f), 0f),
                startSize = Random.Range(0.18f, 0.3f),
                startLifetime = 0.35f,
                startColor = new Color(1f, 0.83f, 0.48f, 0.4f)
            };
            dustParticles.Emit(dust, 1);
        }

        SpriteRenderer ghost = afterimages[nextAfterimage];
        ghost.sprite = carrierRenderer.sprite;
        ghost.sharedMaterial = carrierRenderer.sharedMaterial;
        ghost.flipX = carrierRenderer.flipX;
        ghost.flipY = carrierRenderer.flipY;
        ghost.sortingLayerID = auroraRenderer != null ? auroraRenderer.sortingLayerID : carrierRenderer.sortingLayerID;
        ghost.sortingOrder = auroraRenderer != null ? auroraRenderer.sortingOrder + 1 : carrierRenderer.sortingOrder - 1;
        ghost.transform.SetPositionAndRotation(carrierRenderer.transform.position, carrierRenderer.transform.rotation);
        ghost.transform.localScale = carrierRenderer.transform.lossyScale;
        ghost.enabled = true;
        afterimageAges[nextAfterimage] = 0f;
        nextAfterimage = (nextAfterimage + 1) % afterimages.Length;
    }

    private void FadeAfterimages()
    {
        for (int i = 0; i < afterimages.Length; i++)
        {
            if (!afterimages[i].enabled)
                continue;
            afterimageAges[i] += Time.deltaTime;
            float t = Mathf.Clamp01(afterimageAges[i] / Mathf.Max(0.05f, afterimageLifetime));
            Color color = lightGoldColor;
            color.a = afterimageAlpha * (1f - t);
            afterimages[i].color = color;
            afterimages[i].enabled = t < 1f;
        }
    }

    private ParticleSystem CreateParticles(string objectName, Material material, int capacity, bool stars)
    {
        var child = new GameObject(objectName);
        child.transform.SetParent(transform, false);
        ParticleSystem particles = child.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = true; // 自動放出は無効。フィーバー中だけ手動Emitをシミュレーションする。
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = capacity;
        main.startSpeed = 0f;
        main.gravityModifier = stars ? 0.08f : 0f;
        var emission = particles.emission;
        emission.enabled = false;
        var shape = particles.shape;
        shape.enabled = false;
        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
        var color = particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingLayerID = auroraRenderer != null ? auroraRenderer.sortingLayerID
            : eaterRenderer != null ? eaterRenderer.sortingLayerID : 0;
        renderer.sortingOrder = auroraRenderer != null ? auroraRenderer.sortingOrder + 1 : 4;
        if (stars)
        {
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = starMesh;
        }
        return particles;
    }

    private void UpdateAurora()
    {
        if (auroraRenderer == null || auroraCamera == null)
            return;
        float alpha = FruitCatchOperatorSettings.Enabled("visual.rainbow")
            ? auroraOpacity * Mathf.SmoothStep(0f, 1f, auroraFade) : 0f;
        auroraRenderer.enabled = alpha > 0f;
        if (alpha <= 0f)
            return;

        float height = auroraCamera.orthographicSize * 2f;
        auroraRenderer.transform.localScale = new Vector3(height * auroraCamera.aspect, height, 1f);
        auroraProperties.SetFloat(AuroraAlphaId, alpha);
        auroraRenderer.SetPropertyBlock(auroraProperties);
    }

    private void StopPresentation()
    {
        presenting = false;
        foreach (ParticleSystem particles in new[] { auraParticles, catchParticles, dustParticles })
            if (particles != null)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (SpriteRenderer ghost in afterimages)
            if (ghost != null)
                ghost.enabled = false;
    }

    private void OnDisable()
    {
        if (eater != null)
            eater.ItemConsumed -= HandleConsumption;
        StopPresentation();
        auroraFade = 0f;
        UpdateAurora();
    }

    private void OnDestroy()
    {
        if (starMesh != null)
            Destroy(starMesh);
        if (auroraMesh != null)
            Destroy(auroraMesh);
    }

    private static Mesh CreateStarMesh()
    {
        var vertices = new Vector3[9];
        var uv = new Vector2[9];
        var colors = new Color[9];
        var triangles = new int[24];
        uv[0] = Vector2.one * 0.5f;
        colors[0] = Color.white;
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI / 4f;
            float radius = i % 2 == 0 ? 0.5f : 0.14f;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            uv[i + 1] = (Vector2)vertices[i + 1] + Vector2.one * 0.5f;
            colors[i + 1] = Color.white;
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = (i + 1) % 8 + 1;
        }
        var mesh = new Mesh { name = "FeverFourPointStar", vertices = vertices, uv = uv, colors = colors, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

}
