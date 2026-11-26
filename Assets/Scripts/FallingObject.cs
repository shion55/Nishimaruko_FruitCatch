
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
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
    [SerializeField] private float fallSpeed = 3f; // 下降速度（units/秒）。生成時はSpawnerが設定する。

    [Header("Bounce")]
    [SerializeField, Min(0.1f)] private float bounceGravity = 10f; // 上昇中の減速（units/秒²）。初速と合わせて高さを調整。
    [SerializeField] private Camera gameplayCamera;

    // 共通の速度制御
    public static float FruitMultiplier = 1f;
    public static float BugMultiplier = 1f;

    // 最高落下速度（units/秒）
    public static float FruitMaxSpeed = 4f;
    public static float BugMaxSpeed = 4f;

    private Rigidbody2D body;
    private CatchableItem catchableItem;
    public float VerticalSpeed { get; private set; }
    public bool IsRising => VerticalSpeed > 0f;

    private void Awake()
    {
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;
        body = GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        catchableItem = GetComponent<CatchableItem>();
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector2 position = body.position;
        float descentSpeed = GetDescentSpeed();

        if (IsRising)
        {
            float gravity = Mathf.Max(0.1f, bounceGravity);
            float ascentTime = Mathf.Min(dt, VerticalSpeed / gravity);
            position.y += VerticalSpeed * ascentTime - 0.5f * gravity * ascentTime * ascentTime;
            VerticalSpeed -= gravity * ascentTime;

            if (VerticalSpeed <= 0.0001f)
            {
                position.y -= descentSpeed * (dt - ascentTime);
                VerticalSpeed = -descentSpeed;
            }
        }
        else
        {
            VerticalSpeed = -descentSpeed;
            position.y += VerticalSpeed * dt;
        }

        body.MovePosition(position);

        if (gameplayCamera != null && position.y <
            gameplayCamera.transform.position.y - gameplayCamera.orthographicSize - 1f)
            Destroy(gameObject);
    }

    public bool TryBounce(float speed)
    {
        if (!isActiveAndEnabled || IsRising || speed <= 0f)
            return false;

        VerticalSpeed = speed;
        return true;
    }

    private void OnDisable()
    {
        if (body != null)
            body.linearVelocity = Vector2.zero;
        VerticalSpeed = 0f;
    }

    internal float RelativePitchSlowdown
    {
        get
        {
            float normalSpeed = GetDescentSpeed(applyPitch: false);
            return normalSpeed > 0f ? Mathf.Clamp01(1f - GetDescentSpeed() / normalSpeed) : 0f;
        }
    }

    private float GetDescentSpeed(bool applyPitch = true)
    {
        float currentSpeed = fallSpeed;

        ItemType effectiveType = itemType;
        if (catchableItem != null && catchableItem.Definition != null)
            effectiveType = catchableItem.Category == ItemCategory.Bug ? ItemType.Bug : ItemType.Fruit;

        switch (effectiveType)
        {
            case ItemType.Fruit:
                if (applyPitch) currentSpeed *= FruitMultiplier;
                currentSpeed = Mathf.Min(
                    currentSpeed, FruitMaxSpeed
                );
                break;

            case ItemType.Bug:
                if (applyPitch) currentSpeed *= BugMultiplier;
                currentSpeed = Mathf.Min(
                    currentSpeed, BugMaxSpeed
                );
                break;

            case ItemType.Other:
                // その他は通常速度
                break;
        }

        return Mathf.Max(0f, currentSpeed);
    }

    public void SetSpeed(float speed)
    {
        fallSpeed = speed;
    }
}
