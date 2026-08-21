using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class EmoteWheelSliceGraphic : MaskableGraphic
{
    [SerializeField] [Min(0f)] private float innerRadius = 64f;
    [SerializeField] [Min(1f)] private float outerRadius = 160f;
    [SerializeField] private float startAngle;
    [SerializeField] private float endAngle = 72f;
    [SerializeField] [Range(4, 64)] private int resolution = 24;

    public void Configure(float innerRadius, float outerRadius, float startAngle, float endAngle, Color color)
    {
        this.innerRadius = Mathf.Max(0f, innerRadius);
        this.outerRadius = Mathf.Max(this.innerRadius + 1f, outerRadius);
        this.startAngle = startAngle;
        this.endAngle = endAngle;
        this.color = color;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();

        float safeOuterRadius = Mathf.Max(1f, outerRadius);
        float safeInnerRadius = Mathf.Clamp(innerRadius, 0f, safeOuterRadius - 1f);
        float angleSpan = endAngle - startAngle;
        if (Mathf.Abs(angleSpan) <= 0.01f)
            return;

        int stepCount = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(angleSpan) / 360f * Mathf.Max(4, resolution)));
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;

        for (int i = 0; i <= stepCount; i++)
        {
            float t = i / (float)stepCount;
            float angleRadians = (startAngle + angleSpan * t) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians));

            vertex.position = direction * safeInnerRadius;
            vertexHelper.AddVert(vertex);

            vertex.position = direction * safeOuterRadius;
            vertexHelper.AddVert(vertex);
        }

        for (int i = 0; i < stepCount; i++)
        {
            int inner0 = i * 2;
            int outer0 = inner0 + 1;
            int inner1 = inner0 + 2;
            int outer1 = inner0 + 3;

            if (angleSpan > 0f)
            {
                vertexHelper.AddTriangle(inner0, outer0, outer1);
                vertexHelper.AddTriangle(inner0, outer1, inner1);
            }
            else
            {
                vertexHelper.AddTriangle(inner0, outer1, outer0);
                vertexHelper.AddTriangle(inner0, inner1, outer1);
            }
        }
    }
}
