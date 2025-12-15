using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public partial struct AiTargetingSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (posData, engagementData, commandData, colliderData, combatData, entity) in SystemAPI.Query<RefRW<FormationPositionData>, RefRW<FormationEngagementData>, RefRW<AiCommandData>, RefRW<FormationColliderData>, 
            RefRO<FormationCombatData>>().WithAll<AiControlled>().WithEntityAccess())
        {
            commandData.ValueRW.timeSinceCommand += deltaTime;

            if (engagementData.ValueRO.isEngaged) continue;

            if (commandData.ValueRO.timeSinceCommand >= 1.0f)
            {
                var target = FindClosestPlayerFormationByType(ref state, posData.ValueRO.centerPosition, combatData.ValueRO.troopType);
                if (target != Entity.Null)
                {
                    engagementData.ValueRW.targetFormation = target;
                    ExecuteAttackCommand(ref state, posData, colliderData, target);
   
                    commandData.ValueRW.timeSinceCommand = 0f;
                }
                else
                {
                    var targetAny = FindClosestPlayerFormation(ref state, posData.ValueRO.centerPosition);
                    if (target != Entity.Null)
                    {
                        engagementData.ValueRW.targetFormation = targetAny;
                        ExecuteAttackCommand(ref state, posData, colliderData, targetAny);

                        commandData.ValueRW.timeSinceCommand = 0f;
                    }
                }
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

    private void ExecuteAttackCommand(ref SystemState state, RefRW<FormationPositionData> aiPos, RefRW<FormationColliderData> aiCollider, Entity targetEnemy)
    {
        var enemyPos = SystemAPI.GetComponent<FormationPositionData>(targetEnemy);
        
        int enemyRows = (int)math.ceil((float)enemyPos.soldierCount / enemyPos.columns);
        float enemyDepth = (enemyRows - 1) * enemyPos.spacing;
        
        float3 enemyForward = math.forward(enemyPos.facing);
        float3 enemyRight = math.cross(math.up(), enemyForward);
        float3 enemyTrueCenter = enemyPos.centerPosition - enemyForward * (enemyDepth * 0.5f);

        float3 directionToEnemy = enemyPos.centerPosition - aiPos.ValueRO.centerPosition;
        directionToEnemy.y = 0;

        if (math.lengthsq(directionToEnemy) > 0.1f)
        {
            float3 attackDirection = math.normalize(directionToEnemy);
            
            uint seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + (uint)targetEnemy.Index;
            var rng = new Unity.Mathematics.Random(seed);
            
            float depthVariation = rng.NextFloat(-0.15f, 0.15f);
            float randomizedDepth = math.clamp(aiPos.ValueRO.desiredAttackDepth + depthVariation, 0f, 1f);
            
            float3 targetPos = math.lerp(enemyPos.centerPosition, enemyTrueCenter, randomizedDepth * 2f);
            targetPos.y = enemyPos.centerPosition.y;

            float lateralOffset = rng.NextFloat(-1.5f, 1.5f);
            targetPos += enemyRight * lateralOffset;

            aiPos.ValueRW.targetPosition = targetPos;
            aiPos.ValueRW.targetFacing = quaternion.LookRotationSafe(attackDirection, math.up());
            aiPos.ValueRW.formationState = FormationState.retargeting;
            aiCollider.ValueRW.needsRebuilt = true;
        }
    }

    #region Helpers
    private Entity FindClosestPlayerFormationByType(ref SystemState state, float3 point, int myTroopType)
    {
        Entity closest = Entity.Null;
        float closestDist = float.MaxValue;

        foreach (var (pos, combat, entity) in SystemAPI.Query<RefRO<FormationPositionData>, RefRO<FormationCombatData>>().WithAll<PlayerControlled>().WithEntityAccess())
        {
            if (combat.ValueRO.troopType != myTroopType) continue;

            float dist = GetFormationDistance(pos.ValueRO, point);

            if (dist < closestDist)
            {
                closestDist = dist;
                closest = entity;
            }
        }
        return closest;
    }

    private Entity FindClosestPlayerFormation(ref SystemState state, float3 point)
    {
        Entity closest = Entity.Null;
        float closestDist = float.MaxValue;

        foreach (var (pos, combat, entity) in SystemAPI.Query<RefRO<FormationPositionData>, RefRO<FormationCombatData>>().WithAll<PlayerControlled>().WithEntityAccess())
        {
           
            float dist = GetFormationDistance(pos.ValueRO, point);

            if (dist < closestDist)
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

    #endregion
}
