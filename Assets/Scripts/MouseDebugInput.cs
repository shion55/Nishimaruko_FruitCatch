using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// EditorとDevelopment Buildでは、F3でマウスを実機入力の代わりに使います。
/// シーンをまたいで状態を保持し、Play開始時には通常入力へ戻します。
/// </summary>
public static class MouseDebugInput
{
    internal static bool Current => Enabled;
    internal static void SetEnabled(bool active) => enabled = active;
    private static bool enabled;
    private static int lastToggleFrame = -1;
    private static float mouthOpenAmount = 1f;
    private static int lastMouthAdjustmentFrame = -1;

    public static bool Enabled
    {
        get
        {
            if (!Application.isPlaying)
                return false;

            if (lastToggleFrame != Time.frameCount && (Application.isEditor || Debug.isDebugBuild))
            {
                lastToggleFrame = Time.frameCount;
                if (Application.isFocused && Keyboard.current != null &&
                    Keyboard.current.f3Key.wasPressedThisFrame)
                {
                    enabled = !enabled;
                }
            }

            return enabled;
        }
    }

    public static bool LeftButtonHeld =>
        TryGetMouse(out Mouse mouse) && mouse.leftButton.isPressed;

    public static bool RightButtonHeld =>
        TryGetMouse(out Mouse mouse) && mouse.rightButton.isPressed;

    public static bool LeftButtonPressed =>
        TryGetMouse(out Mouse mouse) && mouse.leftButton.wasPressedThisFrame;

    public static bool RightButtonPressed =>
        TryGetMouse(out Mouse mouse) && mouse.rightButton.wasPressedThisFrame;

    public static float MouthOpenAmount
    {
        get
        {
            if (lastMouthAdjustmentFrame != Time.frameCount)
            {
                lastMouthAdjustmentFrame = Time.frameCount;
                if (TryGetMouse(out Mouse mouse) && mouse.leftButton.isPressed)
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    if (scroll != 0f)
                        mouthOpenAmount = Mathf.Clamp01(mouthOpenAmount + Mathf.Sign(scroll) * 0.1f);
                }
            }
            return mouthOpenAmount;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        enabled = false;
        lastToggleFrame = -1;
        mouthOpenAmount = 1f;
        lastMouthAdjustmentFrame = -1;
    }

    public static bool TryGetHorizontalPosition(Camera camera, float margin, out float worldX)
    {
        worldX = 0f;
        if (camera == null || !TryGetMouse(out Mouse mouse))
            return false;

        Rect viewport = camera.pixelRect;
        Vector2 pointer = mouse.position.ReadValue();
        if (viewport.width <= 0f || !viewport.Contains(pointer))
            return false;

        float normalizedX = Mathf.InverseLerp(viewport.xMin, viewport.xMax, pointer.x);
        float limit = Mathf.Max(0f, camera.orthographicSize * camera.aspect - margin);
        worldX = Mathf.Lerp(camera.transform.position.x - limit,
            camera.transform.position.x + limit, normalizedX);
        return true;
    }

    // 右ボタンを押している間、画面下端を低音(0)、上端を高音(1)として返す。
    public static bool TryGetNormalizedPitch(out float normalizedPitch)
    {
        normalizedPitch = 0f;
        if (!TryGetMouse(out Mouse mouse) || !mouse.rightButton.isPressed)
            return false;

        normalizedPitch = Mathf.InverseLerp(0f, Mathf.Max(1f, Screen.height - 1f),
            mouse.position.ReadValue().y);
        return true;
    }

    private static bool TryGetMouse(out Mouse mouse)
    {
        mouse = Mouse.current;
        if (!Enabled || FruitCatchOperatorPanel.BlocksGameplayInput || !Application.isFocused ||
            mouse == null || !mouse.enabled)
            return false;

        Vector2 pointer = mouse.position.ReadValue();
        return pointer.x >= 0f && pointer.x < Screen.width &&
               pointer.y >= 0f && pointer.y < Screen.height;
    }
}
