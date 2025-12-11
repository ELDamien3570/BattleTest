using Unity.Mathematics;
using UnityEngine;

public class FormationPreviewVisualizer : MonoBehaviour
{
    [Header("Preview Settings")]
    public GameObject soldierPreviewPrefab; // Assign a simple sphere or custom marker
    public Color previewColor = new Color(0, 1, 0, 0.5f); // Green semi-transparent
    public float markerSize = 0.3f;
    public int maxPreviewSoldiers = 1023; // Limit for performance

    [Header("Preview Behavior")]
    public bool showSoldierPositions = true;
    public bool showFormationBounds = true;
    public bool showFacingArrow = true;

    // Internal state
    private GameObject[] previewMarkers;
    private bool isInitialized = false;

    // References to current drag state (set by input system)
    public bool IsDragging { get; set; }
    public float3 DragStart { get; set; }
    public float3 DragEnd { get; set; }
    public int SoldierCount; // Default count

    public float spacing = 1f; // Initialize to 1f to prevent div by zero

    void Start()
    {
        InitializePreviewMarkers();
    }

    void InitializePreviewMarkers()
    {
        // Create pool of preview markers
        previewMarkers = new GameObject[maxPreviewSoldiers];

        for (int i = 0; i < maxPreviewSoldiers; i++)
        {
            GameObject marker;

            if (soldierPreviewPrefab != null)
            {
                marker = Instantiate(soldierPreviewPrefab, transform);
            }
            else
            {
                // Create simple sphere as fallback
                marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.transform.parent = transform;
                marker.transform.localScale = Vector3.one * markerSize;

                // Make it semi-transparent
                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var mat = new Material(Shader.Find("Standard"));
                    mat.color = previewColor;
                    // Enable transparency
                    mat.SetFloat("_Mode", 3); // Transparent mode
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 3000;
                    renderer.material = mat;
                }

                // Remove collider to avoid interfering with raycasts
                Destroy(marker.GetComponent<Collider>());
            }

            marker.SetActive(false);
            previewMarkers[i] = marker;
        }

        isInitialized = true;
    }

    void Update()
    {
        if (!isInitialized) return;

        if (IsDragging && showSoldierPositions)
        {
            UpdatePreviewPositions();
        }
        else
        {
            HideAllMarkers();
        }
    }

    void UpdatePreviewPositions()
    {
        // Calculate formation parameters from drag
        float3 frontLeft = DragStart;
        float3 frontRight = DragEnd;
        float3 center = (frontLeft + frontRight) * 0.5f; // Front-center of formation

        float3 rightVec = frontRight - frontLeft;
        float width = math.length(rightVec);

        if (width < 0.1f)
        {
            HideAllMarkers();
            return;
        }

        rightVec = math.normalize(rightVec);
        float3 forwardVec = new float3(-rightVec.z, 0, rightVec.x);
        quaternion facing = quaternion.LookRotationSafe(forwardVec, math.up());


        int columns = math.max(1, (int)(width / spacing));
        int soldierCount = math.min(SoldierCount, maxPreviewSoldiers);
        int rows = (int)math.ceil((float)soldierCount / columns);

        float halfWidth = (columns - 1) * 0.5f * spacing;
        // No halfDepth - formation builds backwards from front

        // Position preview markers (front to back)
        for (int i = 0; i < soldierCount; i++)
        {
            int col = i % columns;
            int row = i / columns;

            float3 localOffset = new float3(
                (col * spacing) - halfWidth,  // X: left to right, centered
                0,
                -row * spacing                // Z: backwards from front line
            );

            float3 worldPos = center + math.rotate(facing, localOffset);

            previewMarkers[i].transform.position = worldPos;
            previewMarkers[i].SetActive(true);
        }

        // Hide unused markers
        for (int i = soldierCount; i < maxPreviewSoldiers; i++)
        {
            previewMarkers[i].SetActive(false);
        }
    }

    void HideAllMarkers()
    {
        if (previewMarkers == null) return;

        foreach (var marker in previewMarkers)
        {
            if (marker != null)
                marker.SetActive(false);
        }
    }

    void OnDrawGizmos()
    {
        if (!IsDragging || !isInitialized) return;

        float3 frontLeft = DragStart;
        float3 frontRight = DragEnd;
        float3 center = (frontLeft + frontRight) * 0.5f; // Front-center

        float3 rightVec = frontRight - frontLeft;
        float width = math.length(rightVec);

        if (width < 0.1f) return;

        rightVec = math.normalize(rightVec);
        float3 forwardVec = new float3(-rightVec.z, 0, rightVec.x);

        // Draw formation bounds
        if (showFormationBounds)
        {
            int columns = math.max(1, (int)(width / spacing));
            int rows = (int)math.ceil((float)SoldierCount / columns);

            float halfWidth = (columns - 1) * 0.5f * spacing;
            float depth = (rows - 1) * spacing; // Total depth backwards

            quaternion facing = quaternion.LookRotationSafe(forwardVec, math.up());

            // Corners: front line is at center, back line is behind
            float3[] corners = new float3[4];
            corners[0] = center + math.rotate(facing, new float3(-halfWidth, 0, 0));        // Front-left
            corners[1] = center + math.rotate(facing, new float3(halfWidth, 0, 0));         // Front-right
            corners[2] = center + math.rotate(facing, new float3(halfWidth, 0, -depth));    // Back-right
            corners[3] = center + math.rotate(facing, new float3(-halfWidth, 0, -depth));   // Back-left

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(corners[0], corners[1]); // Front line
            Gizmos.DrawLine(corners[1], corners[2]); // Right side
            Gizmos.DrawLine(corners[2], corners[3]); // Back line
            Gizmos.DrawLine(corners[3], corners[0]); // Left side
        }

        // Draw facing arrow (from center forward)
        if (showFacingArrow)
        {
            Gizmos.color = Color.red;
            float3 arrowStart = center;
            float3 arrowEnd = center + forwardVec * 3f;
            Gizmos.DrawLine(arrowStart, arrowEnd);

            // Arrow head
            float3 arrowRight = rightVec * 0.5f;
            float3 arrowBack = forwardVec * -0.5f;
            Gizmos.DrawLine(arrowEnd, arrowEnd + arrowBack + arrowRight);
            Gizmos.DrawLine(arrowEnd, arrowEnd + arrowBack - arrowRight);
        }

        // Draw drag line (front edge)
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(DragStart, DragEnd);

        // Draw a marker at center point
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(center, 0.3f);
    }

    void OnDestroy()
    {
        // Clean up preview markers
        if (previewMarkers != null)
        {
            foreach (var marker in previewMarkers)
            {
                if (marker != null)
                    Destroy(marker);
            }
        }
    }
}