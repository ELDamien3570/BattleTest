using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements.Experimental;

[UpdateInGroup(typeof(InitializationSystemGroup), OrderLast = true)]
public partial class PlayerInputSystem : SystemBase
{
    private FormationPreviewVisualizer previewVisualizer;

    private bool isRightClickDragging = false;
    private float3 dragStartPoint;
    private float3 dragCurrentPoint;
    private float currentSpacing = 1f;

    private const float DragDistanceThreshold = 2f;
    private const double DragTimeThreshold = 0.3;
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
        foreach (var group in SystemAPI.Query<RefRO<FormationPositionData>>().WithAll<FormationSelected>())
        {
            currentSpacing = group.ValueRO.spacing;
            break;
        }

        Camera cam = Camera.main;
        if (cam == null) return;

        HandleLeftClick(cam);
        HandleRightClick(cam);
        HandleCommandedFormations();
    }

    private void HandleCommandedFormations()
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (posData, engagementData, commandData, colliderData, entity) in SystemAPI.Query<RefRW<FormationPositionData>, RefRW<FormationEngagementData>, RefRW<PlayerCommandData>, RefRW<FormationColliderData>>().WithAll<PlayerControlled>().WithEntityAccess())
        {
            Entity target = commandData.ValueRO.setTargetFormation;

            if (target == Entity.Null) continue;

            if (!EntityManager.Exists(target))
            {
                commandData.ValueRW.setTargetFormation = Entity.Null;
                continue;
            }

            if (SystemAPI.HasComponent<FormationCombatData>(target))
            {
                var targetCombat = SystemAPI.GetComponent<FormationCombatData>(target);
                if (targetCombat.formationWiped)
                {
                    commandData.ValueRW.setTargetFormation = Entity.Null;
                    continue;
                }
            }

           // if (engagementData.ValueRO.isEngaged) continue;

            commandData.ValueRW.timeSinceCommand += deltaTime;

            if (commandData.ValueRO.timeSinceCommand >= 1.0f)
            {
                engagementData.ValueRW.targetFormation = target;
                ExecuteAttackCommandForEntity(posData, colliderData, target, entity);
                commandData.ValueRW.timeSinceCommand = 0f;
            }
        }
    }

    private void HandleLeftClick(Camera cam)
    {
        if (!Input.GetMouseButtonDown(0)) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (tag, entity) in SystemAPI.Query<RefRO<FormationSelected>>().WithEntityAccess())
        {
            ecb.RemoveComponent<FormationSelected>(entity);
        }

        Entity closest = FindClosestPlayerFormation(hit.point);
        if (closest != Entity.Null)
        {
            ecb.AddComponent<FormationSelected>(closest);
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }

    private void HandleRightClick(Camera cam)
    {
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
                    }
                }

                if (isRightClickDragging && previewVisualizer != null)
                {
                    previewVisualizer.IsDragging = true;
                    previewVisualizer.DragStart = dragStartPoint;
                    previewVisualizer.DragEnd = dragCurrentPoint;
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

            if (isRightClickDragging)
            {
                ExecuteReformCommand();
            }
            else
            {
                Entity enemy = FindClosestEnemyFormation(dragCurrentPoint);
                if (enemy != Entity.Null)
                {
                    ExecuteAttackCommand(enemy);
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

        int enemyRows = (int)math.ceil((float)enemyPos.soldierCount / enemyPos.columns);
        float enemyDepth = (enemyRows - 1) * enemyPos.spacing;

        float3 enemyForward = math.forward(enemyPos.facing);
        float3 enemyRight = math.cross(math.up(), enemyForward);
        float3 enemyTrueCenter = enemyPos.centerPosition - enemyForward * (enemyDepth * 0.5f);

        int formationIndex = 0;
        foreach (var (group, colliderData, commandData, entity) in SystemAPI.Query<RefRW<FormationPositionData>, RefRW<FormationColliderData>, RefRW<PlayerCommandData>>().WithAll<FormationSelected>().WithEntityAccess())
        {
            commandData.ValueRW.setTargetFormation = targetEnemy;
            commandData.ValueRW.timeSinceCommand = 0f;

            float3 directionToEnemy = enemyPos.centerPosition - group.ValueRO.centerPosition;
            directionToEnemy.y = 0;

            if (math.lengthsq(directionToEnemy) < 0.001f) continue;

            float3 attackDirection = math.normalize(directionToEnemy);

            uint seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + (uint)entity.Index + (uint)formationIndex;
            var rng = new Unity.Mathematics.Random(seed);

            float depthVariation = rng.NextFloat(-0.15f, 0.15f);
            float randomizedDepth = math.clamp(group.ValueRO.desiredAttackDepth + depthVariation, 0f, 1f);

            float3 targetPos = math.lerp(enemyPos.centerPosition, enemyTrueCenter, randomizedDepth * 2f);
            targetPos.y = enemyPos.centerPosition.y;

            float lateralOffset = rng.NextFloat(-1.5f, 1.5f);
            targetPos += enemyRight * lateralOffset;

            group.ValueRW.targetPosition = targetPos;
            group.ValueRW.targetFacing = quaternion.LookRotationSafe(attackDirection, math.up());
            group.ValueRW.formationState = FormationState.rotating;
            colliderData.ValueRW.needsRebuilt = true;
            
            formationIndex++;
        }
    }

    private void ExecuteAttackCommandForEntity(RefRW<FormationPositionData> group, RefRW<FormationColliderData> colliderData, Entity targetEnemy, Entity attackerEntity)
    {
        var enemyPos = SystemAPI.GetComponent<FormationPositionData>(targetEnemy);

        int enemyRows = (int)math.ceil((float)enemyPos.soldierCount / enemyPos.columns);
        float enemyDepth = (enemyRows - 1) * enemyPos.spacing;

        float3 enemyForward = math.forward(enemyPos.facing);
        float3 enemyRight = math.cross(math.up(), enemyForward);
        float3 enemyTrueCenter = enemyPos.centerPosition - enemyForward * (enemyDepth * 0.5f);

        float3 directionToEnemy = enemyPos.centerPosition - group.ValueRO.centerPosition;
        directionToEnemy.y = 0;

        if (math.lengthsq(directionToEnemy) < 0.001f) return;

        float3 attackDirection = math.normalize(directionToEnemy);

        uint seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + (uint)attackerEntity.Index;
        var rng = new Unity.Mathematics.Random(seed);

        float depthVariation = rng.NextFloat(-0.15f, 0.15f);
        float randomizedDepth = math.clamp(group.ValueRO.desiredAttackDepth + depthVariation, 0f, 1f);

        float3 targetPos = math.lerp(enemyPos.centerPosition, enemyTrueCenter, randomizedDepth * 2f);
        targetPos.y = enemyPos.centerPosition.y;

        float lateralOffset = rng.NextFloat(-1.5f, 1.5f);
        targetPos += enemyRight * lateralOffset;

        group.ValueRW.targetPosition = targetPos;
        group.ValueRW.targetFacing = quaternion.LookRotationSafe(attackDirection, math.up());
        group.ValueRW.formationState = FormationState.retargeting;
        colliderData.ValueRW.needsRebuilt = true;
    }

    private void ExecuteMoveCommand(float3 targetPos)
    {
        foreach (var (group, colliderData, commandData) in SystemAPI.Query<RefRW<FormationPositionData>, RefRW<FormationColliderData>, RefRW<PlayerCommandData>>().WithAll<FormationSelected>())
        {
            commandData.ValueRW.setTargetFormation = Entity.Null;

            group.ValueRW.targetPosition = targetPos;

            float3 direction = targetPos - group.ValueRO.centerPosition;
            if (math.lengthsq(direction) > 0.001f)
            {
                group.ValueRW.targetFacing = quaternion.LookRotationSafe(math.normalize(direction), math.up());
            }

            float angleDiff = AngleComparator(group.ValueRO.facing, group.ValueRW.targetFacing);
            group.ValueRW.formationState = angleDiff > group.ValueRO.facingTolerance ? FormationState.rotating : FormationState.retargeting;

            colliderData.ValueRW.needsRebuilt = true;
        }
    }

    private void ExecuteReformCommand()
    {
        float3 center = (dragStartPoint + dragCurrentPoint) * 0.5f;
        float3 rightVec = dragCurrentPoint - dragStartPoint;
        float width = math.length(rightVec);

        if (width < 0.1f) return;

        rightVec /= width;
        float3 forwardVec = new float3(-rightVec.z, 0, rightVec.x);
        quaternion facing = quaternion.LookRotationSafe(forwardVec, math.up());
        int newColumns = FormationBuilderUtility.CalculateColumnsFromWidth(width, currentSpacing);

        foreach (var (group, colliderData, commandData) in SystemAPI.Query<RefRW<FormationPositionData>, RefRW<FormationColliderData>, RefRW<PlayerCommandData>>().WithAll<FormationSelected>())
        {
            commandData.ValueRW.setTargetFormation = Entity.Null;

            group.ValueRW.columns = newColumns;
            group.ValueRW.targetFacing = facing;
            group.ValueRW.targetPosition = center;
            group.ValueRW.formationState = FormationState.rebuilding;
            colliderData.ValueRW.needsRebuilt = true;
        }
    }

    #region Helpers
    private Entity FindClosestPlayerFormation(float3 point)
    {
        Entity closest = Entity.Null;
        float closestDist = float.MaxValue;

        foreach (var (pos, entity) in SystemAPI.Query<RefRO<FormationPositionData>>().WithAll<PlayerControlled>().WithEntityAccess())
        {
            float dist = GetFormationDistance(pos.ValueRO, point);
            float threshold = GetFormationThreshold(pos.ValueRO);

            if (dist < threshold && dist < closestDist)
            {
                closestDist = dist;
                closest = entity;
            }
        }
        return closest;
    }

    private Entity FindClosestEnemyFormation(float3 point)
    {
        Entity closest = Entity.Null;
        float closestDist = float.MaxValue;

        foreach (var (pos, entity) in SystemAPI.Query<RefRO<FormationPositionData>>().WithAll<AiControlled>().WithEntityAccess())
        {
            float dist = GetFormationDistance(pos.ValueRO, point);
            float threshold = GetFormationThreshold(pos.ValueRO);

            if (dist < threshold && dist < closestDist)
            {
                closestDist = dist;
                closest = entity;
            }
        }
        return closest;
    }

    private float GetFormationDistance(FormationPositionData pos, float3 point)
    {
        return math.distance(new float2(pos.centerPosition.x, pos.centerPosition.z), new float2(point.x, point.z));
    }

    private float GetFormationThreshold(FormationPositionData pos)
    {
        int rows = (int)math.ceil((float)pos.soldierCount / pos.columns);
        float width = pos.columns * pos.spacing;
        float depth = rows * pos.spacing;
        float radius = math.length(new float2(width, depth)) * 0.6f;
        return math.max(3.0f, radius);
    }

    private static float AngleComparator(quaternion a, quaternion b)
    {
        float dot = math.abs(math.dot(a, b));
        return math.acos(math.min(dot, 1.0f)) * 2.0f;
    }
    #endregion

}