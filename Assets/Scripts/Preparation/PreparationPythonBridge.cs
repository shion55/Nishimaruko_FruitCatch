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
        [SerializeField, Range(1, 60)] private int previewFps = 30;
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
        private Texture2D registrationTexture;
        private UnityWebRequest activeRequest;
        private string lastFrame;
        private string capturedRegistrationId;
        private bool previewAvailable;
        private string lastWarning;
        private double metricsStartedAt;
        private int displayedFrames;

        private void Reset()
        {
            flow = GetComponent<PreparationFlowController>();
        }

        private void Awake()
        {
            if (flow == null)
                flow = GetComponent<PreparationFlowController>();

            previewTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            registrationTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            commandClient = new UdpClient();
        }

        private void OnEnable()
        {
            lastFrame = null;
            displayedFrames = 0;
            metricsStartedAt = Time.realtimeSinceStartupAsDouble;
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
            capturedRegistrationId = null;
            nextFaceCommandTime = 0f;
            flow.ReportFaceAligned(false);
            presentation?.ResetPresentation();
        }

        private IEnumerator PollPreview()
        {
            string origin = $"http://{pythonIp}:{previewHttpPort}";
            double period = 1.0 / Mathf.Max(1, previewFps);
            double due = Time.realtimeSinceStartupAsDouble;

            while (isActiveAndEnabled)
            {
                double startedAt = Time.realtimeSinceStartupAsDouble;
                if (startedAt + 0.001 < due)
                {
                    yield return null;
                    continue;
                }
                due = Math.Max(due + period, startedAt);
                string url = $"{origin}/frame.jpg?after={lastFrame}";
                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    activeRequest = request;
                    request.timeout = requestTimeoutSeconds;
                    yield return request.SendWebRequest();
                    activeRequest = null;

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        string key = request.GetResponseHeader("X-Stream-Id") + "-" +
                            request.GetResponseHeader("X-Frame-Sequence");
                        if (key != lastFrame)
                        {
                            if (TryLoadImage(previewTexture, request.downloadHandler.data, "live"))
                            {
                                cameraSurface?.SetCameraTexture(previewTexture);
                                lastFrame = key;
                                previewAvailable = true;
                                lastWarning = null;
                                displayedFrames++;
                            }
                            else
                            {
                                previewAvailable = false;
                                cameraSurface?.SetCameraAvailable(false);
                            }
                        }
                        ReportMetrics(request, startedAt);

                        bool handsRaised = previewAvailable && request.GetResponseHeader("X-Hands-Raised") == "1";
                        bool currentRegistration = registrationId != null &&
                            request.GetResponseHeader("X-Registration-Id") == registrationId;
                        bool registered = currentRegistration && request.GetResponseHeader("X-Registered") == "1";
                        string hint = request.GetResponseHeader("X-Hint");
                        if (currentRegistration)
                        {
                            faceRegistrationArmed = hint != "SNAPSHOT_ERROR";
                            if (!faceRegistrationArmed)
                                BeginRegistration();
                            float.TryParse(request.GetResponseHeader("X-Progress"), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out float progress);
                            presentation?.SetStatus(hint, Mathf.Clamp01(progress));
                        }
                        else
                            faceRegistrationArmed = false;
                        flow?.ReportHandsRaised(handsRaised);
                        if (registered && capturedRegistrationId != registrationId)
                            yield return FetchRegistrationImage(origin, registrationId);
                        flow?.ReportFaceAligned(registrationId != null && capturedRegistrationId == registrationId);
                    }
                    else
                    {
                        faceRegistrationArmed = false;
                        previewAvailable = false;
                        lastFrame = null;
                        cameraSurface?.SetCameraAvailable(false);
                        flow?.ReportHandsRaised(false);
                        flow?.ReportFaceAligned(false);
                        presentation?.SetStatus("OFFLINE", 0f);
                        WarnOnce($"HTTP {request.responseCode} {request.GetResponseHeader("X-Hint")} ({request.error})");
                    }
                }
            }
        }

        private IEnumerator FetchRegistrationImage(string origin, string id)
        {
            using UnityWebRequest request = UnityWebRequest.Get($"{origin}/registration.jpg?id={id}");
            activeRequest = request;
            request.timeout = requestTimeoutSeconds;
            yield return request.SendWebRequest();
            activeRequest = null;
            if (id != registrationId)
                yield break;
            if (request.result == UnityWebRequest.Result.Success &&
                request.GetResponseHeader("X-Registration-Id") == id &&
                TryLoadImage(registrationTexture, request.downloadHandler.data, "registration"))
            {
                presentation?.Capture(registrationTexture, guideUv);
                capturedRegistrationId = id;
            }
        }

        private bool TryLoadImage(Texture2D texture, byte[] jpeg, string source)
        {
            try
            {
                if (jpeg != null && jpeg.Length > 0 && ImageConversion.LoadImage(texture, jpeg, false))
                    return true;
                WarnOnce($"JPEG decode failed ({source})");
            }
            catch (Exception exception)
            {
                WarnOnce($"JPEG decode failed ({source}): {exception.Message}");
            }
            return false;
        }

        private void WarnOnce(string message)
        {
            if (lastWarning == message)
                return;
            lastWarning = message;
            Debug.LogWarning($"[FruitCatch preview] {message}");
        }

        private void ReportMetrics(UnityWebRequest request, double startedAt)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double interval = now - metricsStartedAt;
            if (interval < 5)
                return;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[FruitCatch preview] capture={0} inference={1} JPEG={2} Unity={3:F1} fps; frame_age_at_server={4} ms; request_to_display={5:F1} ms",
                request.GetResponseHeader("X-capture-fps"), request.GetResponseHeader("X-inference-fps"),
                request.GetResponseHeader("X-jpeg-fps"), displayedFrames / interval,
                request.GetResponseHeader("X-Frame-Age-Ms"), (now - startedAt) * 1000));
            displayedFrames = 0;
            metricsStartedAt = now;
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
            activeRequest?.Abort();
            StopAllCoroutines();
            activeRequest?.Dispose();
            activeRequest = null;
            flow?.ReportHandsRaised(false);
            flow?.ReportFaceAligned(false);
        }

        private void OnDestroy()
        {
            commandClient?.Close();
            if (previewTexture != null)
                Destroy(previewTexture);
            if (registrationTexture != null)
                Destroy(registrationTexture);
        }
    }
}
