
using UnityEngine;
using UnityEngine.InputSystem;

public class CharacterPairController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 5f; // 横移動速度の上限（units/秒）。
    [SerializeField, Min(0.01f)] private float followSmoothTime = 0.2f; // 追従の滑らかさ（秒）。大きいほど緩やか。
    [SerializeField, Min(0f)] private float horizontalMargin = 0.7f;

    [Header("Fever")]
    [SerializeField] private FallingItemSpawner fallingItemSpawner;
    [SerializeField, Min(1f)] private float feverSpeedMultiplier = 1.5f; // 速度上限と追従の応答を速める倍率。

    [Header("Chili")]
    [SerializeField, Min(0.1f)] private float chiliDuration = 5f; // 効果時間（秒）。
    [SerializeField, Min(1f)] private float chiliSpeedMultiplier = 1.6f;
    [SerializeField, Min(0f)] private float chiliSwayAmplitude = 0.45f; // 自動揺れの幅（units）。
    [SerializeField, Min(0f)] private float chiliSwayFrequency = 2f; // 自動揺れの周波数（Hz）。
    [Header("Banana Slide")]
    [SerializeField, Min(0.1f)] private float slideSpeed = 12f; // 壁への滑走速度（units/秒）。

    private readonly TemporaryEffectTimer chiliTimer = new TemporaryEffectTimer();
    private float swayTime;
    private float swayOffset;
    private int slideDirection;
    private bool movementEnabled = true;
    public bool IsChiliActive => chiliTimer.IsActive;
    public float ChiliRemainingTime => chiliTimer.Remaining;
    public bool IsSliding => slideDirection != 0;
    public event System.Action ReachedBananaWall;
    private float FeverMultiplier => fallingItemSpawner != null && fallingItemSpawner.IsFever
        ? Mathf.Max(1f, feverSpeedMultiplier) : 1f;
    private float EffectiveMoveSpeed => moveSpeed * FeverMultiplier * (IsChiliActive ? chiliSpeedMultiplier : 1f);
    private float EffectiveFollowSmoothTime => Mathf.Max(0.01f, followSmoothTime / FeverMultiplier);

    private enum InputSource
    {
        None,
        Face,
        Mouse,
        Keyboard
    }

    private Camera mainCamera;
    private FaceMovementController faceMovement;
    private InputSource inputSource;
    private float followVelocityX;

    private void Awake()
    {
        mainCamera = Camera.main;
        faceMovement = GetComponent<FaceMovementController>();
    }

    private void OnEnable()
    {
        inputSource = InputSource.None;
        followVelocityX = 0f;
    }

    private void Update()
    {
        if (!movementEnabled || Time.deltaTime <= 0f)
            return;
        RemoveSwayOffset();
        chiliTimer.Tick(Time.deltaTime);
        swayTime += Time.deltaTime;
        if (IsSliding)
        {
            SetInputSource(InputSource.None);
            float wall = mainCamera.transform.position.x + slideDirection * GetHorizontalLimit();
            SetPositionX(Mathf.MoveTowards(transform.position.x, wall, slideSpeed * Time.deltaTime));
            if (Mathf.Abs(transform.position.x - wall) < 0.001f)
            {
                slideDirection = 0;
                ReachedBananaWall?.Invoke();
            }
            return;
        }
        UpdateInputMovement();
        if (IsChiliActive)
        {
            float before = transform.position.x;
            SetPositionX(before + Mathf.Sin(swayTime * chiliSwayFrequency * Mathf.PI * 2f) * chiliSwayAmplitude);
            swayOffset = transform.position.x - before;
        }
    }

    private void UpdateInputMovement()
    {
        if (Time.deltaTime <= 0f)
            return;

        if (mainCamera == null)
            mainCamera = Camera.main;

        if (moveSpeed <= 0f)
        {
            SetInputSource(InputSource.None);
            return;
        }

        if (MouseDebugInput.Enabled)
        {
            if (TryGetFollowTarget(out float mouseX))
                FollowTarget(mouseX, InputSource.Mouse);
            else
                SetInputSource(InputSource.None);

            return;
        }

        float keyboardInput = ReadKeyboardInput();
        if (keyboardInput != 0f)
        {
            // キーを押している間は顔入力より優先し、同じ座標を上書きしない。
            SetInputSource(InputSource.Keyboard);
            SetPositionX(transform.position.x + keyboardInput * EffectiveMoveSpeed * Time.deltaTime);
            return;
        }

        if (TryGetFollowTarget(out float faceTargetX))
        {
            FollowTarget(faceTargetX, InputSource.Face);
            return;
        }

        // 顔の追跡切れやマウスの範囲外では、慣性を残さず停止する。
        SetInputSource(InputSource.None);
    }

    public void StartChili()
    {
        if (movementEnabled)
            chiliTimer.Refresh(chiliDuration);
    }

    public void StartBananaSlide(int direction)
    {
        if (!movementEnabled || direction == 0 || mainCamera == null)
            return;
        RemoveSwayOffset();
        slideDirection = direction < 0 ? -1 : 1;
        SetInputSource(InputSource.None);
    }

    public void SetMovementEnabled(bool value)
    {
        movementEnabled = value;
        StopTemporaryEffects();
    }

    public void StopTemporaryEffects()
    {
        RemoveSwayOffset();
        chiliTimer.Clear();
        slideDirection = 0;
        SetInputSource(InputSource.None);
    }

    private void RemoveSwayOffset()
    {
        if (swayOffset != 0f)
        {
            SetPositionX(transform.position.x - swayOffset);
            swayOffset = 0f;
        }
    }

    private void OnDisable()
    {
        StopTemporaryEffects();
    }

    public bool TryGetFollowTarget(out float targetX)
    {
        targetX = transform.position.x;
        if (mainCamera == null)
            mainCamera = Camera.main;
        if (mainCamera == null)
            return false;

        if (MouseDebugInput.Enabled)
            return MouseDebugInput.TryGetHorizontalPosition(mainCamera, horizontalMargin, out targetX);

        if (faceMovement == null || !faceMovement.TryGetNormalizedPosition(out float normalizedX))
            return false;

        float limit = GetHorizontalLimit();
        float centerX = mainCamera.transform.position.x;
        targetX = Mathf.Lerp(centerX - limit, centerX + limit, normalizedX);
        return true;
    }

    private static float ReadKeyboardInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return 0f;

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

        return input;
    }

    private void FollowTarget(float targetX, InputSource source)
    {
        SetInputSource(source);

        float nextX = Mathf.SmoothDamp(
            transform.position.x,
            ClampPositionX(targetX),
            ref followVelocityX,
            EffectiveFollowSmoothTime,
            EffectiveMoveSpeed,
            Time.deltaTime
        );

        SetPositionX(nextX);
    }

    private void SetInputSource(InputSource source)
    {
        if (source != inputSource || source == InputSource.None)
            followVelocityX = 0f;

        inputSource = source;
    }

    private void SetPositionX(float x)
    {
        // 初期高さはCharactorParentのTransformで調整し、移動では横位置だけを変える。
        Vector3 position = transform.position;
        position.x = ClampPositionX(x);
        if (position.x != x)
            followVelocityX = 0f;

        transform.position = position;
    }

    private float ClampPositionX(float x)
    {
        if (mainCamera == null)
            return x;

        float centerX = mainCamera.transform.position.x;
        float limit = GetHorizontalLimit();
        return Mathf.Clamp(x, centerX - limit, centerX + limit);
    }

    private float GetHorizontalLimit()
    {
        return Mathf.Max(0f,
            mainCamera.orthographicSize * mainCamera.aspect - Mathf.Max(0f, horizontalMargin));
    }
}
