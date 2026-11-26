using UnityEngine;

public sealed class ItemEffectController : MonoBehaviour
{
    [SerializeField] private CharacterPairController movement;
    [SerializeField] private Collider2D carrier;
    [SerializeField] private EaterCatchController eater;
    [SerializeField] private FallingItemSpawner spawner;
    [SerializeField] private BananaPeel peelPrefab;
    [SerializeField] private Camera gameplayCamera;
    [Header("Banana")]
    [SerializeField, Range(0.1f, 0.9f)] private float landingWidthRatio = 0.3f; // 半画面幅に対する着地先の距離。
    [SerializeField, Min(0.1f)] private float throwDuration = 0.7f; // 投擲時間（秒）。
    [SerializeField, Min(0f)] private float throwHeight = 1.2f; // 弧の高さ（units）。
    [SerializeField, Min(0)] private int burstFruitCount = 12; // 壁到達時に画面全体へ追加する個数。
    [Header("Wall Impact")]
    [SerializeField, Min(0f)] private float shakeDuration = 0.35f; // 画面揺れ（秒）。
    [SerializeField, Min(0f)] private float shakeAmplitude = 0.12f; // カメラの揺れ幅（units）。

    private BananaPeel peel;
    private FruitCatchAudioManager audioManager;
    private bool running;
    private bool awaitingWall;
    private float shakeRemaining;
    private Vector3 cameraOffset;
    public bool IsChiliActive => movement != null && movement.IsChiliActive;
    public bool HasBananaPeel => peel != null;
    public float CharacterX => movement != null ? movement.transform.position.x : 0f;

    private void OnEnable()
    {
        if (eater != null) eater.ItemConsumed += HandleConsumption;
        if (movement != null) movement.ReachedBananaWall += HandleWallReached;
    }

    private void OnDisable()
    {
        if (eater != null) eater.ItemConsumed -= HandleConsumption;
        if (movement != null) movement.ReachedBananaWall -= HandleWallReached;
        StopEffects();
    }

    public void BeginRun()
    {
        StopEffects();
        audioManager = FruitCatchAudioManager.Instance;
        running = true;
    }

    private void HandleConsumption(ItemConsumption consumption)
    {
        if (!running) return;
        if (consumption.Effect == ItemEffectType.Chili && movement != null)
            movement.StartChili();
        else if (consumption.Effect == ItemEffectType.BananaPeel)
            ThrowPeel(consumption.WorldPosition);
    }

    private void ThrowPeel(Vector3 from)
    {
        if (peel != null || peelPrefab == null || carrier == null || gameplayCamera == null)
            return;
        float centerX = gameplayCamera.transform.position.x;
        int direction = Mathf.Abs(carrier.transform.position.x - centerX) < 0.01f
            ? (Random.value < 0.5f ? -1 : 1)
            : (carrier.transform.position.x < centerX ? 1 : -1);
        float halfWidth = gameplayCamera.orthographicSize * gameplayCamera.aspect;
        float x = centerX + direction * halfWidth * landingWidthRatio;
        float halfPeelHeight = peelPrefab.GetComponent<SpriteRenderer>().bounds.extents.y;
        Vector3 destination = new Vector3(x, carrier.bounds.min.y + halfPeelHeight, from.z);
        peel = Instantiate(peelPrefab, from, Quaternion.identity);
        peel.Launch(this, carrier, destination, direction, throwDuration, throwHeight);
    }

    public void StepOnPeel(BananaPeel source, int direction)
    {
        if (!running || source == null || source != peel || awaitingWall || movement == null)
            return;
        awaitingWall = true;
        movement.StartBananaSlide(direction);
        if (movement.IsSliding && movement.isActiveAndEnabled && audioManager != null)
        {
            audioManager.SetChiliSwayPlaying(false);
            audioManager.PlayBananaSlide();
        }
        Destroy(peel.gameObject);
        peel = null;
    }

    private void HandleWallReached()
    {
        if (!running || !awaitingWall) return;
        awaitingWall = false;
        if (audioManager != null)
        {
            audioManager.StopBananaSlide();
            audioManager.PlayWallImpact();
        }
        shakeRemaining = shakeDuration;
        if (spawner != null) spawner.SpawnBananaBurst(burstFruitCount);
    }

    private void LateUpdate()
    {
        if (running && audioManager != null)
        {
            audioManager.SetChiliSwayPlaying(movement != null && movement.IsChiliSwaying);
            if (awaitingWall && (movement == null || !movement.IsSliding))
                audioManager.StopBananaSlide();
        }

        RestoreCamera();
        if (!running || gameplayCamera == null || shakeRemaining <= 0f) return;
        shakeRemaining = Mathf.Max(0f, shakeRemaining - Time.deltaTime);
        float strength = shakeAmplitude * (shakeRemaining / Mathf.Max(0.01f, shakeDuration));
        cameraOffset = new Vector3(Mathf.Sin(Time.time * 90f), Mathf.Cos(Time.time * 113f), 0f) * strength;
        gameplayCamera.transform.position += cameraOffset;
    }

    private void RestoreCamera()
    {
        if (gameplayCamera != null) gameplayCamera.transform.position -= cameraOffset;
        cameraOffset = Vector3.zero;
    }

    public void StopEffects()
    {
        running = awaitingWall = false;
        if (audioManager != null) audioManager.StopItemEffectAudio();
        shakeRemaining = 0f;
        RestoreCamera();
        if (movement != null) movement.StopTemporaryEffects();
        if (peel != null)
        {
            peel.gameObject.SetActive(false);
            Destroy(peel.gameObject);
            peel = null;
        }
    }
}
