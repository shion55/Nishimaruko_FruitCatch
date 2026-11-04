using System;
using System.Collections;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 登録シーン用: Pythonのカメラ映像をUnityのRawImageに表示し、
/// 顔が中央に3秒入ったら自動登録 → ゲームシーンに移動する。
/// 
/// 既存のゲーム中の顔X座標受信(UDP 5005)は変更しない。
/// このスクリプトは以前のFaceRegistrationController.csと置き換える。
/// </summary>
public class FaceRegistrationController : MonoBehaviour
{
    [Header("Pythonとの通信（同じPC）")]
    [SerializeField] private string pythonIp = "127.0.0.1";
    [SerializeField] private int commandPort = 5006;
    [SerializeField] private int statusPort = 5007;
    [SerializeField] private int previewHttpPort = 8765;
    [SerializeField, Range(1, 25)] private int previewFps = 15;

    [Header("登録画面UI")]
    [SerializeField] private RawImage cameraPreview;
    [SerializeField] private Image progressRing;
    [SerializeField] private TMP_Text guideText;
    [SerializeField] private GameObject registrationScreen;

    [Header("登録完了後（どちらかを指定）")]
    [Tooltip("別シーンへ移動する場合。Build Profiles/Scenes In Buildに登録したシーン名を入力")]
    [SerializeField] private string gameSceneName = "";
    [Tooltip("同一シーン内で切替える場合、ゲーム全体のルートを指定")]
    [SerializeField] private GameObject gameplayRoot;
    [SerializeField] private float successMessageSeconds = 0.4f;

    private UdpClient commandClient;
    private UdpClient statusClient;
    private Texture2D previewTexture;
    private bool autoRegistrationSent;
    private bool seenUnregisteredAfterArm;
    private bool transitioning;

    private void Awake()
    {
        commandClient = new UdpClient();
        try
        {
            // PythonもUnityも同じPCで動かす前提
            statusClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, statusPort));
            statusClient.Client.Blocking = false;
        }
        catch (SocketException exception)
        {
            Debug.LogError($"FaceRegistration: UDP {statusPort} を開けません: {exception.Message}");
            ShowGuide("状態受信用ポートを開けません");
        }
    }

    private void Start()
    {
        if (progressRing != null)
        {
            progressRing.type = Image.Type.Filled;
            progressRing.fillMethod = Image.FillMethod.Radial360;
            progressRing.fillOrigin = (int)Image.Origin360.Top;
            progressRing.fillClockwise = true;
            progressRing.fillAmount = 0f;
        }

        // ゲームを同一シーン内で切り替える場合は最初は非表示
        if (gameplayRoot != null && string.IsNullOrWhiteSpace(gameSceneName))
            gameplayRoot.SetActive(false);

        previewTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        if (cameraPreview != null)
            cameraPreview.texture = previewTexture;

        ShowGuide("カメラに接続しています…");
        StartCoroutine(PollPreview());
    }

    private void Update()
    {
        if (statusClient == null || transitioning) return;

        try
        {
            while (statusClient.Available > 0)
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes = statusClient.Receive(ref remote);
                if (!IPAddress.IsLoopback(remote.Address)) continue;
                string status = Encoding.ASCII.GetString(bytes).Trim();
                if (status == "REGISTERED")
                {
                    BeginGameTransition();
                    break;
                }
            }
        }
        catch (SocketException e)
        {
            // ノンブロッキングの一時的なエラーは読み飛ばす
            if (e.SocketErrorCode != SocketError.WouldBlock)
                Debug.LogWarning($"FaceRegistration: UDP状態受信エラー: {e.Message}");
        }
    }

    private IEnumerator PollPreview()
    {
        string url = $"http://{pythonIp}:{previewHttpPort}/frame.jpg";
        WaitForSecondsRealtime wait = new WaitForSecondsRealtime(1f / Mathf.Max(previewFps, 1));

        while (!transitioning)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 2;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    // Pythonが起動した後、最初にプレビューを受け取れたタイミングで自動登録開始。
                    // PythonよりUnityを先に起動した場合も正しく動作する。
                    if (!autoRegistrationSent)
                    {
                        autoRegistrationSent = SendCommand("AUTO");
                        if (autoRegistrationSent)
                            ShowGuide("顔を丸の中央に合わせてください");
                    }

                    byte[] jpeg = request.downloadHandler.data;
                    if (jpeg != null && jpeg.Length > 0 && previewTexture != null)
                    {
                        try
                        {
                            // テクスチャを毎フレーム生成せず、同じTexture2DへJPEGを書き込む
                            ImageConversion.LoadImage(previewTexture, jpeg, false);
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"FaceRegistration: JPEGデコード失敗: {e.Message}");
                        }
                    }

                    // 前回の登録がPythonに残っていても、今回AUTOで解除されたことを
                    // 一度確認するまでは「登録完了」に遷移しない。
                    string registeredHeader = request.GetResponseHeader("X-Registered");
                    if (autoRegistrationSent && registeredHeader == "0")
                        seenUnregisteredAfterArm = true;

                    string progressHeader = request.GetResponseHeader("X-Progress");
                    if (progressRing != null)
                    {
                        float progress = 0f;
                        if (seenUnregisteredAfterArm && float.TryParse(progressHeader,
                                NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                        {
                            progress = Mathf.Clamp01(parsed);
                        }
                        progressRing.fillAmount = progress;
                    }

                    // UDP通知を万一取り逃しても、HTTPヘッダーから登録完了を確認
                    if (seenUnregisteredAfterArm && registeredHeader == "1")
                    {
                        BeginGameTransition();
                    }
                    else if (!transitioning)
                    {
                        ShowGuide(GetInstruction(request.GetResponseHeader("X-Hint")));
                    }
                }
                else if (!transitioning)
                {
                    ShowGuide("Pythonのカメラ映像を待っています…");
                }
            }

            if (!transitioning)
                yield return wait;
        }
    }

    private static string GetInstruction(string hint)
    {
        switch (hint)
        {
            case "NO_FACE": return "顔をカメラに映してください";
            case "MULTIPLE_FACES": return "1人だけカメラに映ってください";
            case "MOVE_CLOSER": return "もう少しカメラに近づいてください";
            case "MOVE_BACK": return "少しカメラから離れてください";
            case "MOVE_CENTER": return "顔を丸の中央に合わせてください";
            case "HOLD": return "そのまま3秒間キープ！";
            case "REGISTER_FAILED": return "正面を向き、明るい場所で試してください";
            case "DONE": return "登録完了！";
            default: return "顔を丸の中央に合わせてください";
        }
    }

    private void BeginGameTransition()
    {
        if (transitioning) return;
        transitioning = true;
        if (progressRing != null)
            progressRing.fillAmount = 1f;
        ShowGuide("登録完了！ ゲームを開始します");
        StartCoroutine(SwitchToGame());
    }

    private IEnumerator SwitchToGame()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, successMessageSeconds));

        if (!string.IsNullOrWhiteSpace(gameSceneName))
        {
            // シーン名はBuild Profiles / Scenes In Buildに登録しておく
            SceneManager.LoadScene(gameSceneName);
        }
        else if (gameplayRoot != null)
        {
            gameplayRoot.SetActive(true);
            if (registrationScreen != null)
                registrationScreen.SetActive(false);
            else
                Debug.LogWarning("FaceRegistration: Registration Screenを設定してください");
        }
        else
        {
            Debug.LogError("FaceRegistration: Game Scene Name または Gameplay Root を指定してください");
        }
    }

    /// <summary>メニューに戻って別の人を登録する場合は、再表示後にこのメソッドを呼ぶ。</summary>
    public void RestartRegistration()
    {
        SendCommand("CLEAR");
        autoRegistrationSent = false;
        seenUnregisteredAfterArm = false;
        transitioning = false;
        if (progressRing != null)
            progressRing.fillAmount = 0f;
        ShowGuide("顔を丸の中央に合わせてください");
        StartCoroutine(PollPreview());
    }

    private bool SendCommand(string command)
    {
        if (commandClient == null) return false;
        try
        {
            byte[] bytes = Encoding.ASCII.GetBytes(command);
            commandClient.Send(bytes, bytes.Length, pythonIp, commandPort);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"FaceRegistration: Pythonへの送信に失敗: {exception.Message}");
            return false;
        }
    }

    private void ShowGuide(string message)
    {
        if (guideText != null && guideText.text != message)
            guideText.text = message;
    }

    private void OnDestroy()
    {
        statusClient?.Close();
        commandClient?.Close();
        if (cameraPreview != null && cameraPreview.texture == previewTexture)
            cameraPreview.texture = null;
        if (previewTexture != null)
            Destroy(previewTexture);

        // ここでCLEARを送るとシーン切替で登録情報まで消えてしまうため送らない
    }
}
