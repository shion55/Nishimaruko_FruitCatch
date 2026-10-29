
import cv2
import socket


CAMERA_INDEX = 0
UNITY_IP = "127.0.0.1"
UNITY_PORT = 5005

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# WindowsのWebカメラを使用
cap = cv2.VideoCapture(CAMERA_INDEX, cv2.CAP_DSHOW)
cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)

if not cap.isOpened():
    raise RuntimeError("カメラを開けません")

# 顔検出器
face_cascade = cv2.CascadeClassifier(
    cv2.data.haarcascades +
    "haarcascade_frontalface_default.xml"
)

if face_cascade.empty():
    raise RuntimeError("顔検出器を読み込めません")

try:
    while True:
        ret, frame = cap.read()
        if not ret:
            break

        # 鏡のように左右反転
        frame = cv2.flip(frame, 1)

        gray = cv2.cvtColor(
            frame, cv2.COLOR_BGR2GRAY
        )

        faces = face_cascade.detectMultiScale(
            gray,
            scaleFactor=1.15,
            minNeighbors=5,
            minSize=(60, 60)
        )

        if len(faces) > 0:
            # 複数検出時は最も大きい顔を使う
            x, y, w, h = max(
                faces, key=lambda f: f[2] * f[3]
            )

            center_x = x + w / 2
            normalized_x = center_x / frame.shape[1]

            # 0〜1のX座標をUnityへ送信
            message = f"{normalized_x:.4f}"
            sock.sendto(
                message.encode("utf-8"),
                (UNITY_IP, UNITY_PORT)
            )

            cv2.rectangle(
                frame,
                (x, y),
                (x + w, y + h),
                (0, 255, 0),
                2
            )

        cv2.imshow("Face Tracking", frame)

        if cv2.waitKey(1) & 0xFF == ord("q"):
            break

finally:
    cap.release()
    sock.close()
    cv2.destroyAllWindows()
