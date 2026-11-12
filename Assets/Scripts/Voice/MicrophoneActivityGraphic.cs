using UnityEngine;
using UnityEngine.UI;

/// <summary>Resolution-independent microphone artwork for the voice HUD.</summary>
[AddComponentMenu("UI/Microphone Activity Graphic")]
public sealed class MicrophoneActivityGraphic : MaskableGraphic
{
    [SerializeField] private bool crossedOut;

    public bool CrossedOut
    {
        get => crossedOut;
        set
        {
            if (crossedOut == value) return;
            crossedOut = value;
            SetVerticesDirty();
        }
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        float scale = Mathf.Min(rectTransform.rect.width / 32f, rectTransform.rect.height / 36f);
        Vector2 center = rectTransform.rect.center;
        AddCapsule(mesh, center, scale);
        AddCup(mesh, center, scale);
        AddLine(mesh, center, scale, new Vector2(0f, -8f), new Vector2(0f, -11f), 2f);
        AddLine(mesh, center, scale, new Vector2(-5f, -12f), new Vector2(5f, -12f), 2f);
        if (crossedOut)
            AddLine(mesh, center, scale, new Vector2(-12f, -14f), new Vector2(12f, 14f), 2.5f);
    }

    private void AddLine(VertexHelper mesh, Vector2 center, float scale, Vector2 from, Vector2 to, float width)
    {
        Vector2 normal = new Vector2(-(to - from).y, (to - from).x).normalized * (width * 0.5f);
        int start = mesh.currentVertCount;
        mesh.AddVert(center + (from - normal) * scale, color, Vector2.zero);
        mesh.AddVert(center + (from + normal) * scale, color, Vector2.zero);
        mesh.AddVert(center + (to + normal) * scale, color, Vector2.zero);
        mesh.AddVert(center + (to - normal) * scale, color, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }

    private void AddCapsule(VertexHelper mesh, Vector2 center, float scale)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(center + new Vector2(0f, 5.5f) * scale, color, Vector2.zero);
        const int capSteps = 12;
        const int points = (capSteps + 1) * 2;
        for (int i = 0; i < points; i++)
        {
            bool upper = i <= capSteps;
            int step = upper ? i : i - capSteps - 1;
            float angle = step * Mathf.PI / capSteps + (upper ? 0f : Mathf.PI);
            Vector2 point = new Vector2(Mathf.Cos(angle) * 4f, (upper ? 9f : 2f) + Mathf.Sin(angle) * 4f);
            mesh.AddVert(center + point * scale, color, Vector2.zero);
        }
        for (int i = 0; i < points; i++)
            mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % points);
    }

    private void AddCup(VertexHelper mesh, Vector2 center, float scale)
    {
        int start = mesh.currentVertCount;
        AddStrokePair(mesh, center, scale, new Vector2(-8f, 6f), Vector2.left);
        const int arcSteps = 20;
        for (int i = 0; i <= arcSteps; i++)
        {
            float angle = Mathf.PI + i * Mathf.PI / arcSteps;
            Vector2 normal = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            AddStrokePair(mesh, center, scale, new Vector2(0f, 1f) + normal * 8f, normal);
        }
        AddStrokePair(mesh, center, scale, new Vector2(8f, 6f), Vector2.right);
        for (int i = 0; i < arcSteps + 2; i++)
        {
            int index = start + i * 2;
            mesh.AddTriangle(index, index + 1, index + 3);
            mesh.AddTriangle(index, index + 3, index + 2);
        }
    }

    private void AddStrokePair(VertexHelper mesh, Vector2 center, float scale, Vector2 point, Vector2 normal)
    {
        mesh.AddVert(center + (point - normal) * scale, color, Vector2.zero);
        mesh.AddVert(center + (point + normal) * scale, color, Vector2.zero);
    }
}
