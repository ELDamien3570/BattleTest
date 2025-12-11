using Unity.Collections;
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
    private float currentSpacing = 1f; // Default spacing
    
    private const float DragDistanceThreshold = 0.5f; 
    private const double DragTimeThreshold = 0.20; 
    private double rightMouseDownTime;

    protected override void OnCreate()
    {
        base.OnCreate();

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
        // Update soldier count and spacing from the SELECTED formation
        currentSoldierCount = 0;
        foreach (var group in SystemAPI.Query<RefRO<FormationPositionData>>().WithAll<FormationSelected>())
        {
            currentSoldierCount = group.ValueRO.soldierCount;
            currentSpacing = group.ValueRO.spacing; // Capture spacing

            if (previewVisualizer != null) 
            {
                previewVisualizer.SoldierCount = currentSoldierCount;
                previewVisualizer.spacing = currentSpacing; // Update visualizer
            }
            break; 
        }

        Camera cam = Camera.main;
        if (cam == null) return;

        // ... (Left Click Selection Logic remains the same) ...
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
                foreach (var (tag, entity) in SystemAPI.Query<RefRO<FormationSelected>>().WithEntityAccess())
                {
                    ecb.RemoveComponent<FormationSelected>(entity);
                }

                Entity closestEntity = Entity.Null;
                float closestDist = float.MaxValue;

                foreach (var (pos, entity) in SystemAPI.Query<RefRO<FormationPositionData>>().WithEntityAccess())
                {
                    float dist = math.distance(new float2(pos.ValueRO.centerPosition.x, pos.ValueRO.centerPosition.z), new float2(hit.point.x, hit.point.z));
                    
                    int rows = (int)math.ceil((float)pos.ValueRO.soldierCount / pos.ValueRO.columns);
                    float width = pos.ValueRO.columns * pos.ValueRO.spacing; // Use spacing
                    float depth = rows * pos.ValueRO.spacing; // Use spacing
                    float formationRadius = math.length(new float2(width, depth)) * 0.6f; 
                    
                    float selectionThreshold = math.max(3.0f, formationRadius);

                    if (dist < selectionThreshold && dist < closestDist)
                    {
                        closestDist = dist;
                        closestEntity = entity;
                    }
                }

                if (closestEntity != Entity.Null)
                {
                    ecb.AddComponent<FormationSelected>(closestEntity);
                    UnityEngine.Debug.Log($"Selected formation at {hit.point}");
                }
                else
                {
                    UnityEngine.Debug.Log("Selection cleared");
                }

                ecb.Playback(EntityManager);
                ecb.Dispose();
            }
        }

        // ... (Right Click Down/Hold Logic remains the same) ...
        if (Input.GetMouseButtonDown(1))
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                isRightClickDragging = false;
                dragStartPoint = hit.point;
                dragCurrentPoint = hit.point;
                rightMouseDownTime = SystemAPI.Time.ElapsedTime;
            }
        }

        if (Input.GetMouseButton(1))
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                dragCurrentPoint = hit.point;

                if (!isRightClickDragging)
                {
                    float dist = math.distance(dragStartPoint, dragCurrentPoint);
                    double timeHeld = SystemAPI.Time.ElapsedTime - rightMouseDownTime;

                    if (dist > DragDistanceThreshold || timeHeld > DragTimeThreshold)
                    {
                        isRightClickDragging = true;
                        
                        if (previewVisualizer != null && currentSoldierCount > 0)
                        {
                            previewVisualizer.IsDragging = true;
                                previewVisualizer.DragStart = dragStartPoint;
                            previewVisualizer.DragEnd = dragCurrentPoint;
                            previewVisualizer.SoldierCount = currentSoldierCount;
                            previewVisualizer.spacing = currentSpacing; // Ensure spacing is set
                        }
                    }
                }
                else
                {
                    if (previewVisualizer != null)
                    {
                        previewVisualizer.DragEnd = dragCurrentPoint;
                    }
                }
            }
        }

        // --- UP: Execute Command ---
        if (Input.GetMouseButtonUp(1))
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                dragCurrentPoint = hit.point;
            }

            if (isRightClickDragging)
            {
                ExecuteReformCommand();
            }
            else
            {
                ExecuteMoveCommand(dragCurrentPoint);
            }

            isRightClickDragging = false;
            if (previewVisualizer != null)
            {
                previewVisualizer.IsDragging = false;
            }
        }
    }

    private void ExecuteMoveCommand(float3 targetPos)
    {
        foreach (var group in SystemAPI.Query<RefRW<FormationPositionData>>().WithAll<FormationSelected>())
        {
            group.ValueRW.targetPosition = targetPos;
            
            float3 directionToTarget = targetPos - group.ValueRO.centerPosition;
            if (math.lengthsq(directionToTarget) > 0.001f)
            {
                group.ValueRW.targetFacing = quaternion.LookRotationSafe(math.normalize(directionToTarget), math.up());
            }
            
            float angleDiff = QuaternionAngle(group.ValueRO.facing, group.ValueRW.targetFacing);
            
            if (angleDiff > 0.7f)
            {
                group.ValueRW.formationState = FormationState.rotating;
            }
            else
            {
                group.ValueRW.formationState = FormationState.retargeting;
            }
            
            UnityEngine.Debug.Log($"Move command issued to {targetPos}");
        }
    }

    private void ExecuteReformCommand()
    {
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

            // Use currentSpacing for calculation
            int newColumns = FormationBuilderUtility.CalculateColumnsFromWidth(width, currentSpacing);

            foreach (var group in SystemAPI.Query<RefRW<FormationPositionData>>().WithAll<FormationSelected>())
            {
                group.ValueRW.columns = newColumns;
                group.ValueRW.targetFacing = facing;
                group.ValueRW.targetPosition = center;
                group.ValueRW.formationState = FormationState.rebuilding;

                UnityEngine.Debug.Log($"Reform command issued: {newColumns} cols at {center}");
            }
        }
    }
    
    private static float QuaternionAngle(quaternion a, quaternion b)
    {
        float dot = math.abs(math.dot(a, b));
        return math.acos(math.min(dot, 1.0f)) * 2.0f;
    }
}
