using System;
using System.Collections;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace FruitCatch.Preparation
{
    /// <summary>
    /// Pythonが配信するカメラ映像と判定結果をPreparationへ接続します。
    /// カメラをUnity側で二重に開かないため、映像も判定も同じPythonプロセスを使用します。
    /// </summary>
    public sealed class PreparationPythonBridge : MonoBehaviour
    {
        [Header("Pythonとの通信")]
        [SerializeField] private string pythonIp = "127.0.0.1";
        [SerializeField] private int previewHttpPort = 8765;
        [SerializeField] private int commandPort = 5006;
        [SerializeField, Range(1, 25)] private int previewFps = 15;
        [SerializeField, Min(1)] private int requestTimeoutSeconds = 2;

        [Header("接続先")]
        [SerializeField] private PreparationFlowController flow;
        [SerializeField] private PreparationCameraSurface cameraSurface;

        private Texture2D previewTexture;
        private UdpClient commandClient;
        private bool faceRegistrationArmed;
        private float nextFaceCommandTime;
        private string registrationId;
        private string registrationCommand;
        private PreparationFacePresentation presentation;
        private bool wasFaceStep;
        private Rect guideUv;

        private void Reset()
        {
            flow = GetComponent<PreparationFlowController>();
        }

        private void Awake()
        {
            if (flow == null)
                flow = GetComponent<PreparationFlowController>();

            previewTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            commandClient = new UdpClient();
        }

        private void OnEnable()
        {
            StartCoroutine(PollPreview());
        }

        private void Update()
        {
            if (flow == null)
                return;

            bool isFaceStep = flow.CurrentRequiredSignal == PreparationSignal.FaceAligned &&
                flow.CurrentPanel != null && flow.CurrentPanel.activeInHierarchy;
            if (isFaceStep && !flow.IsTransitioning)
            {
                if (!wasFaceStep)
                {
                    presentation = flow.CurrentPanel.GetComponentInChildren<PreparationFacePresentation>();
                    Canvas.ForceUpdateCanvases();
                    BeginRegistration();
                }
                else if (presentation != null && GuideChanged(presentation.GetGuideUv()))
                    BeginRegistration();

                if (!faceRegistrationArmed && Time.unscaledTime >= nextFaceCommandTime)
                {
                    SendCommand(registrationCommand);
                    nextFaceCommandTime = Time.unscaledTime + 1f;
                }
                wasFaceStep = true;
            }
            else if (!isFaceStep)
            {
                wasFaceStep = false;
                faceRegistrationArmed = false;
                nextFaceCommandTime = 0f;
                registrationId = null;
                presentation = null;
            }
        }

        private bool GuideChanged(Rect current)
        {
            const float tolerance = 0.0001f;
            return Mathf.Abs(current.x - guideUv.x) > tolerance || Mathf.Abs(current.y - guideUv.y) > tolerance ||
                Mathf.Abs(current.width - guideUv.width) > tolerance || Mathf.Abs(current.height - guideUv.height) > tolerance;
        }

        private void BeginRegistration()
        {
            registrationId = Guid.NewGuid().ToString("N").ToUpperInvariant();
            guideUv = presentation != null ? presentation.GetGuideUv() : new Rect(0.25f, 0.15f, 0.5f, 0.7f);
            registrationCommand = string.Format(CultureInfo.InvariantCulture,
                "AUTO_REGION {0} {1:F5} {2:F5} {3:F5} {4:F5}", registrationId,
                guideUv.x, 1f - guideUv.yMax, guideUv.width, guideUv.height);
            faceRegistrationArmed = false;
            nextFaceCommandTime = 0f;
            flow.ReportFaceAligned(false);
            presentation?.ResetPresentation();
        }

        private IEnumerator PollPreview()
        {
            string url = $"http://{pythonIp}:{previewHttpPort}/frame.jpg";
            WaitForSecondsRealtime wait = new WaitForSecondsRealtime(1f / Mathf.Max(1, previewFps));

            while (enabled)
            {
                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    request.timeout = requestTimeoutSeconds;
                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        byte[] jpeg = request.downloadHandler.data;
                        if (jpeg != null && jpeg.Length > 0)
                        {
                            try
                            {
                                ImageConversion.LoadImage(previewTexture, jpeg, false);
                                cameraSurface?.SetCameraTexture(previewTexture);
                            }
                            catch (Exception exception)
                            {
                                Debug.LogWarning($"PreparationPythonBridge: 映像の読み込みに失敗しました: {exception.Message}");
                            }
                        }

                        bool handsRaised = request.GetResponseHeader("X-Hands-Raised") == "1";
                        bool currentRegistration = registrationId != null &&
                            request.GetResponseHeader("X-Registration-Id") == registrationId;
                        bool faceAligned = currentRegistration && request.GetResponseHeader("X-Registered") == "1";
                        string hint = request.GetResponseHeader("X-Hint");
                        if (currentRegistration)
                        {
                            faceRegistrationArmed = true;
                            float.TryParse(request.GetResponseHeader("X-Progress"), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out float progress);
                            presentation?.SetStatus(hint, Mathf.Clamp01(progress));
                            if (faceAligned) presentation?.Capture(previewTexture);
                        }
                        else
                            faceRegistrationArmed = false;
                        flow?.ReportHandsRaised(handsRaised);
                        flow?.ReportFaceAligned(faceAligned);
                    }
                    else
                    {
                        faceRegistrationArmed = false;
                        cameraSurface?.SetCameraAvailable(false);
                        flow?.ReportHandsRaised(false);
                        flow?.ReportFaceAligned(false);
                        presentation?.SetStatus("OFFLINE", 0f);
                    }
                }

                yield return wait;
            }
        }

        private bool SendCommand(string command)
        {
            try
            {
                byte[] bytes = Encoding.ASCII.GetBytes(command);
                commandClient.Send(bytes, bytes.Length, pythonIp, commandPort);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"PreparationPythonBridge: Pythonへの送信に失敗しました: {exception.Message}");
                return false;
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            flow?.ReportHandsRaised(false);
            flow?.ReportFaceAligned(false);
        }

        private void OnDestroy()
        {
            commandClient?.Close();
            if (previewTexture != null)
                Destroy(previewTexture);
        }
    }
}
