
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Globalization;

public class FaceMovementController : MonoBehaviour
{
    //pythonから顔の位置をOSCで受け反映させる
    [SerializeField] private int port = 5005;
    [SerializeField] private float smoothTime = 0.12f;

    [SerializeField] private float inputMinX = 0.25f;
    [SerializeField] private float inputMaxX = 0.75f;

    [SerializeField] private float horizontalMargin = 0.7f;
    [SerializeField] private float trackingTimeout = 0.5f;

    private UdpClient receiver;
    private IPEndPoint remoteEndPoint;
    private Camera mainCamera;

    private float faceX = 0.5f;
    private float velocityX;
    private float lastReceivedTime;
    private bool hasReceivedFace;

    void OnEnable()
    {
        try
        {
            receiver = new UdpClient(
                new IPEndPoint(IPAddress.Loopback, port)
            );
            remoteEndPoint = new IPEndPoint(
                IPAddress.Any, 0
            );
        }
        catch (SocketException e)
        {
            Debug.LogError("UDP受信エラー: " + e.Message);
            enabled = false;
        }
    }

    void Start()
    {
        mainCamera = Camera.main;
    }

    void Update()
    {
        if (receiver == null || mainCamera == null)
            return;

        // 最新の顔位置を取得
        while (receiver.Available > 0)
        {
            byte[] data = receiver.Receive(
                ref remoteEndPoint
            );

            string message = Encoding.UTF8.GetString(data);

            if (float.TryParse(
                message,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float x
            ) && !float.IsNaN(x) && !float.IsInfinity(x))
            {
                faceX = Mathf.Clamp01(x);
                lastReceivedTime = Time.time;
                hasReceivedFace = true;
            }
        }

        // 顔が見つからなくなったら停止
        if (!hasReceivedFace ||
            Time.time - lastReceivedTime > trackingTimeout)
            return;

        float normalized = Mathf.InverseLerp(
            inputMinX,
            inputMaxX,
            faceX
        );

        float halfWidth =
            mainCamera.orthographicSize *
            mainCamera.aspect;

        float limit = Mathf.Max(
            0f, halfWidth - horizontalMargin
        );

        float targetX = Mathf.Lerp(
            mainCamera.transform.position.x - limit,
            mainCamera.transform.position.x + limit,
            normalized
        );

        Vector3 position = transform.position;

        position.x = Mathf.SmoothDamp(
            position.x,
            targetX,
            ref velocityX,
            smoothTime
        );

        transform.position = position;
    }

    void OnDisable()
    {
        receiver?.Close();
        receiver = null;
    }
}
