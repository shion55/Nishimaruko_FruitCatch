using TMPro;
using UnityEngine;

public class ScorePopup : MonoBehaviour
{
    [Header("Popup")]
    [SerializeField] private bool popupEnabled = true;
    [SerializeField] private TextMeshPro popupPrefab;
    [SerializeField, Min(1f)] private float fontSize = 6f;
    [SerializeField, Range(1, 6)] private int maxVisible = 4;

    [Header("Colors")]
    [SerializeField] private Color positiveColor = new Color(1f, 0.84f, 0.39f);
    [SerializeField] private Color negativeColor = new Color(1f, 0.39f, 0.45f);

    [Header("Fever")]
    [SerializeField] private Color feverPositiveColor = new Color(1f, 0.94f, 0.35f);
    [SerializeField, Min(1f)] private float feverScaleMultiplier = 1.2f;

    [Header("Movement")]
    [SerializeField] private Vector2 spawnOffset = new Vector2(0f, 0.42f);
    [SerializeField, Min(0f)] private float riseDistance = 0.95f;
    [SerializeField, Min(0f)] private float horizontalSpread = 0.32f;

    [Header("Animation")]
    [SerializeField, Min(0.05f)] private float duration = 0.65f;
    [SerializeField, Range(0.01f, 0.5f)] private float fadeInRatio = 0.12f;
    [SerializeField, Range(0.5f, 0.99f)] private float fadeOutStart = 0.45f;

    private struct Popup
    {
        public TextMeshPro text;
        public Vector3 origin;
        public Color color;
        public float elapsed;
        public bool fever;
    }

    private Popup[] popups;
    private int nextPopup;
    private Material outlineMaterial;
    private Camera view;

    private void Awake() => view = Camera.main;

    public void ShowScore(int scoreDelta, Vector3 mouthPosition, bool fever = false)
    {
        if (!isActiveAndEnabled || !popupEnabled || popupPrefab == null || scoreDelta == 0)
            return;

        popups ??= new Popup[Mathf.Clamp(maxVisible, 1, 6)];
        int index = nextPopup;
        nextPopup = (nextPopup + 1) % popups.Length;
        ref Popup popup = ref popups[index];
        if (popup.text == null)
            popup.text = CreatePopup();

        int lane = (index + 1) / 2 * (index % 2 == 0 ? -1 : 1);
        popup.origin = mouthPosition + (Vector3)(spawnOffset + Vector2.right * (lane * horizontalSpread));
        if (view != null && view.orthographic)
        {
            float limit = Mathf.Max(0f, view.orthographicSize * view.aspect - 0.5f);
            float centerX = view.transform.position.x;
            popup.origin.x = Mathf.Clamp(popup.origin.x, centerX - limit, centerX + limit);
        }
        popup.fever = fever && scoreDelta > 0;
        popup.color = scoreDelta < 0 ? negativeColor : popup.fever ? feverPositiveColor : positiveColor;
        popup.elapsed = 0f;
        popup.text.text = scoreDelta > 0 ? $"+{scoreDelta}" : scoreDelta.ToString();
        popup.text.fontSize = fontSize;
        ApplyFrame(in popup, 0f);
        popup.text.gameObject.SetActive(true);
    }

    private TextMeshPro CreatePopup()
    {
        TextMeshPro text = Instantiate(popupPrefab, transform.root);
        if (outlineMaterial == null && popupPrefab.fontSharedMaterial != null)
        {
            outlineMaterial = new Material(popupPrefab.fontSharedMaterial);
            outlineMaterial.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(31, 17, 57, 255));
            outlineMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.24f);
        }
        if (outlineMaterial != null)
        {
            text.fontSharedMaterial = outlineMaterial;
            text.UpdateMeshPadding();
        }
        return text;
    }

    private void LateUpdate()
    {
        if (popups == null)
            return;
        if (!popupEnabled)
        {
            Clear();
            return;
        }

        float lifetime = Mathf.Max(0.05f, duration);
        for (int i = 0; i < popups.Length; i++)
        {
            ref Popup popup = ref popups[i];
            if (popup.text == null || !popup.text.gameObject.activeSelf)
                continue;

            popup.elapsed += Time.unscaledDeltaTime;
            if (popup.elapsed >= lifetime)
                popup.text.gameObject.SetActive(false);
            else
                ApplyFrame(in popup, popup.elapsed / lifetime);
        }
    }

    private void ApplyFrame(in Popup popup, float progress)
    {
        float remaining = 1f - progress;
        float lift = 1f - remaining * remaining * remaining;
        popup.text.transform.position = popup.origin + Vector3.up * (riseDistance * lift);

        float appear = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, fadeInRatio, progress));
        float disappear = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeOutStart, 1f, progress));
        Color color = popup.color;
        color.a *= appear * (1f - disappear);
        popup.text.color = color;

        float pop = Mathf.Clamp01(progress * 5f);
        float scale = Mathf.Lerp(0.68f, 1f, pop) + 0.22f * Mathf.Sin(pop * Mathf.PI);
        if (popup.fever)
            scale *= Mathf.Max(1f, feverScaleMultiplier);
        popup.text.transform.localScale = popupPrefab.transform.localScale * scale;
    }

    internal void Clear()
    {
        nextPopup = 0;
        if (popups == null)
            return;
        foreach (Popup popup in popups)
            if (popup.text != null && popup.text.gameObject.activeSelf)
                popup.text.gameObject.SetActive(false);
    }

    private void OnDisable() => Clear();

    private void OnDestroy()
    {
        if (popups != null)
            foreach (Popup popup in popups)
                if (popup.text != null)
                    Destroy(popup.text.gameObject);
        if (outlineMaterial != null)
            Destroy(outlineMaterial);
    }
}
