
using UnityEngine;
using UnityEngine.InputSystem;

public class CharacterPairController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float horizontalMargin = 0.7f;

    private Camera mainCamera;

    private void Awake()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        if (MouseDebugInput.Enabled)
        {
            if (MouseDebugInput.TryGetHorizontalPosition(mainCamera, horizontalMargin, out float mouseX))
            {
                Vector3 mousePosition = transform.position;
                mousePosition.x = mouseX;
                transform.position = mousePosition;
            }
            return;
        }

        Keyboard keyboard = Keyboard.current;

        if (keyboard == null) return;

        float input = 0f;

        if (keyboard.aKey.isPressed ||
            keyboard.leftArrowKey.isPressed)
        {
            input -= 1f;
        }

        if (keyboard.dKey.isPressed ||
            keyboard.rightArrowKey.isPressed)
        {
            input += 1f;
        }

        Vector3 position = transform.position;

        position.x += input * moveSpeed * Time.deltaTime;

        // 画面外に出ないようにする
        if (mainCamera != null)
        {
            float halfWidth =
                mainCamera.orthographicSize *
                mainCamera.aspect;

            float limit = Mathf.Max(
                0f,
                halfWidth - horizontalMargin
            );

            position.x = Mathf.Clamp(
                position.x,
                -limit,
                limit
            );
        }

        transform.position = position;
    }
}
