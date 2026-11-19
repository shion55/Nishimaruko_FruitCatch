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
                cameraImage.texture = texture;

            SetCameraAvailable(texture != null);
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
