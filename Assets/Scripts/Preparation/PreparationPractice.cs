using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitCatch.Preparation
{
    public sealed class PreparationPractice : MonoBehaviour
    {
        [SerializeField] private PreparationFlowController flow;
        [SerializeField] private PreparationVoiceDetector voice;
        [SerializeField] private CharacterPairController movement;
        [SerializeField] private RectTransform character;
        [SerializeField] private Graphic leftGoal, rightGoal, celebration;
        [SerializeField] private Image eater, fruit;
        [SerializeField] private Image carrier;
        [SerializeField] private GameObject gameplayFruitPrefab;
        [SerializeField] private Sprite openMouth;
        [SerializeField] private TMP_Text feedback;
        [SerializeField] private GameObject registrationRetry;
        [SerializeField] private PitchFallSpeedController pitchController;
        [SerializeField] private PreparationPitchMeter pitchMeter;
        [SerializeField, Range(0.01f, 0.2f)] private float goalTolerance = 0.045f;
        [SerializeField, Range(0.04f, 0.35f)] private float minimumTravel = 0.15f;
        [SerializeField, Min(0.1f)] private float retryHintSeconds = 4f;
        [SerializeField, Min(0.5f)] private float fallSeconds = 4f;
        [SerializeField, Min(0.1f)] private float respawnSeconds = 0.75f;
        [SerializeField, Min(0.1f)] private float successSeconds = 0.7f;
        [SerializeField, Range(0f, 0.35f)] private float fruitRetryOffset = 0.26f;

        private Camera view;
        private Sprite closedMouth;
        private bool leftReached, rightReached, completed;
        private float leftmost, rightmost, timeWithoutInput, elapsed, successElapsed;
        private int attempt;
        private Vector2 fruitStart, restPosition;
        private RectTransform board;
        private readonly PreparationOvalGraphic[] sparkles = new PreparationOvalGraphic[8];
        private static readonly Color[] SparkleColors =
        {
            new Color32(255, 215, 88, 255), new Color32(255, 153, 190, 255),
            new Color32(137, 226, 253, 255), new Color32(188, 166, 255, 255)
        };

        private void Awake()
        {
            view = Camera.main;
            board = (RectTransform)character.parent;
            restPosition = character.anchorMin;
            if (eater != null) closedMouth = eater.sprite;
            if (fruit != null) fruitStart = fruit.rectTransform.anchorMin;
        }

        private void OnEnable()
        {
            movement.transform.position = view.transform.position + Vector3.forward * 10f;
            movement.SetMovementEnabled(true);
            SetAnchor(character, restPosition.x, restPosition.y);
            leftReached = rightReached = completed = false;
            leftmost = float.PositiveInfinity;
            rightmost = float.NegativeInfinity;
            elapsed = -0.65f;
            timeWithoutInput = successElapsed = 0f;
            attempt = 0;
            character.localScale = Vector3.one;
            if (leftGoal != null) leftGoal.enabled = true;
            if (rightGoal != null) rightGoal.enabled = true;
            if (eater != null) eater.sprite = closedMouth;
            if (fruit != null) fruit.enabled = false;
            if (pitchMeter != null) pitchMeter.gameObject.SetActive(true);
            celebration.enabled = false;
            celebration.rectTransform.localScale = Vector3.one;
            celebration.rectTransform.localRotation = Quaternion.identity;
            foreach (var sparkle in sparkles)
                if (sparkle != null) sparkle.enabled = false;
            registrationRetry.SetActive(false);
            feedback.text = string.Empty;
        }

        private void LateUpdate()
        {
            if (flow.CurrentPanel != gameObject || flow.IsTransitioning) return;
            float dt = Time.unscaledDeltaTime;
            MatchGameplaySize(carrier, Vector3.one);
            if (eater != null)
            {
                float offset = 1.284f * board.rect.height / (2f * view.orthographicSize);
                eater.rectTransform.anchoredPosition = new Vector2(0f, offset);
                MatchGameplaySize(eater, Vector3.one);
                if (gameplayFruitPrefab != null) MatchGameplaySize(fruit, gameplayFruitPrefab.transform.localScale);
            }
            if (completed)
            {
                successElapsed += dt;
                float progress = Mathf.Clamp01(successElapsed / successSeconds);
                character.localScale = Vector3.one * (1f + 0.17f * Mathf.Sin(progress * Mathf.PI));
                AnimateCelebration(progress);
                if (progress >= 1f) flow.ReportPracticeComplete(gameObject);
                return;
            }

            bool hasInput = movement.TryGetFollowTarget(out _);
            timeWithoutInput = hasInput ? 0f : timeWithoutInput + dt;
            bool needsRetry = timeWithoutInput >= retryHintSeconds;
            registrationRetry.SetActive(needsRetry);
            if (!hasInput)
            {
                feedback.text = needsRetry ? "カメラの前に戻るか、顔をあわせ直してね" : "カメラの前に立ってね";
                if (fruit != null)
                {
                    fruit.enabled = false;
                    eater.sprite = closedMouth;
                }
                return;
            }

            float x = view.WorldToViewportPoint(movement.transform.position).x;
            SetAnchor(character, x, restPosition.y);
            if (fruit == null)
                CheckGoals(x);
            else
                DropFruit(x, dt);
        }

        private void CheckGoals(float x)
        {
            leftmost = Mathf.Min(leftmost, x);
            rightmost = Mathf.Max(rightmost, x);
            if (Mathf.Abs(x - leftGoal.rectTransform.anchorMin.x) <= goalTolerance) leftReached = true;
            if (Mathf.Abs(x - rightGoal.rectTransform.anchorMin.x) <= goalTolerance) rightReached = true;
            leftGoal.enabled = !leftReached;
            rightGoal.enabled = !rightReached;
            if (leftReached && rightReached)
                feedback.text = "もう少し 左右に うごこう！";
            else
                feedback.text = leftReached ? "つぎは右へ！" : rightReached ? "つぎは左へ！" : "左右のめじるしにタッチ！";
            if (leftReached && rightReached && rightmost - leftmost >= minimumTravel) Complete();
        }

        private void DropFruit(float x, float dt)
        {
            bool mouthOpen = voice.IsVoiceDetected;
            eater.sprite = mouthOpen ? openMouth : closedMouth;
            // Keep the existing respawn delay on real time and slow only the falling fruit.
            if (elapsed < 0f)
            {
                float wait = Mathf.Min(dt, -elapsed);
                elapsed += wait;
                dt -= wait;
            }
            if (elapsed >= 0f)
                elapsed += dt * (pitchController != null ? pitchController.CurrentSpeedMultiplier : 1f);
            fruit.enabled = elapsed >= 0f;
            if (!fruit.enabled) return;
            feedback.text = "高いこえ・低いこえをためして、くだものをたべよう！";
            float fruitX = fruitStart.x + (attempt % 3 == 0 ? 0f : attempt % 3 == 1 ? -fruitRetryOffset : fruitRetryOffset);
            float mouthY = board.InverseTransformPoint(eater.rectTransform.position).y / board.rect.height + board.pivot.y;
            float progress = elapsed / fallSeconds;
            SetAnchor(fruit.rectTransform, fruitX, Mathf.LerpUnclamped(fruitStart.y, mouthY, progress));
            if (progress >= 0.88f && progress <= 1.12f && Mathf.Abs(x - fruitX) <= goalTolerance && mouthOpen)
            {
                fruit.enabled = false;
                eater.sprite = closedMouth;
                Complete();
            }
            else if (progress > 1.2f)
            {
                attempt++;
                elapsed = -respawnSeconds;
                fruit.enabled = false;
                feedback.text = "もういちど！";
            }
        }

        private void Complete()
        {
            completed = true;
            if (pitchMeter != null) pitchMeter.gameObject.SetActive(false);
            movement.SetMovementEnabled(false);
            celebration.enabled = true;
            SetAnchor(celebration.rectTransform, character.anchorMin.x, celebration.rectTransform.anchorMin.y);
            celebration.transform.SetAsLastSibling();
            AnimateCelebration(0f);
            feedback.text = fruit != null ? "たべられた！" : "できた！";
            FruitCatchAudioManager.Instance.PlayPracticeSuccess(fruit != null);
        }

        private void AnimateCelebration(float progress)
        {
            float spread = 1f - Mathf.Pow(1f - progress, 3f);
            float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.5f, 1f, progress));
            celebration.rectTransform.localScale = Vector3.one * (0.45f + 0.55f * spread + .28f * Mathf.Sin(progress * Mathf.PI));
            celebration.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -14f * (1f - progress));
            for (int i = 0; i < sparkles.Length; i++)
            {
                if (sparkles[i] == null)
                {
                    var particle = new GameObject("SuccessSparkle", typeof(RectTransform));
                    particle.transform.SetParent(celebration.transform, false);
                    sparkles[i] = particle.AddComponent<PreparationOvalGraphic>();
                    sparkles[i].star = i % 2 == 0;
                    sparkles[i].filled = true;
                    sparkles[i].raycastTarget = false;
                }
                var sparkle = sparkles[i];
                sparkle.enabled = fade > 0f;
                Color tint = SparkleColors[i % SparkleColors.Length];
                tint.a = fade;
                sparkle.color = tint;
                float angle = i * Mathf.PI / 4f;
                float size = (i % 2 == 0 ? 34f : 17f) * Mathf.Lerp(.65f, 1f, spread);
                sparkle.rectTransform.sizeDelta = Vector2.one * size;
                sparkle.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Lerp(24f, 145f, spread);
                sparkle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0 ? 1 : -1) * progress * 95f);
            }
        }

        private void MatchGameplaySize(Image image, Vector3 worldScale)
        {
            if (image == null || image.sprite == null) return;
            float units = board.rect.height / (2f * view.orthographicSize);
            Vector2 size = image.sprite.rect.size / image.sprite.pixelsPerUnit * units;
            image.rectTransform.sizeDelta = Vector2.Scale(size, new Vector2(worldScale.x, worldScale.y));
            image.preserveAspect = false;
        }

        private static void SetAnchor(RectTransform rect, float x, float y)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(x, y);
        }
    }
}
