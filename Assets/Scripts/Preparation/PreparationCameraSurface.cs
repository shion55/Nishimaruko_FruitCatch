using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitCatch.Preparation
{
    /// <summary>
    /// カメラ表示と説明画面を分離するための差し替え口です。
    /// 実カメラ連携を追加するまでは、フォールバック表示だけで動作します。
    /// </summary>
    public sealed class PreparationCameraSurface : MonoBehaviour
    {
        [SerializeField] private RawImage cameraImage;
        [SerializeField] private GameObject fallbackRoot;
        [SerializeField] private TMP_Text fallbackText;
        [SerializeField] private string unavailableMessage = "カメラ映像なし（開発モード）";

        public bool IsCameraAvailable { get; private set; }

        public void SetCameraTexture(Texture texture)
        {
            if (cameraImage != null)
            {
                cameraImage.texture = texture;
                UpdateCameraUv(texture);
            }

            SetCameraAvailable(texture != null);
        }

        private void UpdateCameraUv(Texture texture)
        {
            if (cameraImage == null || texture == null || texture.width <= 0 || texture.height <= 0)
                return;

            Rect displayRect = cameraImage.rectTransform.rect;
            if (displayRect.width <= 0f || displayRect.height <= 0f)
                return;

            float sourceAspect = (float)texture.width / texture.height;
            float displayAspect = displayRect.width / displayRect.height;
            Rect uv = new Rect(0f, 0f, 1f, 1f);

            // 画面を埋めつつ縦横比を維持する。切り抜いた範囲は枠判定にも使用する。
            if (sourceAspect > displayAspect)
            {
                uv.width = displayAspect / sourceAspect;
                uv.x = (1f - uv.width) * 0.5f;
            }
            else if (sourceAspect < displayAspect)
            {
                uv.height = sourceAspect / displayAspect;
                uv.y = (1f - uv.height) * 0.5f;
            }

            cameraImage.uvRect = uv;
        }

        public void SetCameraAvailable(bool available)
        {
            IsCameraAvailable = available;

            if (cameraImage != null)
                cameraImage.enabled = available && cameraImage.texture != null;

            if (fallbackRoot != null)
                fallbackRoot.SetActive(!available);

            if (fallbackText != null)
                fallbackText.text = unavailableMessage;
        }
    }
}
