
using System.Collections;
using TMPro;
using UnityEngine;

public class ScorePopup : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private TextMeshPro popupPrefab;

    [Header("Colors")]
    [SerializeField]
    private Color positiveColor =
        new Color(1f, 0.8f, 0.2f);

    [SerializeField]
    private Color negativeColor =
        new Color(1f, 0.3f, 0.4f);

    [Header("Movement")]
    [SerializeField] private float startY = -0.2f;
    [SerializeField] private float riseDistance = 0.8f;
    [SerializeField] private float horizontalSpread = 0.25f;

    [Header("Animation")]
    [SerializeField] private float duration = 0.8f;

    [Range(0.01f, 0.5f)]
    [SerializeField] private float fadeInRatio = 0.2f;

    [Range(0.5f, 0.99f)]
    [SerializeField] private float fadeOutStart = 0.55f;

    private int popupCount;

    public void ShowScore(int scoreDelta)
    {
        if (popupPrefab == null || scoreDelta == 0)
            return;

        TextMeshPro popup = Instantiate(
            popupPrefab, transform
        );

        popup.text = scoreDelta > 0
            ? $"+{scoreDelta}"
            : scoreDelta.ToString();

        Color color = scoreDelta > 0
            ? positiveColor
            : negativeColor;

        // 連続取得時に少し左右へずらす
        int lane = popupCount++ % 3 - 1;
        float offsetX = lane * horizontalSpread;

        Vector3 start = new Vector3(
            offsetX, startY, 0f
        );

        Vector3 end = start +
            Vector3.up * riseDistance;

        StartCoroutine(Animate(
            popup, start, end, color
        ));
    }

    private IEnumerator Animate(
        TextMeshPro popup,
        Vector3 start,
        Vector3 end,
        Color baseColor)
    {
        float elapsed = 0f;
        float lifetime = Mathf.Max(0.01f, duration);

        Vector3 originalScale = popup.transform.localScale;

        while (elapsed < lifetime)
        {
            float t = Mathf.Clamp01(elapsed / lifetime);

            // 上方向へ減速しながら移動
            float moveT = 1f - Mathf.Pow(1f - t, 3f);

            popup.transform.localPosition =
                Vector3.Lerp(start, end, moveT);

            // フェードイン → 表示 → フェードアウト
            float alpha;

            if (t < fadeInRatio)
            {
                alpha = Mathf.SmoothStep(
                    0f, 1f, t / fadeInRatio
                );
            }
            else if (t < fadeOutStart)
            {
                alpha = 1f;
            }
            else
            {
                alpha = 1f - Mathf.SmoothStep(
                    0f, 1f,
                    (t - fadeOutStart) /
                    (1f - fadeOutStart)
                );
            }

            Color c = baseColor;
            c.a *= alpha;
            popup.color = c;

            // 出現時に少し拡大
            float scale = Mathf.Lerp(
                0.8f, 1f,
                Mathf.SmoothStep(0f, 1f, t * 5f)
            );

            popup.transform.localScale =
                originalScale * scale;

            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(popup.gameObject);
    }
}
