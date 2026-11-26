using UnityEngine;
using UnityEngine.UI;

public sealed class ResultCelebration : MaskableGraphic
{
    [Header("Score")]
    [SerializeField, Min(0)] private int mediumScore = 100;
    [SerializeField, Min(0)] private int highScore = 200;
    [SerializeField, Range(0, 128)] private int lowCount = 12;
    [SerializeField, Range(0, 128)] private int mediumCount = 40;
    [SerializeField, Range(0, 128)] private int highCount = 78;

    [Header("Appearance")]
    [SerializeField, Range(0f, 1f)] private float opacity = 0.88f;
    [SerializeField, Min(0.2f)] private float lifetime = 2.7f;
    [SerializeField, Min(0f)] private float driftSpeed = 0.1f;
    [SerializeField, Min(0.1f)] private float sizeScale = 1f;
    [SerializeField] private Color starColor = new Color32(255, 224, 125, 255);
    [SerializeField] private Color roseColor = new Color32(255, 141, 185, 255);
    [SerializeField] private Color mintColor = new Color32(129, 235, 215, 255);
    [SerializeField] private Color lilacColor = new Color32(202, 174, 255, 255);

    private struct Particle
    {
        public Vector2 origin;
        public Color color;
        public float delay;
        public float speed;
        public float angle;
        public float radius;
        public bool star;
    }

    private readonly Particle[] particles = new Particle[128];
    private int count;
    private float elapsed;
    private float playbackSpeed;

    public void Play(int score, float maxDuration = float.PositiveInfinity)
    {
        int tier = score >= Mathf.Max(mediumScore, highScore) ? 2 : score >= mediumScore ? 1 : 0;
        count = Mathf.Clamp(tier == 2 ? highCount : tier == 1 ? mediumCount : lowCount, 0, particles.Length);
        elapsed = 0f;
        playbackSpeed = Mathf.Max(1f, (lifetime + 0.65f) / maxDuration);
        var random = new System.Random(score);
        for (int i = 0; i < count; i++)
        {
            float scatter = (float)random.NextDouble();
            bool star = tier == 0 || i % (tier == 2 ? 4 : 2) == 0;
            float x = Mathf.Lerp(0.06f, 0.94f, scatter);
            float y = i % 2 == 0 ? 0.94f : 0.035f;
            float direction = i % 2 == 0 ? -1f : 1f;
            if (i % 5 < 3)
            {
                // The columns are visible between and alongside the two result cards.
                x = (i % 5 == 0 ? 0.02f : i % 5 == 1 ? 0.56f : 0.98f) + (scatter - 0.5f) * 0.012f;
                y = Mathf.Lerp(0.2f, 0.98f, (float)random.NextDouble());
                direction = -1f;
            }
            particles[i] = new Particle
            {
                origin = new Vector2(x, y),
                color = star ? starColor : i % 3 == 0 ? roseColor : i % 3 == 1 ? mintColor : lilacColor,
                delay = (float)random.NextDouble() * 0.65f,
                speed = direction * Mathf.Lerp(0.46f, 1f, (float)random.NextDouble()),
                angle = scatter * Mathf.PI * 2f,
                radius = Mathf.Lerp(4f, 13f, (float)random.NextDouble()),
                star = star
            };
        }
        SetVerticesDirty();
    }

    private void Update()
    {
        if (count == 0)
            return;
        elapsed += Time.unscaledDeltaTime * playbackSpeed;
        if (elapsed >= lifetime + 0.65f)
            count = 0;
        SetVerticesDirty();
    }

    protected override void OnDisable()
    {
        count = 0;
        base.OnDisable();
    }

    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        vertices.Clear();
        Rect rect = rectTransform.rect;
        float scale = sizeScale * Mathf.Min(rect.width / 1920f, rect.height / 1080f);
        for (int i = 0; i < count; i++)
        {
            Particle particle = particles[i];
            float age = elapsed - particle.delay;
            if (age <= 0f || age >= lifetime)
                continue;
            float progress = age / lifetime;
            float angle = particle.angle + age * particle.speed * 3f;
            float sway = Mathf.Sin(angle) - Mathf.Sin(particle.angle);
            Vector2 position = particle.origin + new Vector2(sway * 0.004f, age * particle.speed * driftSpeed);
            if (position.y < 0f || position.y > 1f)
                continue;
            Vector2 center = rect.min + Vector2.Scale(position, rect.size);
            Color tint = particle.color;
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress * 8f))
                * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, progress)));
            tint.a *= opacity * fade;
            AddParticle(vertices, center, particle.radius * scale, angle, tint, particle.star);
        }
    }

    private static void AddParticle(VertexHelper vertices, Vector2 center, float radius, float angle,
        Color tint, bool star)
    {
        int start = vertices.currentVertCount;
        vertices.AddVert(center, Color.Lerp(tint, new Color(1f, 1f, 1f, tint.a), 0.32f), Vector2.zero);
        int sides = star ? 8 : 4;
        for (int i = 0; i < sides; i++)
        {
            float arc = i * Mathf.PI * 2f / sides;
            Vector2 point;
            if (star)
            {
                float reach = i % 2 == 0 ? radius : radius * 0.31f;
                point = new Vector2(Mathf.Cos(arc + angle), Mathf.Sin(arc + angle)) * reach;
            }
            else
            {
                arc += Mathf.PI * 0.25f;
                float x = Mathf.Cos(arc) * (0.28f + 0.56f * Mathf.Abs(Mathf.Cos(angle * 1.8f)));
                float y = Mathf.Sin(arc);
                point = new Vector2(x * Mathf.Cos(angle) - y * Mathf.Sin(angle),
                    x * Mathf.Sin(angle) + y * Mathf.Cos(angle)) * radius;
            }
            vertices.AddVert(center + point, tint, Vector2.zero);
            vertices.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % sides);
        }
    }
}
