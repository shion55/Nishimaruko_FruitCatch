using UnityEngine;
using UnityEngine.UI;

namespace FruitCatch.Preparation
{
    public sealed class PreparationPitchMeter : MaskableGraphic
    {
        [SerializeField] private PitchFallSpeedController pitchController;
        [SerializeField] private Color lowColor = new Color32(72, 164, 218, 255);
        [SerializeField] private Color highColor = new Color32(232, 110, 157, 255);
        [SerializeField] private Color inactiveColor = new Color32(205, 198, 202, 255);
        [SerializeField, Min(0.01f)] private float activityFade = 0.12f;

        private float pitch = 0.5f;
        private float activity;

        protected override void OnEnable()
        {
            activity = 0f;
            base.OnEnable();
        }

        private void LateUpdate()
        {
            bool valid = pitchController != null && pitchController.HasCurrentPitch;
            float nextActivity = Mathf.MoveTowards(activity, valid ? 1f : 0f,
                Time.unscaledDeltaTime / Mathf.Max(0.01f, activityFade));
            float nextPitch = valid ? pitchController.NormalizedPitch : pitch;
            if (activity == nextActivity && pitch == nextPitch)
                return;
            activity = nextActivity;
            pitch = nextPitch;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            Rect rect = rectTransform.rect;
            float width = Mathf.Min(rect.width * 0.52f, rect.height * 0.2f);
            float height = rect.height - width;
            Vector2 center = rect.center;
            Color low = Color.Lerp(inactiveColor, lowColor, activity);
            Color high = Color.Lerp(inactiveColor, highColor, activity);
            AddCapsule(vertices, center, width, height, low, high);

            float travel = height - width;
            for (int i = 0; i <= 4; i++)
            {
                Vector2 tick = center + new Vector2(0f, (i / 4f - 0.5f) * travel);
                AddCapsule(vertices, tick, width * 0.36f, 2f,
                    new Color(1f, 1f, 1f, 0.52f), new Color(1f, 1f, 1f, 0.52f));
            }

            if (activity <= 0f)
                return;
            Vector2 marker = center + Vector2.up * ((pitch - 0.5f) * travel);
            Color outline = Color.Lerp(lowColor, highColor, pitch);
            outline.a *= activity;
            Color white = new Color(1f, 1f, 1f, activity);
            AddCapsule(vertices, marker, width * 1.62f, width * 0.52f, outline, outline);
            AddCapsule(vertices, marker, width * 1.39f, width * 0.32f, white, white);
        }

        private static void AddCapsule(VertexHelper vertices, Vector2 center, float width, float height,
            Color bottom, Color top)
        {
            if (width <= 0f || height <= 0f)
                return;
            const int steps = 8;
            float radius = Mathf.Min(width, height) * 0.5f;
            Vector2 offset = new Vector2(width * 0.5f - radius, height * 0.5f - radius);
            int index = vertices.currentVertCount;
            vertices.AddVert(center, Color.Lerp(bottom, top, 0.5f), Vector2.zero);
            // A vertical capsule uses top/bottom arcs, a horizontal capsule uses right/left arcs.
            bool vertical = height >= width;
            int count = 2 * (steps + 1);
            for (int i = 0; i < count; i++)
            {
                bool first = i <= steps;
                float angle = ((i % (steps + 1)) / (float)steps + (first ? 0f : 1f)) * Mathf.PI;
                if (!vertical) angle -= Mathf.PI * 0.5f;
                Vector2 point = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius
                    + offset * (first ? 1f : -1f);
                vertices.AddVert(center + point, Color.Lerp(bottom, top, point.y / height + 0.5f), Vector2.zero);
                vertices.AddTriangle(index, index + 1 + i, index + 1 + (i + 1) % count);
            }
        }
    }
}
