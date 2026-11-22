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

    [Header("References")]
    [SerializeField] private PitchFallSpeedController pitchController;
    [SerializeField] private EaterCatchController voiceController;
    [SerializeField] private Transform emissionPoint;
    [SerializeField] private FruitCatchGameSession gameSession;

    [Header("Ripples")]
    [SerializeField, Min(0.1f)] private float lifetime = 0.75f;
    [SerializeField, Min(0.1f)] private float waveSpeed = 6f;
    [SerializeField, Min(0.01f)] private float waveWidth = 0.18f;
    [SerializeField, Min(0f)] private float distortion = 0.035f; // ワールド単位。
    [SerializeField, Min(0.05f)] private float lowPitchInterval = 0.25f;
    [SerializeField, Min(0.05f)] private float highPitchInterval = 0.12f;
    [SerializeField, Range(0f, 1f)] private float minimumStrength = 0.2f;
    [SerializeField] private Color ringColor = new Color(0.7f, 0.95f, 1f, 0.12f);

    [Header("Volume Size")]
    [Tooltip("今の到達半径を最小(1倍)として、最大音量での到達半径を指定する。")]
    [SerializeField, Min(1f)] private float maxRadiusMultiplier = 3f;

    private struct Ripple
    {
        public Vector2 center;
        public float startedAt;
        public float strength;
        public float radiusMultiplier;
    }

    private static readonly int RipplesId = Shader.PropertyToID("_Ripples");
    private static readonly int RippleSizesId = Shader.PropertyToID("_RippleSizes");
    private static readonly int SettingsId = Shader.PropertyToID("_RippleSettings");
    private static readonly int LifetimeId = Shader.PropertyToID("_RippleLifetime");
    private static readonly int RingColorId = Shader.PropertyToID("_RippleColor");
    private static readonly int UVRectId = Shader.PropertyToID("_SpriteUVRect");
    private static readonly int UVPerLocalUnitId = Shader.PropertyToID("_UVPerLocalUnit");

    private readonly Ripple[] ripples = new Ripple[MaxRipples];
    private readonly Vector4[] shaderRipples = new Vector4[MaxRipples];
    private readonly Vector4[] shaderRippleSizes = new Vector4[MaxRipples];
    private SpriteRenderer background;
    private MaterialPropertyBlock properties;
    private Sprite cachedSprite;
    private Vector4 uvRect;
    private Vector2 uvPerLocalUnit;
    private int nextRipple;
    private float timeUntilNextRipple;

    private void OnEnable()
    {
        background = GetComponent<SpriteRenderer>();
        if (properties == null)
            properties = new MaterialPropertyBlock();
        ClearRipples();
        UploadRipples();
    }

    private void LateUpdate()
    {
        // 開始前・結果画面では歪みを残さない。Time.timeでポーズ中は波も止まる。
        if (gameSession != null && !gameSession.IsPlaying)
        {
            ClearRipples();
            UploadRipples();
            return;
        }

        bool emitting = pitchController != null && pitchController.isActiveAndEnabled &&
            pitchController.HasPitchInput && emissionPoint != null;

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
                age < duration ? ripple.strength : 0f);
            shaderRippleSizes[i] = new Vector4(Mathf.Max(1f, ripple.radiusMultiplier), 0f, 0f, 0f);
        }

        background.GetPropertyBlock(properties);
        properties.SetVectorArray(RipplesId, shaderRipples);
        properties.SetVectorArray(RippleSizesId, shaderRippleSizes);
        properties.SetVector(SettingsId, new Vector4(Mathf.Max(0.1f, waveSpeed),
            Mathf.Max(0.01f, waveWidth), Mathf.Max(0f, distortion), 0f));
        properties.SetFloat(LifetimeId, duration);
        properties.SetColor(RingColorId, ringColor);
        properties.SetVector(UVRectId, uvRect);
        properties.SetVector(UVPerLocalUnitId, new Vector4(
            uvPerLocalUnit.x * (background.flipX ? -1f : 1f),
            uvPerLocalUnit.y * (background.flipY ? -1f : 1f), 0f, 0f));
        background.SetPropertyBlock(properties);
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
        UploadRipples();
    }
}
