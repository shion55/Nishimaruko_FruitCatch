using UnityEngine;

public class FallingItemSpawner : MonoBehaviour
{
    [SerializeField] private GameObject fruitPrefab;
    [SerializeField] private GameObject bugPrefab;

    [SerializeField] private float spawnInterval = 1f;

    [Range(0f, 1f)]
    [SerializeField] private float bugProbability = 0.2f;

    [SerializeField] private float minFallSpeed = 2f;
    [SerializeField] private float maxFallSpeed = 4f;

    private Camera mainCamera;
    private float timer;

    void Start()
    {
        mainCamera = Camera.main;
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= spawnInterval)
        {
            timer = 0f;
            SpawnItem();
        }
    }

    void SpawnItem()
    {
        if (mainCamera == null) return;

        float halfWidth =
            mainCamera.orthographicSize * mainCamera.aspect;

        float x = Random.Range(
            -halfWidth + 0.5f,
            halfWidth - 0.5f
        );

        float y = mainCamera.orthographicSize + 1f;

        Vector3 spawnPosition =
            mainCamera.transform.position
            + new Vector3(x, y, 10f);

        bool isBug = Random.value < bugProbability;

        GameObject prefab = isBug ? bugPrefab : fruitPrefab;

        if (prefab == null) return;

        GameObject item = Instantiate(
            prefab,
            spawnPosition,
            Quaternion.identity
        );

        FallingObject falling = item.GetComponent<FallingObject>();

        if (falling != null)
        {
            falling.SetSpeed(
                Random.Range(minFallSpeed, maxFallSpeed)
            );
        }
    }
}
