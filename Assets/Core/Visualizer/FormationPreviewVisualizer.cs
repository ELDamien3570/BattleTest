using Unity.Mathematics;
using UnityEngine;

public class FormationPreviewVisualizer : MonoBehaviour
{
    [Header("Preview Settings")]
    public Color previewColor = Color.green;
    public float lineWidth = 0.15f;
    public float bracketDepth = 2f;
    public float heightOffset = 0.1f;

    private LineRenderer lineRenderer;

    public bool IsDragging { get; set; }
    public float3 DragStart { get; set; }
    public float3 DragEnd { get; set; }

    void Start()
    {
        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = previewColor;
        lineRenderer.endColor = previewColor;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.positionCount = 4;
        lineRenderer.useWorldSpace = true;
        lineRenderer.enabled = false;
    }

    void Update()
    {
        if (IsDragging)
        {
            UpdateBracket();
            lineRenderer.enabled = true;
        }
        else
        {
            lineRenderer.enabled = false;
        }
    }

    void UpdateBracket()
    {
        float3 left = DragStart;
        float3 right = DragEnd;

        float3 rightVec = right - left;
        float width = math.length(rightVec);

        if (width < 0.1f)
        {
            lineRenderer.enabled = false;
            return;
        }

        rightVec = math.normalize(rightVec);
        float3 forwardVec = new float3(-rightVec.z, 0, rightVec.x);

        float3 offset = new float3(0, heightOffset, 0);

        float3 backLeft = left - forwardVec * bracketDepth + offset;
        float3 frontLeft = left + offset;
        float3 frontRight = right + offset;
        float3 backRight = right - forwardVec * bracketDepth + offset;

        lineRenderer.positionCount = 4;
        lineRenderer.SetPosition(0, backLeft);
        lineRenderer.SetPosition(1, frontLeft);
        lineRenderer.SetPosition(2, frontRight);
        lineRenderer.SetPosition(3, backRight);
    }

    void OnDestroy()
    {
        if (lineRenderer != null && lineRenderer.material != null)
        {
            Destroy(lineRenderer.material);
        }
    }
}