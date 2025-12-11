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
                    
                    // Calculate targetFacing toward the move destination
                    float3 directionToTarget = clickedPos - group.ValueRO.centerPosition;
                    if (math.lengthsq(directionToTarget) > 0.001f)
                    {
                        group.ValueRW.targetFacing = quaternion.LookRotationSafe(math.normalize(directionToTarget), math.up());
                    }
                    
                    // Check if we need to rotate first
                    float angleDiff = QuaternionAngle(group.ValueRO.facing, group.ValueRW.targetFacing);
                    
                    if (angleDiff > 0.1f)
                    {
                        group.ValueRW.formationState = FormationState.rotating;
                    }
                    else
                    {
                        group.ValueRW.formationState = FormationState.retargeting;
                    }
                }
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

            if (width > 0.1f)
            {
                rightVec = rightVec / width;
                float3 forwardVec = new float3(-rightVec.z, 0, rightVec.x);
                quaternion facing = quaternion.LookRotationSafe(forwardVec, math.up());

                int newColumns = FormationBuilderUtility.CalculateColumnsFromWidth(width, 1f);

                foreach (var group in SystemAPI.Query<RefRW<FormationPositionData>>())
                {
                    group.ValueRW.columns = newColumns;
                    
                    // Set targetFacing (NOT facing)
                    group.ValueRW.targetFacing = facing;
                    group.ValueRW.targetPosition = center;

                    // Trigger rebuild
                    group.ValueRW.formationState = FormationState.rebuilding;
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
    
    private static float QuaternionAngle(quaternion a, quaternion b)
    {
        float dot = math.abs(math.dot(a, b));
        return math.acos(math.min(dot, 1.0f)) * 2.0f;
    }
}
