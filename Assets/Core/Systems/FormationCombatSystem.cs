using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct FormationCombatSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        // Apply damage and mark formations for death
        foreach (var (offsets, engagementData, combatdata) in
            SystemAPI.Query<DynamicBuffer<SoldierOffset>, RefRO<FormationEngagementData>, RefRW<FormationCombatData>>())
        {
            float damageThisFrame = combatdata.ValueRO.meleeDamage * deltaTime;

            for (int i = 0; i < offsets.Length; i++)
            {
                var soldier = offsets[i];
                if (soldier.isEngaged)
                {
                    var enemyGroup = engagementData.ValueRO.targetFormation;

                    // Check if the entity is valid and exists before accessing it
                    if (enemyGroup != Entity.Null && state.EntityManager.Exists(enemyGroup))
                    {
                        var enemyCombatData = SystemAPI.GetComponent<FormationCombatData>(enemyGroup);
                        enemyCombatData.formationHealth -= damageThisFrame;
                        SystemAPI.SetComponent(enemyGroup, enemyCombatData);
                    }
                }
                  
            }

            float healthDif = combatdata.ValueRO.lastDeathHealth - combatdata.ValueRO.formationHealth;
            if (healthDif > combatdata.ValueRO.healthPerTroop)
            {
                combatdata.ValueRW.markedForDeath = true;
            }
        }

        var jobHandle = new KillSoldiersJob().ScheduleParallel(state.Dependency);
        jobHandle.Complete();
    }
}

[BurstCompile]
partial struct KillSoldiersJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<SoldierOffset> offsets, ref FormationCombatData combatData)
    {
        if (!combatData.markedForDeath) return;

        for (int i = 0; i < offsets.Length; i++)
        {
            var soldier = offsets[i];
            if (soldier.isEngaged)
            {
                soldier.isDead = true;
                soldier.isEngaged = false;
                soldier.isMoving = false;
                offsets[i] = soldier;

                combatData.markedForDeath = false;
                combatData.lastDeathHealth = combatData.formationHealth;
                break;
            }
        }
    }
}

[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(FormationCombatSystem))]
public partial struct DeadSoldierRemovalSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        float deathAnimationDuration = 1f;


        var bufferLookup = SystemAPI.GetBufferLookup<SoldierOffset>(false);

        foreach (var (positionData, colliderData, combatData, entity) in SystemAPI.Query<RefRW<FormationPositionData>, RefRW<FormationColliderData>, RefRW<FormationCombatData>>().WithEntityAccess())
        {
            if (!bufferLookup.HasBuffer(entity)) continue;

            var offsets = bufferLookup[entity];
            bool needsRebuild = false;

            for (int i = offsets.Length - 1; i >= 0; i--)
            {
                var soldier = offsets[i];

                if (!soldier.isDead) continue;

                soldier.deathTimer += deltaTime;
                offsets[i] = soldier;

                if (soldier.deathTimer >= deathAnimationDuration)
                {
                    offsets.RemoveAt(i);
                    needsRebuild = true;
                }
            }

            if (needsRebuild)
            {
                positionData.ValueRW.soldierCount = offsets.Length;

                if (positionData.ValueRO.formationState == FormationState.idle ||
                    positionData.ValueRO.formationState == FormationState.retargeting)
                {
                    positionData.ValueRW.formationState = FormationState.rebuilding;
                }

                colliderData.ValueRW.needsRebuilt = true;
            }
        }   
    }

}