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
    private float currentSpacing = 1f; 
    
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
        }
    }

    protected override void OnUpdate()
    {
        currentSoldierCount = 0;
        foreach (var group in SystemAPI.Query<RefRO<FormationPositionData>>().WithAll<FormationSelected>())
        {
            currentSoldierCount = group.ValueRO.soldierCount;
            currentSpacing = group.ValueRO.spacing; 

            if (previewVisualizer != null) 
            {
                previewVisualizer.SoldierCount = currentSoldierCount;
                previewVisualizer.spacing = currentSpacing; 
            }
            break; 
        }

        Camera cam = Camera.main;
        if (cam == null) return;

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

                foreach (var (pos, playerForm, entity) in SystemAPI.Query<RefRO<FormationPositionData>, RefRO<PlayerControlled>>().WithEntityAccess())
                {
                    float dist = math.distance(new float2(pos.ValueRO.centerPosition.x, pos.ValueRO.centerPosition.z), new float2(hit.point.x, hit.point.z));
                    
                    int rows = (int)math.ceil((float)pos.ValueRO.soldierCount / pos.ValueRO.columns);
                    float width = pos.ValueRO.columns * pos.ValueRO.spacing;
                    float depth = rows * pos.ValueRO.spacing;
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
                }
                else
                {
                }

                ecb.Playback(EntityManager);
                ecb.Dispose();
            }
        }

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
                            previewVisualizer.spacing = currentSpacing;
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

        if (Input.GetMouseButtonUp(1))
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                dragCurrentPoint = hit.point;
            }

            bool commandExecuted = false;

            if (!isRightClickDragging)
            {
                // Check if we clicked on an enemy formation
                Entity closestEnemy = Entity.Null;
                float closestDist = float.MaxValue;

                foreach (var (pos, entity) in SystemAPI.Query<RefRO<FormationPositionData>>().WithNone<PlayerControlled>().WithEntityAccess())
                {
                    float dist = math.distance(new float2(pos.ValueRO.centerPosition.x, pos.ValueRO.centerPosition.z), new float2(dragCurrentPoint.x, dragCurrentPoint.z));
                    
                    int rows = (int)math.ceil((float)pos.ValueRO.soldierCount / pos.ValueRO.columns);
                    float width = pos.ValueRO.columns * pos.ValueRO.spacing;
                    float depth = rows * pos.ValueRO.spacing;
                    float formationRadius = math.length(new float2(width, depth)) * 0.6f; 
                    
                    float selectionThreshold = math.max(3.0f, formationRadius);

                    if (dist < selectionThreshold && dist < closestDist)
                    {
                        closestDist = dist;
                        closestEnemy = entity;
                    }
                }

                if (closestEnemy != Entity.Null)
                {
                    ExecuteAttackCommand(closestEnemy);
                    commandExecuted = true;
                }
            }

            if (!commandExecuted)
            {
                if (isRightClickDragging)
                {
                    ExecuteReformCommand();
                }
                else
                {
                    ExecuteMoveCommand(dragCurrentPoint);
                }
            }

            isRightClickDragging = false;
            if (previewVisualizer != null)
            {
                previewVisualizer.IsDragging = false;
            }
        }
    }

    private void ExecuteAttackCommand(Entity targetEnemy)
    {
        var enemyPos = SystemAPI.GetComponent<FormationPositionData>(targetEnemy);

        foreach (var (group, colliderData) in SystemAPI.Query<RefRW<FormationPositionData>, RefRW<FormationColliderData>>().WithAll<FormationSelected>())
        {
            group.ValueRW.targetPosition = enemyPos.centerPosition + new float3(1, 0 ,1);
            float3 enemyForward = math.mul(enemyPos.facing, new float3(0, 0, 1));
            group.ValueRW.targetFacing = quaternion.LookRotationSafe(-enemyForward, math.up());
            group.ValueRW.formationState = FormationState.rotating;
            colliderData.ValueRW.needsRebuilt = true;
        }
    }

    private void ExecuteMoveCommand(float3 targetPos)
    {
        foreach (var (group, colliderData) in SystemAPI.Query<RefRW<FormationPositionData>, RefRW<FormationColliderData>>().WithAll<FormationSelected>())
        {
            group.ValueRW.targetPosition = targetPos;
            
            float3 directionToTarget = targetPos - group.ValueRO.centerPosition;
            if (math.lengthsq(directionToTarget) > 0.001f)
            {
                group.ValueRW.targetFacing = quaternion.LookRotationSafe(math.normalize(directionToTarget), math.up());
            }
            
            float angleDiff = QuaternionAngle(group.ValueRO.facing, group.ValueRW.targetFacing);
            
            if (angleDiff > group.ValueRO.facingTolerance)
            {
                group.ValueRW.formationState = FormationState.rotating;
                colliderData.ValueRW.needsRebuilt = true;
            }
            else
            {
                group.ValueRW.formationState = FormationState.retargeting;
                colliderData.ValueRW.needsRebuilt = true;
            }
            
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

            int newColumns = FormationBuilderUtility.CalculateColumnsFromWidth(width, currentSpacing);

            foreach (var (group, colliderData) in SystemAPI.Query<RefRW<FormationPositionData>, RefRW<FormationColliderData>>().WithAll<FormationSelected>())
            {
                group.ValueRW.columns = newColumns;
                group.ValueRW.targetFacing = facing;
                group.ValueRW.targetPosition = center;
                group.ValueRW.formationState = FormationState.rebuilding;
                colliderData.ValueRW.needsRebuilt = true;
            }
        }
    }
    
    private static float QuaternionAngle(quaternion a, quaternion b)
    {
        float dot = math.abs(math.dot(a, b));
        return math.acos(math.min(dot, 1.0f)) * 2.0f;
    }
}
