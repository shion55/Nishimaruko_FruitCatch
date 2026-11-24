using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FruitCatch.Preparation
{
    /// <summary>
    /// スプライトを用意せず、角丸背景と点線枠を描画します。
    /// 色・角丸・点線はInspectorから調整できます。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RoundedDashedPanelGraphic : MaskableGraphic
    {
        [SerializeField] private Color fillColor = new Color(0.87f, 0.96f, 1f, 0.96f);
        [SerializeField] private Color borderColor = new Color(0.95f, 0.48f, 0.12f, 1f);
        [SerializeField, Min(0f)] private float cornerRadius = 32f;
        [SerializeField, Min(0f)] private float borderThickness = 7f;
        [SerializeField, Min(0f)] private float bottomBorderExtra;
        [SerializeField, Min(1f)] private float dashLength = 22f;
        [SerializeField, Min(0f)] private float gapLength = 14f;
        [SerializeField, Range(2f, 12f)] private float curveStep = 5f;

        public Color FillColor { get => fillColor; set { fillColor = value; SetVerticesDirty(); } }
        public Color BorderColor { get => borderColor; set { borderColor = value; SetVerticesDirty(); } }
        public float CornerRadius { get => cornerRadius; set { cornerRadius = Mathf.Max(0f, value); SetVerticesDirty(); } }
        public float BorderThickness { get => borderThickness; set { borderThickness = Mathf.Max(0f, value); SetVerticesDirty(); } }
        public float BottomBorderExtra { get => bottomBorderExtra; set { bottomBorderExtra = Mathf.Max(0f, value); SetVerticesDirty(); } }
        public float DashLength { get => dashLength; set { dashLength = Mathf.Max(1f, value); SetVerticesDirty(); } }
        public float GapLength { get => gapLength; set { gapLength = Mathf.Max(0f, value); SetVerticesDirty(); } }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = GetPixelAdjustedRect();
            float radius = Mathf.Min(cornerRadius, Mathf.Min(rect.width, rect.height) * 0.5f);
            float perimeterLength = GetPerimeterLength(rect, radius);

            if (rect.width <= 0f || rect.height <= 0f || perimeterLength <= 0f)
                return;

            if (gapLength <= 0f && borderThickness > 0f && borderColor.a > 0f)
            {
                AddFill(vertexHelper, rect, radius, perimeterLength, borderColor);
                Rect inset = Rect.MinMaxRect(rect.xMin + borderThickness,
                    rect.yMin + borderThickness + bottomBorderExtra,
                    rect.xMax - borderThickness, rect.yMax - borderThickness);
                if (inset.width > 0f && inset.height > 0f)
                {
                    float innerRadius = Mathf.Min(Mathf.Max(0f, radius - borderThickness),
                        Mathf.Min(inset.width, inset.height) * 0.5f);
                    AddFill(vertexHelper, inset, innerRadius, GetPerimeterLength(inset, innerRadius), fillColor);
                }
                return;
            }

            AddFill(vertexHelper, rect, radius, perimeterLength, fillColor);
            AddDashedBorder(vertexHelper, rect, radius, perimeterLength);
        }

        private void AddFill(VertexHelper vertexHelper, Rect rect, float radius, float perimeterLength, Color tint)
        {
            int centerIndex = vertexHelper.currentVertCount;
            vertexHelper.AddVert(rect.center, tint, Vector2.zero);

            int pointCount = Mathf.Max(12, Mathf.CeilToInt(perimeterLength / Mathf.Max(2f, curveStep)));
            for (int i = 0; i < pointCount; i++)
            {
                float distance = perimeterLength * i / pointCount;
                vertexHelper.AddVert(GetPointAtDistance(rect, radius, distance), tint, Vector2.zero);
            }

            for (int i = 0; i < pointCount; i++)
            {
                int current = centerIndex + 1 + i;
                int next = centerIndex + 1 + ((i + 1) % pointCount);
                vertexHelper.AddTriangle(centerIndex, current, next);
            }
        }

        private void AddDashedBorder(VertexHelper vertexHelper, Rect rect, float radius, float perimeterLength)
        {
            if (borderThickness <= 0f || borderColor.a <= 0f)
                return;

            float cycle = Mathf.Max(1f, dashLength + gapLength);
            for (float dashStart = 0f; dashStart < perimeterLength; dashStart += cycle)
            {
                float dashEnd = Mathf.Min(dashStart + dashLength, perimeterLength);
                int pieces = Mathf.Max(1, Mathf.CeilToInt((dashEnd - dashStart) / Mathf.Max(2f, curveStep)));

                for (int i = 0; i < pieces; i++)
                {
                    float d0 = Mathf.Lerp(dashStart, dashEnd, (float)i / pieces);
                    float d1 = Mathf.Lerp(dashStart, dashEnd, (float)(i + 1) / pieces);
                    Vector2 p0 = GetPointAtDistance(rect, radius, d0);
                    Vector2 p1 = GetPointAtDistance(rect, radius, d1);
                    Vector2 tangent = (p1 - p0).normalized;
                    Vector2 normal = new Vector2(-tangent.y, tangent.x) * (borderThickness * 0.5f);

                    int index = vertexHelper.currentVertCount;
                    vertexHelper.AddVert(p0 - normal, borderColor, Vector2.zero);
                    vertexHelper.AddVert(p0 + normal, borderColor, Vector2.zero);
                    vertexHelper.AddVert(p1 + normal, borderColor, Vector2.zero);
                    vertexHelper.AddVert(p1 - normal, borderColor, Vector2.zero);
                    vertexHelper.AddTriangle(index, index + 1, index + 2);
                    vertexHelper.AddTriangle(index, index + 2, index + 3);
                }
            }
        }

        private static float GetPerimeterLength(Rect rect, float radius)
        {
            return 2f * Mathf.Max(0f, rect.width - 2f * radius) +
                   2f * Mathf.Max(0f, rect.height - 2f * radius) +
                   2f * Mathf.PI * radius;
        }

        private static Vector2 GetPointAtDistance(Rect rect, float radius, float distance)
        {
            float top = Mathf.Max(0f, rect.width - 2f * radius);
            float side = Mathf.Max(0f, rect.height - 2f * radius);
            float arc = Mathf.PI * radius * 0.5f;
            float perimeter = 2f * top + 2f * side + 4f * arc;
            float d = Mathf.Repeat(distance, perimeter);

            if (d <= top)
                return new Vector2(rect.xMin + radius + d, rect.yMax);
            d -= top;

            if (arc > 0.0001f && d <= arc)
                return ArcPoint(new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 90f, 0f, d / arc);
            d -= arc;

            if (d <= side)
                return new Vector2(rect.xMax, rect.yMax - radius - d);
            d -= side;

            if (arc > 0.0001f && d <= arc)
                return ArcPoint(new Vector2(rect.xMax - radius, rect.yMin + radius), radius, 0f, -90f, d / arc);
            d -= arc;

            if (d <= top)
                return new Vector2(rect.xMax - radius - d, rect.yMin);
            d -= top;

            if (arc > 0.0001f && d <= arc)
                return ArcPoint(new Vector2(rect.xMin + radius, rect.yMin + radius), radius, -90f, -180f, d / arc);
            d -= arc;

            if (d <= side)
                return new Vector2(rect.xMin, rect.yMin + radius + d);
            d -= side;

            return ArcPoint(new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 180f, 90f, arc <= 0f ? 0f : d / arc);
        }

        private static Vector2 ArcPoint(Vector2 center, float radius, float startDegrees, float endDegrees, float t)
        {
            float angle = Mathf.Lerp(startDegrees, endDegrees, Mathf.Clamp01(t)) * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
    }
}
