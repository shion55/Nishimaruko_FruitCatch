using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 顔・マウスが指す移動目標を、画面下部の上向き三角形で示します。
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class MovementTargetIndicator : MaskableGraphic
{
    [SerializeField] private CharacterPairController characterController;
    [SerializeField] private Camera gameplayCamera;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
            return;

        if (characterController == null || !characterController.isActiveAndEnabled ||
            gameplayCamera == null || !characterController.TryGetFollowTarget(out float targetX))
        {
            canvasRenderer.SetAlpha(0f);
            return;
        }

        RectTransform parent = rectTransform.parent as RectTransform;
        if (parent == null || canvas == null)
            return;

        Vector3 targetPosition = characterController.transform.position;
        targetPosition.x = targetX;
        Vector2 screenPosition = gameplayCamera.WorldToScreenPoint(targetPosition);
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parent, screenPosition, uiCamera, out Vector2 localPoint))
        {
            Vector2 position = rectTransform.anchoredPosition;
            position.x = localPoint.x - parent.rect.center.x;
            rectTransform.anchoredPosition = position;
            canvasRenderer.SetAlpha(1f);
        }
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect rect = GetPixelAdjustedRect();
        vertexHelper.AddVert(new Vector3(rect.xMin, rect.yMin), color, Vector2.zero);
        vertexHelper.AddVert(new Vector3(rect.center.x, rect.yMax), color, Vector2.zero);
        vertexHelper.AddVert(new Vector3(rect.xMax, rect.yMin), color, Vector2.zero);
        vertexHelper.AddTriangle(0, 1, 2);
    }
}
