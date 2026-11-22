
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

    public enum MotionPhase
    {
        Falling,
        ReflectedRising,
        ReflectedDescending
    }

    [Header("Item")]
    [SerializeField] private ItemType itemType = ItemType.Fruit;

    [Header("Fall")]
    [SerializeField] private float fallSpeed = 3f;

    [Header("Bounce")]
    [SerializeField, Min(0.1f)] private float bounceGravity = 10f;
    [SerializeField] private Camera gameplayCamera;

    // 共通の速度制御
    public static float FruitMultiplier = 1f;
    public static float BugMultiplier = 1f;

    // 最高落下速度（units/秒）
    public static float FruitMaxSpeed = 4f;
    public static float BugMaxSpeed = 4f;

    private Rigidbody2D body;
    private CatchableItem catchableItem;
    private SpriteRenderer spriteRenderer;
    private Collider2D bounceContact;

    public MotionPhase MotionState { get; private set; }
    public Vector2 Velocity { get; private set; }
    public bool IsRising => MotionState == MotionPhase.ReflectedRising;

    private void Awake()
    {
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;
        body = GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        catchableItem = GetComponent<CatchableItem>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector2 position = body.position;
        Vector2 velocity = Velocity;
        float descentSpeed = GetDescentSpeed();

        if (IsRising)
        {
            float gravity = Mathf.Max(0.1f, bounceGravity);
            float ascentTime = Mathf.Min(dt, Mathf.Max(0f, velocity.y) / gravity);
            position.y += velocity.y * ascentTime - 0.5f * gravity * ascentTime * ascentTime;
            velocity.y -= gravity * ascentTime;

            if (velocity.y <= 0.0001f)
            {
                MotionState = MotionPhase.ReflectedDescending;
                position.y -= descentSpeed * (dt - ascentTime);
                velocity.y = -descentSpeed;
            }
        }
        else
        {
            velocity.y = -descentSpeed;
            position.y += velocity.y * dt;
        }

        position.x += velocity.x * dt;
        ClampToHorizontalBounds(ref position, ref velocity);
        Velocity = velocity;
        body.MovePosition(position);

        if (gameplayCamera != null && position.y <
            gameplayCamera.transform.position.y - gameplayCamera.orthographicSize - 1f)
            Destroy(gameObject);
    }

    public bool TryBounce(Vector2 launchVelocity, Collider2D mouth)
    {
        if (!isActiveAndEnabled || mouth == null || IsRising || bounceContact != null || launchVelocity.y <= 0f)
            return false;

        bounceContact = mouth;
        MotionState = MotionPhase.ReflectedRising;
        Velocity = launchVelocity;
        return true;
    }

    public void ReleaseBounceContact(Collider2D mouth)
    {
        if (bounceContact == mouth)
            bounceContact = null;
    }

    private void OnDisable()
    {
        if (body != null)
            body.linearVelocity = Vector2.zero;
        Velocity = Vector2.zero;
        bounceContact = null;
    }

    private void ClampToHorizontalBounds(ref Vector2 position, ref Vector2 velocity)
    {
        if (gameplayCamera == null)
            return;

        float itemHalfWidth = catchableItem != null ? catchableItem.CatchWidth * 0.5f : 0f;
        if (spriteRenderer != null)
            itemHalfWidth = Mathf.Max(itemHalfWidth, spriteRenderer.bounds.extents.x);
        float limit = Mathf.Max(0f, gameplayCamera.orthographicSize * gameplayCamera.aspect - itemHalfWidth);
        float centerX = gameplayCamera.transform.position.x;
        float clampedX = Mathf.Clamp(position.x, centerX - limit, centerX + limit);
        if (clampedX != position.x)
            velocity.x = 0f;
        position.x = clampedX;
    }

    private float GetDescentSpeed()
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

        return Mathf.Max(0f, currentSpeed);
    }

    public void SetSpeed(float speed)
    {
        fallSpeed = speed;
    }
}
