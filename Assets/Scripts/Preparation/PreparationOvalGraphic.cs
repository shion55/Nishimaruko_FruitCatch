using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace FruitCatch.Preparation
{
    // Shared geometry for the outline, progress arc and snapshot stencil mask.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PreparationOvalGraphic : MaskableGraphic
    {
        public bool filled;
        public float thickness = 8f;
        public float progress = 1f;
        public RectTransform cutout;
        public bool solidRectangle;
        public bool rectangular;
        internal bool SmoothEdges { get; set; }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (solidRectangle || (rectangular && filled && cutout == null))
            {
                AddRectangle(vh, rect, color);
                return;
            }
            if (cutout != null)
            {
                if (rectangular)
                    PopulateRectangularBackdrop(vh, rect);
                else
                    PopulateBackdrop(vh, rect);
                return;
            }
            if (rectangular)
            {
                PopulateRectangularFrame(vh, rect);
                return;
            }
            const int segments = 128;
            float amount = Mathf.Clamp01(progress);
            Vector2 radius = rect.size * 0.5f;
            if (amount <= 0f || radius.x <= 0f || radius.y <= 0f)
                return;

            Vector2 inner = filled ? Vector2.zero : Vector2.Max(radius - Vector2.one * thickness, Vector2.zero);
            float feather = 0f;
            if (SmoothEdges)
            {
                // Center the alpha falloff on the original edge in screen pixels.
                float scale = canvas != null ? canvas.scaleFactor : 1f;
                float maxFeather = (filled ? Mathf.Min(radius.x, radius.y) : thickness) * 0.4f;
                feather = Mathf.Min(0.75f / Mathf.Max(scale, 0.001f), maxFeather);
            }

            Vector2 featherOffset = Vector2.one * feather;
            Vector2 solidOuter = radius - featherOffset;
            Vector2 solidInner = filled ? Vector2.zero : inner + featherOffset;
            Color32 opaque = color;
            Color32 clear = opaque;
            clear.a = 0;

            for (int i = 0; i < segments; i++)
            {
                float a = (Mathf.PI * 0.5f) - Mathf.PI * 2 * amount * i / segments;
                float b = (Mathf.PI * 0.5f) - Mathf.PI * 2 * amount * (i + 1) / segments;
                Vector2 u = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 v = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                AddBand(vh, rect.center, u, v, solidOuter, solidInner, opaque, opaque);
                if (feather > 0f)
                {
                    AddBand(vh, rect.center, u, v, radius + featherOffset, solidOuter, clear, opaque);
                    if (!filled)
                        AddBand(vh, rect.center, u, v, solidInner,
                            Vector2.Max(inner - featherOffset, Vector2.zero), opaque, clear);
                }
            }
        }

        private void PopulateRectangularFrame(VertexHelper vh, Rect rect)
        {
            float amount = Mathf.Clamp01(progress);
            if (amount <= 0f)
                return;

            float stroke = Mathf.Min(thickness, Mathf.Min(rect.width, rect.height) * 0.5f);
            Vector2[] outerStart =
            {
                new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMax, rect.yMax),
                new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMin, rect.yMin)
            };
            Vector2[] outerEnd =
            {
                new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMax)
            };
            Vector2[] innerStart =
            {
                new Vector2(rect.xMin + stroke, rect.yMax - stroke), new Vector2(rect.xMax - stroke, rect.yMax - stroke),
                new Vector2(rect.xMax - stroke, rect.yMin + stroke), new Vector2(rect.xMin + stroke, rect.yMin + stroke)
            };
            Vector2[] innerEnd =
            {
                new Vector2(rect.xMax - stroke, rect.yMax - stroke), new Vector2(rect.xMax - stroke, rect.yMin + stroke),
                new Vector2(rect.xMin + stroke, rect.yMin + stroke), new Vector2(rect.xMin + stroke, rect.yMax - stroke)
            };
            float[] lengths = { rect.width, rect.height, rect.width, rect.height };
            float remaining = amount * (rect.width + rect.height) * 2f;

            for (int i = 0; i < 4 && remaining > 0f; i++)
            {
                float sideAmount = Mathf.Clamp01(remaining / lengths[i]);
                AddQuad(vh, outerStart[i], Vector2.Lerp(outerStart[i], outerEnd[i], sideAmount),
                    Vector2.Lerp(innerStart[i], innerEnd[i], sideAmount), innerStart[i], color);
                remaining -= lengths[i];
            }
        }

        private void PopulateRectangularBackdrop(VertexHelper vh, Rect outer)
        {
            Vector3[] cutoutCorners = new Vector3[4];
            cutout.GetWorldCorners(cutoutCorners);
            Vector2 low = rectTransform.InverseTransformPoint(cutoutCorners[0]);
            Vector2 high = rectTransform.InverseTransformPoint(cutoutCorners[2]);
            low = new Vector2(Mathf.Clamp(low.x, outer.xMin, outer.xMax), Mathf.Clamp(low.y, outer.yMin, outer.yMax));
            high = new Vector2(Mathf.Clamp(high.x, outer.xMin, outer.xMax), Mathf.Clamp(high.y, outer.yMin, outer.yMax));

            AddRectangle(vh, Rect.MinMaxRect(outer.xMin, outer.yMin, low.x, outer.yMax), color);
            AddRectangle(vh, Rect.MinMaxRect(high.x, outer.yMin, outer.xMax, outer.yMax), color);
            AddRectangle(vh, Rect.MinMaxRect(low.x, outer.yMin, high.x, low.y), color);
            AddRectangle(vh, Rect.MinMaxRect(low.x, high.y, high.x, outer.yMax), color);
        }

        private static void AddRectangle(VertexHelper vh, Rect rect, Color32 vertexColor)
        {
            if (rect.width <= 0f || rect.height <= 0f)
                return;
            AddQuad(vh, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMax),
                new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax, rect.yMin), vertexColor);
        }

        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 vertexColor)
        {
            int index = vh.currentVertCount;
            vh.AddVert(a, vertexColor, Vector2.zero);
            vh.AddVert(b, vertexColor, Vector2.zero);
            vh.AddVert(c, vertexColor, Vector2.zero);
            vh.AddVert(d, vertexColor, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }

        private static void AddBand(VertexHelper vh, Vector2 center, Vector2 u, Vector2 v,
            Vector2 outer, Vector2 inner, Color32 outerColor, Color32 innerColor)
        {
            int index = vh.currentVertCount;
            vh.AddVert(center + Vector2.Scale(u, outer), outerColor, Vector2.zero);
            vh.AddVert(center + Vector2.Scale(v, outer), outerColor, Vector2.zero);
            vh.AddVert(center + Vector2.Scale(v, inner), innerColor, Vector2.zero);
            vh.AddVert(center + Vector2.Scale(u, inner), innerColor, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }

        private void PopulateBackdrop(VertexHelper vh, Rect outer)
        {
            Vector3[] corners = new Vector3[4];
            cutout.GetWorldCorners(corners);
            Vector2 low = rectTransform.InverseTransformPoint(corners[0]);
            Vector2 high = rectTransform.InverseTransformPoint(corners[2]);
            Vector2 center = (low + high) * 0.5f;
            Vector2 radius = (high - low) * 0.5f;
            var angles = new List<float>();
            for (int i = 0; i < 128; i++) angles.Add(i * Mathf.PI * 2 / 128);
            foreach (Vector2 corner in new[] { outer.min, outer.max,
                new Vector2(outer.xMin, outer.yMax), new Vector2(outer.xMax, outer.yMin) })
                angles.Add(Mathf.Repeat(Mathf.Atan2(corner.y - center.y, corner.x - center.x), Mathf.PI * 2));
            angles.Sort();
            for (int i = 0; i < angles.Count; i++)
            {
                Vector2 u = new Vector2(Mathf.Cos(angles[i]), Mathf.Sin(angles[i]));
                Vector2 v = new Vector2(Mathf.Cos(angles[(i + 1) % angles.Count]), Mathf.Sin(angles[(i + 1) % angles.Count]));
                int index = vh.currentVertCount;
                vh.AddVert(center + u * DistanceToRect(center, u, outer), color, Vector2.zero);
                vh.AddVert(center + v * DistanceToRect(center, v, outer), color, Vector2.zero);
                vh.AddVert(center + v / Mathf.Sqrt(v.x * v.x / (radius.x * radius.x) + v.y * v.y / (radius.y * radius.y)), color, Vector2.zero);
                vh.AddVert(center + u / Mathf.Sqrt(u.x * u.x / (radius.x * radius.x) + u.y * u.y / (radius.y * radius.y)), color, Vector2.zero);
                vh.AddTriangle(index, index + 1, index + 2);
                vh.AddTriangle(index, index + 2, index + 3);
            }
        }

        private static float DistanceToRect(Vector2 center, Vector2 direction, Rect rect)
        {
            float x = Mathf.Abs(direction.x) < 0.00001f ? float.PositiveInfinity :
                ((direction.x > 0 ? rect.xMax : rect.xMin) - center.x) / direction.x;
            float y = Mathf.Abs(direction.y) < 0.00001f ? float.PositiveInfinity :
                ((direction.y > 0 ? rect.yMax : rect.yMin) - center.y) / direction.y;
            return Mathf.Min(x, y);
        }
    }
}
