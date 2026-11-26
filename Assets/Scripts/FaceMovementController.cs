
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Globalization;

[DefaultExecutionOrder(-100)]
public class FaceMovementController : MonoBehaviour
{
    // Pythonから顔位置を受信し、移動側へ正規化した目標位置を渡す。
    [SerializeField] private int port = 5015;

    [SerializeField] private float inputMinX = 0.25f;
    [SerializeField] private float inputMaxX = 0.75f;

    [SerializeField, Min(0f)] private float trackingTimeout = 0.5f; // 顔位置の受信が途切れてから停止するまでの秒数。

    private UdpClient receiver;
    private IPEndPoint remoteEndPoint;

    private float faceX = 0.5f;
    private float lastReceivedTime;
    private bool hasReceivedFace;
    private bool timedOut;

    public bool movefacetoggle = true;
    void OnEnable()
    {
        hasReceivedFace = false;
        timedOut = false;
        lastReceivedTime = Time.unscaledTime;

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

    void Update()
    {
        if (receiver == null)
            return;

        // マウス操作中や顔移動の無効化中も受信し、古い位置を溜めない。
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
                lastReceivedTime = Time.unscaledTime;
                if (timedOut)
                    Debug.Log("[FruitCatch tracking] UDP positions resumed.");
                timedOut = false;
                hasReceivedFace = true;
            }
        }

        if (!timedOut && !movefacetoggle && Time.unscaledTime - lastReceivedTime > trackingTimeout)
        {
            timedOut = true;
            Debug.LogWarning("[FruitCatch tracking] No recent UDP position on port " + port);
        }
    }

    public bool TryGetNormalizedPosition(out float normalizedX)
    {
        normalizedX = 0.5f;
        if (!isActiveAndEnabled || movefacetoggle || !hasReceivedFace ||
            Time.unscaledTime - lastReceivedTime > Mathf.Max(0f, trackingTimeout))
            return false;

        normalizedX = Mathf.InverseLerp(
            inputMinX,
            inputMaxX,
            faceX
        );

        return true;
    }

    void OnDisable()
    {
        receiver?.Close();
        receiver = null;
        hasReceivedFace = false;
    }
}
