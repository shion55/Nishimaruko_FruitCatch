
using UnityEngine;
using UnityEngine.UI;

public class CarrierAnimator : MonoBehaviour
{
    [Header("Sprites")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Image uiImage;
    [SerializeField] private Sprite frontSprite;
    [SerializeField] private Sprite[] walkSprites = new Sprite[2];

    [Header("Animation")]
    [SerializeField] private float walkFPS = 6f;
    [SerializeField] private float moveThreshold = 0.05f;
    [SerializeField] private bool spritesFaceRight = true;

    [Header("Fever")]
    [SerializeField] private FallingItemSpawner fallingItemSpawner;
    [SerializeField, Min(1f)] private float feverWalkSpeedMultiplier = 1.5f; // 移動中の足踏み速度の倍率。

    private Vector3 previousPosition;
    private float animationTimer;
    private int frameIndex;

    void Start()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        previousPosition = transform.position;
        SetSprite(frontSprite, false);
    }

    void LateUpdate()
    {
        float deltaX = transform.position.x - previousPosition.x;
        previousPosition = transform.position;

        float speedX = deltaX / Mathf.Max(Time.deltaTime, 0.0001f);

        // 停止している場合
        if (Mathf.Abs(speedX) < moveThreshold)
        {
            SetSprite(frontSprite, false);

            animationTimer = 0f;
            frameIndex = 0;
            return;
        }

        // 移動方向によって左右反転
        bool movingRight = speedX > 0f;
        bool flip = spritesFaceRight
            ? !movingRight
            : movingRight;

        // 歩行アニメーション
        if (walkSprites.Length < 2 ||
            walkSprites[0] == null ||
            walkSprites[1] == null)
        {
            SetSprite(null, flip);
            return;
        }

        float feverMultiplier = fallingItemSpawner != null && fallingItemSpawner.IsFever
            ? Mathf.Max(1f, feverWalkSpeedMultiplier) : 1f;
        animationTimer += Time.deltaTime * feverMultiplier;
        float frameDuration = 1f / Mathf.Max(walkFPS, 0.1f);

        while (animationTimer >= frameDuration)
        {
            animationTimer -= frameDuration;
            frameIndex = (frameIndex + 1) % 2;
        }

        SetSprite(walkSprites[frameIndex], flip);
    }

    private void SetSprite(Sprite sprite, bool flip)
    {
        if (uiImage != null)
        {
            if (sprite != null) uiImage.sprite = sprite;
            uiImage.rectTransform.localRotation = Quaternion.Euler(0f, flip ? 180f : 0f, 0f);
        }
        else if (spriteRenderer != null)
        {
            if (sprite != null) spriteRenderer.sprite = sprite;
            spriteRenderer.flipX = flip;
        }
    }
}
