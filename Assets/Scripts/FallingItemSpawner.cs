
using UnityEngine;

public class FallingItemSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject fruitPrefab;
    [SerializeField] private GameObject bugPrefab;
    [SerializeField] private GameObject feverItemPrefab;

    [Header("Normal")]
    [Range(0f, 1f)]
    [SerializeField] private float bugProbability = 0.2f;

    [SerializeField] private float minFallSpeed = 2f;
    [SerializeField] private float maxFallSpeed = 4f;

    [Header("Calm Phase")]
    [SerializeField]
    private Vector2 calmDuration =
        new Vector2(5f, 8f);

    [SerializeField]
    private Vector2 calmInterval =
        new Vector2(0.9f, 1.4f);

    [Header("Rush Phase")]
    [SerializeField]
    private Vector2 rushDuration =
        new Vector2(3f, 5f);

    [SerializeField]
    private Vector2 rushInterval =
        new Vector2(0.25f, 0.45f);

    [Header("Fever Item")]
    [Range(0f, 1f)]
    [SerializeField] private float feverItemProbability = 0.06f;

    [SerializeField] private float feverItemCooldown = 15f;

    [Header("Fever Time")]
    [SerializeField] private float feverDuration = 8f;
    [SerializeField] private float feverSpawnInterval = 0.2f;
    [SerializeField] private int feverFruitsPerSpawn = 2;

    private Camera mainCamera;

    private float spawnTimer;
    private float phaseTimer;
    private float feverTimer;
    private float feverItemCooldownTimer;

    private bool isRush;

    public bool IsFever => feverTimer > 0f;

    public bool isFall;
    private void Start()
    {
        mainCamera = Camera.main;
        SetPhase(false);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (!isFall) 
            return;

        feverItemCooldownTimer = Mathf.Max(
            0f, feverItemCooldownTimer - dt
        );

        // フィーバー中は果物だけ生成
        if (IsFever)
        {
            feverTimer -= dt;
            spawnTimer -= dt;

            if (spawnTimer <= 0f)
            {
                for (int i = 0; i < feverFruitsPerSpawn; i++)
                {
                    SpawnItem(fruitPrefab);
                }

                spawnTimer = Mathf.Max(
                    0.05f, feverSpawnInterval
                );
            }

            if (feverTimer <= 0f)
            {
                feverTimer = 0f;
                SetPhase(false);
                Debug.Log("FEVER END");
            }

            return;
        }

        // 通常時：緩急を切り替える
        phaseTimer -= dt;

        if (phaseTimer <= 0f)
        {
            SetPhase(!isRush);
        }

        spawnTimer -= dt;

        if (spawnTimer <= 0f)
        {
            SpawnNormalItem();
            spawnTimer = GetSpawnInterval();
        }
    }

    private void SetPhase(bool rush)
    {
        isRush = rush;

        phaseTimer = Mathf.Max(
            0.1f,
            RandomRange(isRush ? rushDuration : calmDuration)
        );

        spawnTimer = GetSpawnInterval();
    }

    private float GetSpawnInterval()
    {
        return Mathf.Max(
            0.05f,
            RandomRange(isRush ? rushInterval : calmInterval)
        );
    }

    private float RandomRange(Vector2 range)
    {
        return Random.Range(
            Mathf.Min(range.x, range.y),
            Mathf.Max(range.x, range.y)
        );
    }

    private void SpawnNormalItem()
    {
        // 特別なアイテムを低確率で出現させる
        if (feverItemPrefab != null &&
            feverItemCooldownTimer <= 0f &&
            Random.value < feverItemProbability)
        {
            SpawnItem(feverItemPrefab);

            feverItemCooldownTimer =
                Mathf.Max(0f, feverItemCooldown);

            return;
        }

        bool isBug = Random.value < bugProbability;

        SpawnItem(isBug ? bugPrefab : fruitPrefab);
    }

    private void SpawnItem(GameObject prefab)
    {
        if (mainCamera == null || prefab == null)
            return;

        float halfWidth =
            mainCamera.orthographicSize *
            mainCamera.aspect;

        float margin = 0.5f;

        float x = Random.Range(
            -Mathf.Max(0f, halfWidth - margin),
            Mathf.Max(0f, halfWidth - margin)
        );

        float y = mainCamera.orthographicSize + 1f;

        Vector3 spawnPosition =
            mainCamera.transform.position +
            new Vector3(x, y, 10f);

        GameObject item = Instantiate(
            prefab,
            spawnPosition,
            Quaternion.identity
        );

        FallingObject falling =
            item.GetComponent<FallingObject>();

        if (falling != null)
        {
            falling.SetSpeed(
                Random.Range(minFallSpeed, maxFallSpeed)
            );
        }
    }

    // 特別なアイテムを取得したときに呼び出す
    public void StartFever()
    {
        feverTimer = Mathf.Max(0.1f, feverDuration);
        spawnTimer = 0f;

        Debug.Log("FEVER START!");
    }
}
