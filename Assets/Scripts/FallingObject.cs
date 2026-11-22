
using UnityEngine;

public class FallingObject : MonoBehaviour
{
    public enum ItemType
    {
        Fruit,
        Bug,
        Other
    }

    [Header("Item")]
    [SerializeField] private ItemType itemType = ItemType.Fruit;

    [Header("Fall")]
    [SerializeField] private float fallSpeed = 3f;

    // 共通の速度制御
    public static float FruitMultiplier = 1f;
    public static float BugMultiplier = 1f;

    // 最高落下速度（units/秒）
    public static float FruitMaxSpeed = 4f;
    public static float BugMaxSpeed = 4f;

    private Camera mainCamera;
    private CatchableItem catchableItem;

    void Start()
    {
        mainCamera = Camera.main;
        catchableItem = GetComponent<CatchableItem>();
    }

    void Update()
    {
        float currentSpeed = fallSpeed;

        ItemType effectiveType = itemType;
        if (catchableItem != null && catchableItem.Definition != null)
            effectiveType = catchableItem.Category == ItemCategory.Bug ? ItemType.Bug : ItemType.Fruit;

        switch (effectiveType)
        {
            case ItemType.Fruit:
                currentSpeed *= FruitMultiplier;
                currentSpeed = Mathf.Min(
                    currentSpeed, FruitMaxSpeed
                );
                break;

            case ItemType.Bug:
                currentSpeed *= BugMultiplier;
                currentSpeed = Mathf.Min(
                    currentSpeed, BugMaxSpeed
                );
                break;

            case ItemType.Other:
                // その他は通常速度
                break;
        }

        currentSpeed = Mathf.Max(0f, currentSpeed);

        transform.position +=
            Vector3.down * currentSpeed * Time.deltaTime;

        if (mainCamera != null)
        {
            float bottom =
                mainCamera.transform.position.y
                - mainCamera.orthographicSize - 1f;

            if (transform.position.y < bottom)
            {
                Destroy(gameObject);
            }
        }
    }

    public void SetSpeed(float speed)
    {
        fallSpeed = speed;
    }
}
