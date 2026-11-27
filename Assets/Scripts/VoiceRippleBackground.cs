using UnityEngine;

/// <summary>
/// 口元の発生位置をワールド座標で保持し、背景だけに短い波紋を描画します。
/// 音程の解釈・平滑化・マウス代替入力はPitchFallSpeedControllerに従います。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class VoiceRippleBackground : MonoBehaviour
{
    private const int MaxRipples = 8; // VoiceRipple.shaderの配列長と合わせる。
    private const int MaxWindStrokes = 96;

    [Header("References")]
    [SerializeField] private PitchFallSpeedController pitchController;
    [SerializeField] private EaterCatchController voiceController;
    [SerializeField] private Transform emissionPoint;
    [SerializeField] private FruitCatchGameSession gameSession;
    [SerializeField] private FallingItemSpawner spawner;

    [Header("Ripples")]
    [SerializeField, Min(0.1f)] private float lifetime = 0.75f;
    [SerializeField, Min(0.1f)] private float waveSpeed = 6f;
    [SerializeField, Min(0.01f)] private float waveWidth = 0.18f;
    [SerializeField, Min(0f)] private float distortion = 0.035f; // ワールド単位。
    [SerializeField, Min(0.05f)] private float lowPitchInterval = 0.25f;
    [SerializeField, Min(0.05f)] private float highPitchInterval = 0.12f;
    [SerializeField, Range(0f, 1f)] private float minimumStrength = 0.2f;
    [SerializeField] private Color ringColor = new Color(0.7f, 0.95f, 1f, 0.12f);

    [Header("Ripple Pitch Tint")]
    [SerializeField] private bool ripplePitchTintEnabled = true;
    [SerializeField] private Color lowPitchRippleColor = new Color(0.12f, 0.21f, 0.47f);
    [SerializeField] private Color highPitchRippleColor = new Color(1f, 0.46f, 0.12f);
    [SerializeField, Range(0f, 1f)] private float ripplePitchTintOpacity = 0.22f;

    [Header("Volume Size")]
    [Tooltip("今の到達半径を最小(1倍)として、最大音量での到達半径を指定する。")]
    [SerializeField, Min(1f)] private float maxRadiusMultiplier = 3f;

    [Header("Airflow")]
    [SerializeField] private bool airflowEnabled = true;
    [SerializeField] private Material airflowMaterial;
    [SerializeField] private Color windColor = new Color(0.63f, 0.92f, 1f);
    [SerializeField] private Color windAccentColor = new Color(0.94f, 0.99f, 1f);
    [SerializeField, Range(0f, 0.8f)] private float windOpacity = 0.55f;
    [SerializeField, Range(0f, 1f)] private float feverWindScale = 0.55f;
    [SerializeField, Range(0f, 24f), Tooltip("減速量が最大のときの毎秒の発生数。")] private float windDensity = 9f;
    [SerializeField, Range(0f, 24f), Tooltip("減速が起きているときの毎秒の最低発生数。Wind Density以下に制限されます。")]
    private float windMinimumEmissionRate = 2.5f;
    [SerializeField, Range(0f, 0.8f)] private float windIntervalJitter = 0.35f;
    [SerializeField, Range(0.05f, 0.8f)] private float windSpeed = 0.32f;
    [SerializeField, Range(1f, 3f), Tooltip("ピッチ範囲の上限での上昇速度の倍率。1でピッチによる速度変化がなくなります。")]
    private float windHighPitchSpeedMultiplier = 1.5f;
    [SerializeField, Range(0f, 0.6f)] private float windSpeedVariation = 0.25f;
    [SerializeField, Range(0.04f, 0.4f)] private float windLength = 0.16f;
    [SerializeField, Range(0.01f, 0.10f)] private float windWidth = 0.034f;
    [SerializeField, Min(0.01f)] private float windFadeSeconds = 0.24f;

    private struct Ripple
    {
        public Vector2 center;
        public float startedAt;
        public float strength;
        public float radiusMultiplier;
        public float pitch;
    }

    private struct WindStroke
    {
        public Vector2 center;
        public float speed;
        public float halfLength;
        public float halfWidth;
        public bool accent;
    }

    private static readonly int RipplesId = Shader.PropertyToID("_Ripples");
    private static readonly int RippleSizesId = Shader.PropertyToID("_RippleSizes");
    private static readonly int SettingsId = Shader.PropertyToID("_RippleSettings");
    private static readonly int LifetimeId = Shader.PropertyToID("_RippleLifetime");
    private static readonly int RingColorId = Shader.PropertyToID("_RippleColor");
    private static readonly int LowPitchRippleColorId = Shader.PropertyToID("_LowPitchRippleColor");
    private static readonly int HighPitchRippleColorId = Shader.PropertyToID("_HighPitchRippleColor");
    private static readonly int RipplePitchTintOpacityId = Shader.PropertyToID("_RipplePitchTintOpacity");
    private static readonly int UVRectId = Shader.PropertyToID("_SpriteUVRect");
    private static readonly int UVPerLocalUnitId = Shader.PropertyToID("_UVPerLocalUnit");
    private readonly Ripple[] ripples = new Ripple[MaxRipples];
    private readonly Vector4[] shaderRipples = new Vector4[MaxRipples];
    private readonly Vector4[] shaderRippleSizes = new Vector4[MaxRipples];
    private readonly System.Random windRandom = new System.Random();
    private readonly WindStroke[] windStrokes = new WindStroke[MaxWindStrokes];
    private readonly Vector3[] windVertices = new Vector3[MaxWindStrokes * 4];
    private readonly Color[] windColors = new Color[MaxWindStrokes * 4];
    private readonly int[] windTriangles = new int[MaxWindStrokes * 6];
    private SpriteRenderer background;
    private MaterialPropertyBlock properties;
    private Sprite cachedSprite;
    private Vector4 uvRect;
    private Vector2 uvPerLocalUnit;
    private int nextRipple;
    private float timeUntilNextRipple;
    private float windEmissionRate;
    private float windPitch;
    private float windVisibility;
    private float windEmissionProgress;
    private float nextWindEmission;
    private int windStrokeCount;
    private Camera windCamera;
    private Mesh windMesh;
    private MeshRenderer windRenderer;

    private void OnEnable()
    {
        background = GetComponent<SpriteRenderer>();
        if (properties == null)
            properties = new MaterialPropertyBlock();
        if (airflowMaterial != null && windMesh == null)
            CreateWindMesh();
        windCamera = Camera.main;
        ClearRipples();
        ClearWind();
        UploadRipples();
    }

    private void LateUpdate()
    {
        // 開始前・結果画面では歪みを残さない。Time.timeでポーズ中は波も止まる。
        if (gameSession != null && !gameSession.IsPlaying)
        {
            ClearRipples();
            ClearWind();
            UploadRipples();
            return;
        }

        bool hasPitch = pitchController != null && pitchController.isActiveAndEnabled && pitchController.HasPitchInput;
        UpdateWind(hasPitch);

        bool emitting = FruitCatchOperatorSettings.Enabled("visual.ripples") && hasPitch && emissionPoint != null;
        if (emitting && Time.deltaTime > 0f)
        {
            timeUntilNextRipple -= Time.deltaTime;
            if (timeUntilNextRipple <= 0f)
            {
                float strength = pitchController.SlowdownStrength;
                ripples[nextRipple] = new Ripple
                {
                    center = emissionPoint.position,
                    startedAt = Time.time,
                    strength = Mathf.Lerp(minimumStrength, 1f, strength),
                    pitch = pitchController.NormalizedPitch,
                    // 発生後に音量が変わっても、この波の大きさは変えない。
                    radiusMultiplier = Mathf.Lerp(1f, Mathf.Max(1f, maxRadiusMultiplier),
                        voiceController != null ? voiceController.NormalizedVoiceVolume : 0f)
                };
                nextRipple = (nextRipple + 1) % MaxRipples;
                timeUntilNextRipple = Mathf.Max(0.05f,
                    Mathf.Lerp(lowPitchInterval, highPitchInterval, strength));
            }
        }
        else if (!emitting)
        {
            // 次の発声にはすぐ反応し、発生済みの波だけ自然に消す。
            timeUntilNextRipple = 0f;
        }

        UploadRipples();
    }

    private void UploadRipples()
    {
        if (background == null || background.sprite == null)
            return;

        if (cachedSprite != background.sprite)
            CacheSpriteCoordinates();

        float duration = Mathf.Max(0.1f, lifetime);
        for (int i = 0; i < MaxRipples; i++)
        {
            Ripple ripple = ripples[i];
            float age = Mathf.Max(0f, Time.time - ripple.startedAt);
            shaderRipples[i] = new Vector4(ripple.center.x, ripple.center.y, age,
                FruitCatchOperatorSettings.Enabled("visual.ripples") && age < duration ? ripple.strength : 0f);
            shaderRippleSizes[i] = new Vector4(Mathf.Max(1f, ripple.radiusMultiplier), ripple.pitch, 0f, 0f);
        }

        background.GetPropertyBlock(properties);
        properties.SetVectorArray(RipplesId, shaderRipples);
        properties.SetVectorArray(RippleSizesId, shaderRippleSizes);
        properties.SetVector(SettingsId, new Vector4(Mathf.Max(0.1f, waveSpeed),
            Mathf.Max(0.01f, waveWidth), Mathf.Max(0f, distortion), 0f));
        properties.SetFloat(LifetimeId, duration);
        properties.SetColor(RingColorId, ringColor);
        properties.SetColor(LowPitchRippleColorId, lowPitchRippleColor);
        properties.SetColor(HighPitchRippleColorId, highPitchRippleColor);
        properties.SetFloat(RipplePitchTintOpacityId, ripplePitchTintEnabled ? ripplePitchTintOpacity : 0f);
        properties.SetVector(UVRectId, uvRect);
        properties.SetVector(UVPerLocalUnitId, new Vector4(
            uvPerLocalUnit.x * (background.flipX ? -1f : 1f),
            uvPerLocalUnit.y * (background.flipY ? -1f : 1f), 0f, 0f));
        background.SetPropertyBlock(properties);
    }

    private void CreateWindMesh()
    {
        var child = new GameObject("Airflow");
        child.transform.SetParent(transform, false);
        child.layer = gameObject.layer;
        windRenderer = child.AddComponent<MeshRenderer>();
        windRenderer.sharedMaterial = airflowMaterial;
        windRenderer.sortingLayerID = background.sortingLayerID;
        windRenderer.sortingOrder = background.sortingOrder + 1;

        windMesh = new Mesh { name = "Airflow Strokes" };
        windMesh.MarkDynamic();
        child.AddComponent<MeshFilter>().sharedMesh = windMesh;

        var uvs = new Vector2[windVertices.Length];
        for (int i = 0; i < MaxWindStrokes; i++)
        {
            int v = i * 4;
            uvs[v] = Vector2.zero;
            uvs[v + 1] = Vector2.right;
            uvs[v + 2] = Vector2.one;
            uvs[v + 3] = Vector2.up;
            int t = i * 6;
            windTriangles[t] = windTriangles[t + 3] = v;
            windTriangles[t + 1] = v + 1;
            windTriangles[t + 2] = windTriangles[t + 4] = v + 2;
            windTriangles[t + 5] = v + 3;
        }
        windMesh.SetVertices(windVertices);
        windMesh.SetUVs(0, uvs);
    }

    private void UpdateWind(bool hasPitch)
    {
        if (!airflowEnabled || windRenderer == null || windCamera == null)
        {
            ClearWind();
            return;
        }

        float dt = Time.deltaTime;
        float slowdown = hasPitch && spawner != null ? spawner.GetAveragePitchSlowdown() : 0f;
        float feverScale = spawner != null && spawner.IsFever ? feverWindScale : 1f;
        bool emitting = slowdown * feverScale > 0.001f && windDensity > 0f;
        float minimumRate = Mathf.Min(windMinimumEmissionRate, windDensity);
        float targetRate = emitting ? Mathf.Max(windDensity * slowdown, minimumRate) * feverScale : 0f;
        float blend = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, windFadeSeconds));
        windEmissionRate = Mathf.Lerp(windEmissionRate, targetRate, blend);
        windVisibility = Mathf.Lerp(windVisibility, emitting ? 1f : 0f, blend);
        if (hasPitch)
            windPitch = pitchController.NormalizedPitch;
        if (!emitting)
        {
            windEmissionProgress = 0f;
            if (windVisibility < 0.01f)
            {
                ClearWind();
                return;
            }
        }

        float depth = Vector3.Dot(background.bounds.center - windCamera.transform.position, windCamera.transform.forward);
        Vector3 lower = windCamera.ViewportToWorldPoint(new Vector3(0f, 0f, depth));
        Vector3 upper = windCamera.ViewportToWorldPoint(new Vector3(1f, 1f, depth));
        float height = upper.y - lower.y;
        if (height <= 0f || upper.x <= lower.x)
        {
            ClearWind();
            return;
        }

        float riseStep = dt * Mathf.Lerp(1f, windHighPitchSpeedMultiplier, windPitch);
        for (int i = windStrokeCount - 1; i >= 0; i--)
        {
            windStrokes[i].center.y += windStrokes[i].speed * riseStep;
            if (windStrokes[i].center.y >= upper.y + height * 0.04f)
                windStrokes[i] = windStrokes[--windStrokeCount];
        }

        if (emitting && dt > 0f)
        {
            windEmissionProgress += dt * windEmissionRate;
            while (windEmissionProgress >= nextWindEmission)
            {
                windEmissionProgress -= nextWindEmission;
                if (windStrokeCount < MaxWindStrokes)
                    EmitWindStroke(lower, upper);
                nextWindEmission = RandomWindRange(1f - windIntervalJitter, 1f + windIntervalJitter);
            }
        }

        DrawWind(lower.y, height, lower.z);
    }

    private void EmitWindStroke(Vector3 lower, Vector3 upper)
    {
        float height = upper.y - lower.y;
        float width = upper.x - lower.x;
        bool accent = windRandom.NextDouble() < 0.4;
        float halfLength = 0.5f * height * windLength * RandomWindRange(0.76f, 1.24f) * (accent ? 0.7f : 1f);
        float halfWidth = width * windWidth / 13f * RandomWindRange(0.7f, 1.2f) * (accent ? 0.82f : 1f);
        float speed = height * windSpeed * RandomWindRange(1f - windSpeedVariation, 1f + windSpeedVariation);
        float x = lower.x;
        float bestGap = -1f;
        // Prefer room beside newly emitted strokes, with random candidates across the viewport.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            float candidate = RandomWindRange(lower.x + halfWidth, upper.x - halfWidth);
            float gap = width;
            for (int i = 0; i < windStrokeCount; i++)
                if (windStrokes[i].center.y <= lower.y + height * 0.28f)
                    gap = Mathf.Min(gap, Mathf.Abs(candidate - windStrokes[i].center.x));
            if (gap > bestGap)
            {
                x = candidate;
                bestGap = gap;
            }
            if (gap >= width * 0.065f)
                break;
        }
        windStrokes[windStrokeCount++] = new WindStroke
        {
            center = new Vector2(x, lower.y - halfLength),
            speed = speed,
            halfLength = halfLength,
            halfWidth = halfWidth,
            accent = accent
        };
    }

    private void DrawWind(float bottom, float height, float worldZ)
    {
        windRenderer.enabled = windStrokeCount > 0;
        if (windStrokeCount == 0)
            return;

        Matrix4x4 worldToLocal = windRenderer.transform.worldToLocalMatrix;
        for (int i = 0; i < windStrokeCount; i++)
        {
            WindStroke stroke = windStrokes[i];
            float x = stroke.center.x;
            float y = stroke.center.y;
            float w = stroke.halfWidth * 1.12f; // Reserve a small border for silhouette antialiasing.
            float h = stroke.halfLength;
            int v = i * 4;
            windVertices[v] = worldToLocal.MultiplyPoint3x4(new Vector3(x - w, y - h, worldZ));
            windVertices[v + 1] = worldToLocal.MultiplyPoint3x4(new Vector3(x + w, y - h, worldZ));
            windVertices[v + 2] = worldToLocal.MultiplyPoint3x4(new Vector3(x + w, y + h, worldZ));
            windVertices[v + 3] = worldToLocal.MultiplyPoint3x4(new Vector3(x - w, y + h, worldZ));

            float progress = (y - bottom) / height;
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.04f, 0.17f, progress))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1.04f, progress)));
            Color tint = stroke.accent ? windAccentColor : windColor;
            tint.a *= windOpacity * windVisibility * fade * 0.75f;
            windColors[v] = windColors[v + 1] = windColors[v + 2] = windColors[v + 3] = tint;
        }

        windMesh.SetVertices(windVertices);
        windMesh.SetColors(windColors);
        windMesh.SetTriangles(windTriangles, 0, windStrokeCount * 6, 0);
    }

    private float RandomWindRange(float min, float max) => Mathf.Lerp(min, max, (float)windRandom.NextDouble());

    private void ClearWind()
    {
        windStrokeCount = 0;
        windEmissionRate = windVisibility = windPitch = 0f;
        windEmissionProgress = 0f;
        nextWindEmission = 0f;
        if (windRenderer != null)
            windRenderer.enabled = false;
    }

    private void CacheSpriteCoordinates()
    {
        cachedSprite = background.sprite;
        Vector2 min = Vector2.one;
        Vector2 max = Vector2.zero;
        foreach (Vector2 uv in cachedSprite.uv)
        {
            min = Vector2.Min(min, uv);
            max = Vector2.Max(max, uv);
        }
        Vector2 size = max - min;
        uvRect = new Vector4(min.x, min.y, size.x, size.y);
        Vector3 localSize = cachedSprite.bounds.size;
        uvPerLocalUnit = new Vector2(size.x / Mathf.Max(0.0001f, localSize.x),
            size.y / Mathf.Max(0.0001f, localSize.y));
    }

    private void ClearRipples()
    {
        System.Array.Clear(ripples, 0, ripples.Length);
        nextRipple = 0;
        timeUntilNextRipple = 0f;
    }

    private void OnDisable()
    {
        ClearRipples();
        ClearWind();
        UploadRipples();
    }

    private void OnDestroy()
    {
        if (windRenderer != null)
            Destroy(windRenderer.gameObject);
        if (windMesh != null)
            Destroy(windMesh);
    }
}
