using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace FruitCatch.Preparation
{
    /// <summary>
    /// メニュー画面専用のループアニメーションです。
    /// ゲーム中と同じ歩行スプライトを使い、Eaterを運んで置きます。
    /// </summary>
    public sealed class PreparationMenuCharacterAnimator : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private RectTransform carrierTransform;
        [SerializeField] private RectTransform eaterTransform;
        [SerializeField] private Image carrierImage;
        [SerializeField] private Image eaterImage;

        [Header("Carrier Sprites")]
        [SerializeField] private Sprite holdFrontSprite;
        [SerializeField] private Sprite waitFrontSprite;
        [SerializeField] private Sprite[] walkSprites = new Sprite[2];
        [SerializeField] private bool walkSpritesFaceRight = true;

        [Header("Size (gameplay reference)")]
        [SerializeField, Min(1f)] private float characterSize = 216f;

        [Header("Movement")]
        [SerializeField, Min(1f)] private float moveSpeed = 540f;
        [SerializeField, Min(0.1f)] private float walkFPS = 6f;
        [SerializeField, Range(0.1f, 0.45f)] private float dropPointFromSide = 0.28f;
        [SerializeField, Min(0f)] private float edgePadding = 40f;
        [SerializeField] private float groundY = 108f;
        [SerializeField] private float heldOffsetY = 118f;

        [Header("Pick up / Put down")]
        [SerializeField, Min(0.05f)] private float actionDuration = 0.55f;
        [SerializeField, Min(0f)] private float stopDuration = 0.2f;
        [SerializeField] private Vector2 squashScale = new Vector2(1.18f, 0.72f);
        [SerializeField] private Vector2 stretchScale = new Vector2(0.9f, 1.18f);

        private Coroutine animationCoroutine;
        private bool carrying;

        private void OnEnable()
        {
            ApplyReferenceSize();
            animationCoroutine = StartCoroutine(AnimationLoop());
        }

        private void OnDisable()
        {
            if (animationCoroutine != null)
                StopCoroutine(animationCoroutine);

            animationCoroutine = null;
            carrying = false;

            if (eaterTransform != null)
                eaterTransform.localScale = Vector3.one;
        }

        private void ApplyReferenceSize()
        {
            Vector2 size = Vector2.one * characterSize;
            if (carrierTransform != null)
                carrierTransform.sizeDelta = size;
            if (eaterTransform != null)
                eaterTransform.sizeDelta = size;
        }

        private IEnumerator AnimationLoop()
        {
            yield return null; // RectTransformの実寸が確定するまで待つ

            while (enabled && gameObject.activeInHierarchy)
            {
                GetHorizontalPoints(out float leftEdge, out float leftDrop, out float rightDrop, out float rightEdge);
                SetCharacterPosition(carrierTransform, leftEdge, groundY);
                SetCharacterPosition(eaterTransform, leftDrop, groundY);
                carrying = false;

                yield return MoveCarrier(leftDrop);
                yield return PickUpEater();
                yield return MoveCarrier(rightDrop);
                yield return PutDownEater();

                yield return MoveCarrier(rightEdge);
                yield return MoveCarrier(rightDrop);
                yield return PickUpEater();
                yield return MoveCarrier(leftDrop);
                yield return PutDownEater();

                yield return MoveCarrier(leftEdge);
            }
        }

        private IEnumerator MoveCarrier(float targetX)
        {
            if (carrierTransform == null)
                yield break;

            float startX = carrierTransform.anchoredPosition.x;
            float distance = Mathf.Abs(targetX - startX);
            float duration = distance / Mathf.Max(1f, moveSpeed);
            bool movingRight = targetX >= startX;
            float elapsed = 0f;
            float frameTimer = 0f;
            int frameIndex = 0;

            SetWalkDirection(movingRight);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float x = Mathf.Lerp(startX, targetX, duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration));
                SetCharacterPosition(carrierTransform, x, groundY);

                if (carrying && eaterTransform != null)
                    SetCharacterPosition(eaterTransform, x, groundY + heldOffsetY);

                frameTimer += Time.deltaTime;
                if (walkSprites != null && walkSprites.Length >= 2 &&
                    frameTimer >= 1f / Mathf.Max(0.1f, walkFPS))
                {
                    frameTimer = 0f;
                    frameIndex = (frameIndex + 1) % 2;
                    if (carrierImage != null && walkSprites[frameIndex] != null)
                        carrierImage.sprite = walkSprites[frameIndex];
                }

                yield return null;
            }

            SetCharacterPosition(carrierTransform, targetX, groundY);
            if (carrying && eaterTransform != null)
                SetCharacterPosition(eaterTransform, targetX, groundY + heldOffsetY);
        }

        private IEnumerator PickUpEater()
        {
            if (carrierImage != null && holdFrontSprite != null)
            {
                carrierImage.sprite = holdFrontSprite;
                carrierImage.rectTransform.localScale = Vector3.one;
            }

            yield return BounceEater(groundY, groundY + heldOffsetY);
            carrying = true;
            yield return new WaitForSeconds(stopDuration);
        }

        private IEnumerator PutDownEater()
        {
            if (carrierImage != null && waitFrontSprite != null)
            {
                carrierImage.sprite = waitFrontSprite;
                carrierImage.rectTransform.localScale = Vector3.one;
            }

            carrying = false;
            yield return BounceEater(groundY + heldOffsetY, groundY);
            yield return new WaitForSeconds(stopDuration);
        }

        private IEnumerator BounceEater(float startY, float endY)
        {
            if (eaterTransform == null)
                yield break;

            float elapsed = 0f;
            float duration = Mathf.Max(0.05f, actionDuration);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float easedY = Mathf.SmoothStep(startY, endY, t);
                SetCharacterPosition(eaterTransform, carrierTransform.anchoredPosition.x, easedY);

                Vector2 scale;
                if (t < 0.32f)
                    scale = Vector2.Lerp(Vector2.one, squashScale, t / 0.32f);
                else if (t < 0.68f)
                    scale = Vector2.Lerp(squashScale, stretchScale, (t - 0.32f) / 0.36f);
                else
                    scale = Vector2.Lerp(stretchScale, Vector2.one, (t - 0.68f) / 0.32f);

                eaterTransform.localScale = new Vector3(scale.x, scale.y, 1f);
                yield return null;
            }

            SetCharacterPosition(eaterTransform, carrierTransform.anchoredPosition.x, endY);
            eaterTransform.localScale = Vector3.one;
        }

        private void SetWalkDirection(bool movingRight)
        {
            if (carrierImage == null)
                return;

            carrierImage.rectTransform.localScale = Vector3.one;
            carrierImage.sprite = walkSprites != null && walkSprites.Length > 0
                ? walkSprites[0]
                : carrierImage.sprite;
            carrierImage.rectTransform.localScale = new Vector3(
                walkSpritesFaceRight ? (movingRight ? 1f : -1f) : (movingRight ? -1f : 1f),
                1f,
                1f);
        }

        private void GetHorizontalPoints(out float leftEdge, out float leftDrop, out float rightDrop, out float rightEdge)
        {
            RectTransform parent = carrierTransform != null ? carrierTransform.parent as RectTransform : null;
            float halfWidth = parent != null ? parent.rect.width * 0.5f : 960f;
            float halfCharacter = characterSize * 0.5f;
            leftEdge = -halfWidth + halfCharacter + edgePadding;
            rightEdge = halfWidth - halfCharacter - edgePadding;
            leftDrop = Mathf.Lerp(leftEdge, rightEdge, dropPointFromSide);
            rightDrop = Mathf.Lerp(rightEdge, leftEdge, dropPointFromSide);
        }

        private static void SetCharacterPosition(RectTransform target, float x, float y)
        {
            if (target != null)
                target.anchoredPosition = new Vector2(x, y);
        }
    }
}
