using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup), OrderLast = true)]
public partial class PlayerInputSystem : SystemBase
{
    private FormationPreviewVisualizer previewVisualizer;

    private bool isRightClickDragging = false;
    private float3 dragStartPoint;
    private float3 dragCurrentPoint;
    private int currentSoldierCount = 0;

    protected override void OnCreate()
    {
        base.OnCreate();

        // Find or create preview visualizer
        previewVisualizer = Object.FindAnyObjectByType<FormationPreviewVisualizer>();

        if (previewVisualizer == null)
        {
            var go = new GameObject("Formation Preview Visualizer");
            previewVisualizer = go.AddComponent<FormationPreviewVisualizer>();
            UnityEngine.Debug.Log("Created FormationPreviewVisualizer automatically");
        }
    }

    protected override void OnUpdate()
    {
        // Get current soldier count from first formation (for preview)
        foreach (var group in SystemAPI.Query<RefRO<FormationPositionData>>())
        {
            currentSoldierCount = group.ValueRO.soldierCount;
            previewVisualizer.SoldierCount = currentSoldierCount;
            break;
        }

        // === LEFT CLICK: Simple move command ===
        if (Input.GetMouseButtonDown(0))
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                float3 clickedPos = hit.point;

                foreach (var group in SystemAPI.Query<RefRW<FormationPositionData>>())
                {
                    group.ValueRW.targetPosition = clickedPos;
                    group.ValueRW.formationState = FormationState.retargeting;
                }

                UnityEngine.Debug.Log($"Move command to: {clickedPos}");
            }
        }

        // === RIGHT CLICK DRAG: Define formation shape and position ===
        if (Input.GetMouseButtonDown(1))
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                isRightClickDragging = true;
                dragStartPoint = hit.point;
                dragCurrentPoint = hit.point;

                // Start preview
                if (previewVisualizer != null)
                {
                    previewVisualizer.IsDragging = true;
                    previewVisualizer.DragStart = dragStartPoint;
                    previewVisualizer.DragEnd = dragCurrentPoint;
                    previewVisualizer.SoldierCount = currentSoldierCount;
                }
            }
        }

        // Update drag endpoint while dragging
        if (Input.GetMouseButton(1) && isRightClickDragging)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                dragCurrentPoint = hit.point;

                // Update preview
                if (previewVisualizer != null)
                {
                    previewVisualizer.DragEnd = dragCurrentPoint;
                }
            }
        }

        // Complete formation on mouse up
        if (Input.GetMouseButtonUp(1) && isRightClickDragging)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    dragCurrentPoint = hit.point;
                }
            }

            // Calculate formation parameters from drag
            float3 frontLeft = dragStartPoint;
            float3 frontRight = dragCurrentPoint;
            float3 center = (frontLeft + frontRight) * 0.5f;

            float3 rightVec = frontRight - frontLeft;
            float width = math.length(rightVec);

            if (width > 0.1f) // Minimum drag distance
            {
                rightVec = rightVec / width; // Normalize

                // Forward is perpendicular to drag line (90° rotation in XZ plane)
                float3 forwardVec = new float3(-rightVec.z, 0, rightVec.x);
                quaternion facing = quaternion.LookRotationSafe(forwardVec, math.up());

                // Calculate columns from width
                float spacing = 1f;
                int newColumns = FormationBuilderUtility.CalculateColumnsFromWidth(width, spacing);

                foreach (var group in SystemAPI.Query<RefRW<FormationPositionData>>())
                {
                    // Update formation shape
                    group.ValueRW.columns = newColumns;
                    group.ValueRW.facing = facing;

                    // Set target to the dragged position
                    group.ValueRW.targetPosition = center;

                    // IMPORTANT: Set to rebuilding first (will rebuild on next frame)
                    // Then FormationMovementSystem will handle movement to target
                    group.ValueRW.formationState = FormationState.rebuilding;

                    UnityEngine.Debug.Log($"Formation drag: {newColumns} columns, width {width:F1}m, moving to {center}");
                }
            }

            // End preview
            isRightClickDragging = false;
            if (previewVisualizer != null)
            {
                previewVisualizer.IsDragging = false;
            }
        }

        // Cancel drag if button released
        if (!Input.GetMouseButton(1) && isRightClickDragging)
        {
            isRightClickDragging = false;
            if (previewVisualizer != null)
            {
                previewVisualizer.IsDragging = false;
            }
        }
    }
}
