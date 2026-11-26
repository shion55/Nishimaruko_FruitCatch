using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitCatch.Preparation
{
    public sealed class PreparationFacePresentation : MonoBehaviour
    {
        [SerializeField] private RawImage cameraImage;
        [SerializeField] private Sprite carrierSprite;
        [SerializeField] private Image readingBackdrop;
        [SerializeField, Min(0.1f)] private float morphDuration = 1.5f;
        [SerializeField, Min(0f)] private float carrierHoldSeconds = 1f;

        private PreparationOvalGraphic outline;
        private PreparationOvalGraphic progressArc;
        private RawImage liveFacePreview;
        private RectTransform portrait;
        private RawImage frozenFace;
        private Image carrier;
        private Texture2D snapshot;
        private PreparationOvalGraphic flash;
        private PreparationOvalGraphic halo;
        private readonly RectTransform[] sparkles = new RectTransform[12];
        private Vector2 carrierRestPosition;
        private bool playing;
        private readonly Vector3[] corners = new Vector3[4];

        private void Awake()
        {
            // Keep the shared camera RawImage and its UVs/transform untouched.
            GetComponent<Image>().enabled = false;
            TMP_Text label = GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.gameObject.SetActive(false);
            if (readingBackdrop != null)
                readingBackdrop.enabled = false;

            liveFacePreview = CreateRect("FaceCameraPreview", transform).gameObject.AddComponent<RawImage>();
            liveFacePreview.raycastTarget = false;
            liveFacePreview.color = Color.white;
            liveFacePreview.transform.SetAsFirstSibling();

            portrait = CreateRect("FaceTransformation", transform);
            PreparationOvalGraphic maskGraphic = portrait.gameObject.AddComponent<PreparationOvalGraphic>();
            maskGraphic.filled = true;
            maskGraphic.rectangular = true;
            Mask mask = portrait.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            frozenFace = CreateRect("FrozenFace", portrait).gameObject.AddComponent<RawImage>();
            frozenFace.raycastTarget = false;
            portrait.gameObject.SetActive(false);

            halo = CreateRect("TransformationHalo", transform).gameObject.AddComponent<PreparationOvalGraphic>();
            halo.thickness = 14f;
            halo.raycastTarget = false;
            halo.gameObject.SetActive(false);

            carrier = CreateRect("TransformationCarrier", transform).gameObject.AddComponent<Image>();
            carrier.sprite = carrierSprite;
            carrier.preserveAspect = true;
            carrier.raycastTarget = false;
            carrier.gameObject.SetActive(false);
            carrierRestPosition = carrier.rectTransform.anchoredPosition;

            flash = CreateRect("TransformationFlash", transform).gameObject.AddComponent<PreparationOvalGraphic>();
            flash.filled = true;
            flash.raycastTarget = false;
            flash.gameObject.SetActive(false);
            for (int i = 0; i < sparkles.Length; i++)
            {
                RectTransform sparkle = CreateRect("TransformationSparkle_" + i, transform);
                sparkle.anchorMin = sparkle.anchorMax = new Vector2(0.5f, 0.5f);
                sparkle.sizeDelta = new Vector2(32f, 32f);
                Image horizontal = CreateRect("Horizontal", sparkle).gameObject.AddComponent<Image>();
                horizontal.rectTransform.anchorMin = new Vector2(0f, 0.4f);
                horizontal.rectTransform.anchorMax = new Vector2(1f, 0.6f);
                Image vertical = CreateRect("Vertical", sparkle).gameObject.AddComponent<Image>();
                vertical.rectTransform.anchorMin = new Vector2(0.4f, 0f);
                vertical.rectTransform.anchorMax = new Vector2(0.6f, 1f);
                horizontal.color = vertical.color = new Color(1f, 0.72f, 0.08f);
                horizontal.raycastTarget = vertical.raycastTarget = false;
                sparkles[i] = sparkle;
                sparkle.gameObject.SetActive(false);
            }

            outline = CreateRect("OvalOutline", transform).gameObject.AddComponent<PreparationOvalGraphic>();
            outline.thickness = 10f;
            outline.rectangular = true;
            outline.color = new Color(0.08f, 0.18f, 0.24f, 1f);
            outline.raycastTarget = false;
            progressArc = CreateRect("OvalProgress", transform).gameObject.AddComponent<PreparationOvalGraphic>();
            progressArc.thickness = 6f;
            progressArc.rectangular = true;
            progressArc.raycastTarget = false;
        }

        private void OnEnable() => ResetPresentation();

        private void LateUpdate()
        {
            if (playing || liveFacePreview == null || cameraImage == null)
                return;

            liveFacePreview.texture = cameraImage.texture;
            liveFacePreview.uvRect = GetGuideUv();
            liveFacePreview.enabled = cameraImage.enabled && cameraImage.texture != null;
        }

        public void ResetPresentation()
        {
            playing = false;
            if (Application.isPlaying)
                FruitCatchAudioManager.Instance.StopTransformation();
            if (portrait == null) return;
            liveFacePreview.enabled = cameraImage != null && cameraImage.enabled && cameraImage.texture != null;
            portrait.localScale = Vector3.one;
            portrait.localRotation = Quaternion.identity;
            portrait.gameObject.SetActive(false);
            carrier.gameObject.SetActive(false);
            carrier.rectTransform.anchoredPosition = carrierRestPosition;
            carrier.rectTransform.localScale = Vector3.one;
            carrier.rectTransform.localRotation = Quaternion.identity;
            flash.gameObject.SetActive(false);
            halo.gameObject.SetActive(false);
            foreach (RectTransform sparkle in sparkles) sparkle.gameObject.SetActive(false);
            outline.gameObject.SetActive(true);
            progressArc.gameObject.SetActive(true);
            ReleaseSnapshot();
            SetStatus("WAITING", 0f);
        }

        // UV rectangle in the *displayed* image, with a bottom-left origin.
        public Rect GetGuideUv()
        {
            RectTransform guide = (RectTransform)transform;
            guide.GetWorldCorners(corners);
            Rect imageRect = cameraImage.rectTransform.rect;
            Vector3 low = cameraImage.rectTransform.InverseTransformPoint(corners[0]);
            Vector3 high = cameraImage.rectTransform.InverseTransformPoint(corners[2]);
            Rect uv = cameraImage.uvRect;
            return new Rect(
                uv.x + (low.x - imageRect.xMin) / imageRect.width * uv.width,
                uv.y + (low.y - imageRect.yMin) / imageRect.height * uv.height,
                (high.x - low.x) / imageRect.width * uv.width,
                (high.y - low.y) / imageRect.height * uv.height);
        }

        public void SetStatus(string hint, float progress)
        {
            if (playing || progressArc == null) return;
            bool holding = hint == "HOLD" || hint == "DONE";
            progressArc.progress = holding ? progress : 1f;
            progressArc.color = holding ? new Color(0.15f, 0.95f, 0.45f) : new Color(0.25f, 0.85f, 1f);
            progressArc.SetVerticesDirty();
        }

        public void Capture(Texture2D frame)
        {
            Capture(frame, GetGuideUv());
        }

        public void Capture(Texture2D frame, Rect uv)
        {
            if (snapshot != null || playing || frame == null) return;
            int x = Mathf.Clamp(Mathf.FloorToInt(uv.xMin * frame.width), 0, frame.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(uv.yMin * frame.height), 0, frame.height - 1);
            int w = Mathf.Clamp(Mathf.RoundToInt(uv.width * frame.width), 1, frame.width - x);
            int h = Mathf.Clamp(Mathf.RoundToInt(uv.height * frame.height), 1, frame.height - y);
            snapshot = new Texture2D(w, h, TextureFormat.RGB24, false);
            snapshot.SetPixels(frame.GetPixels(x, y, w, h));
            snapshot.Apply();
            frozenFace.texture = snapshot;
        }

        public IEnumerator PlaySuccess()
        {
            playing = true;
            liveFacePreview.enabled = false;
            PlayTransformationSound();
            portrait.gameObject.SetActive(snapshot != null);
            carrier.gameObject.SetActive(true);
            outline.gameObject.SetActive(false);
            progressArc.gameObject.SetActive(false);
            RectTransform carrierRect = carrier.rectTransform;
            Vector2 startPosition = carrierRestPosition;
            flash.gameObject.SetActive(true);
            halo.gameObject.SetActive(true);
            bool completionSoundPlayed = false;
            for (float elapsed = 0f; elapsed < morphDuration; elapsed += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(elapsed / morphDuration);
                float compress = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.35f, t));
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.48f, t));
                portrait.localScale = new Vector3(Mathf.Lerp(1f, 0.25f, compress), Mathf.Lerp(1f, 0.12f, compress), 1f);
                portrait.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(compress * Mathf.PI * 2) * 16f);
                frozenFace.color = new Color(1, 1, 1, 1f - blend);
                carrier.color = new Color(1, 1, 1, blend);
                if (!completionSoundPlayed && t >= 0.48f)
                {
                    PlayTransformationCompleteSound();
                    completionSoundPlayed = true;
                }
                float reveal = Mathf.InverseLerp(0.3f, 0.76f, t);
                float pop = Mathf.Sin(reveal * Mathf.PI);
                float landing = Mathf.Sin(Mathf.InverseLerp(0.76f, 1f, t) * Mathf.PI);
                carrierRect.localScale = new Vector3(Mathf.Lerp(0.25f, 1f, reveal) + pop * 0.35f + landing * 0.16f,
                    Mathf.Lerp(0.25f, 1f, reveal) + pop * 0.2f - landing * 0.2f, 1f);
                carrierRect.anchoredPosition = startPosition + Vector2.up * (pop * 110f);
                carrierRect.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(reveal * Mathf.PI * 4) * (1f - reveal) * 14f);
                float burst = Mathf.InverseLerp(0.3f, 0.85f, t);
                halo.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.25f, 1.65f, burst);
                halo.color = new Color(1f, 0.7f, 0.1f, t >= 0.3f ? (1f - burst) * 0.9f : 0f);
                flash.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.2f, 1.05f, Mathf.InverseLerp(0.25f, 0.48f, t));
                flash.color = new Color(1f, 0.94f, 0.65f,
                    Mathf.Sin(Mathf.InverseLerp(0.25f, 0.55f, t) * Mathf.PI) * 0.95f);
                for (int i = 0; i < sparkles.Length; i++)
                {
                    RectTransform sparkle = sparkles[i];
                    sparkle.gameObject.SetActive(t >= 0.3f && burst < 1f);
                    float angle = i * Mathf.PI * 2f / sparkles.Length;
                    sparkle.anchoredPosition = new Vector2(Mathf.Cos(angle) * 260f, Mathf.Sin(angle) * 290f) * Mathf.Lerp(0.15f, 1f, burst);
                    sparkle.localRotation = Quaternion.Euler(0, 0, i * 30f + burst * 90f);
                    sparkle.localScale = Vector3.one * (Mathf.Sin(burst * Mathf.PI) * (i % 2 == 0 ? 1.1f : 0.7f));
                }
                yield return null;
            }
            portrait.gameObject.SetActive(false);
            carrier.color = Color.white;
            carrierRect.localScale = Vector3.one;
            carrierRect.localRotation = Quaternion.identity;
            carrierRect.anchoredPosition = startPosition;
            flash.gameObject.SetActive(false);
            halo.gameObject.SetActive(false);
            foreach (RectTransform sparkle in sparkles) sparkle.gameObject.SetActive(false);
            if (!completionSoundPlayed)
                PlayTransformationCompleteSound();
            yield return new WaitForSecondsRealtime(carrierHoldSeconds);
        }

        private void PlayTransformationSound() => FruitCatchAudioManager.Instance.PlayTransformation();

        private void PlayTransformationCompleteSound() => FruitCatchAudioManager.Instance.PlayTransformationComplete();

        private void OnDisable()
        {
            ReleaseSnapshot();
        }

        private void ReleaseSnapshot()
        {
            if (frozenFace != null) frozenFace.texture = null;
            if (snapshot != null) Destroy(snapshot);
            snapshot = null;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer))
                .GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            return rect;
        }
    }
}
