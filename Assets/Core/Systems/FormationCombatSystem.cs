using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Physics;

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct FormationCombatSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (offsets, engagementData, combatData) in SystemAPI.Query<DynamicBuffer<SoldierOffset>, RefRO<FormationEngagementData>, RefRW<FormationCombatData>>())
        {
            float damageThisFrame = combatData.ValueRO.meleeDamage * deltaTime;

            int engagedCount = 0;
            for (int i = 0; i < offsets.Length; i++)
            {
                var soldier = offsets[i];
                if (soldier.isEngaged && !soldier.isDead)
                {
                    engagedCount++;
                    var enemyGroup = engagementData.ValueRO.targetFormation;

                    if (enemyGroup != Entity.Null && state.EntityManager.Exists(enemyGroup))
                    {
                        var enemyCombatData = SystemAPI.GetComponent<FormationCombatData>(enemyGroup);
                        float defenseThisFrame = enemyCombatData.meleeDefense * deltaTime;
                        float totalDamageThisFrame = damageThisFrame - defenseThisFrame;
                        totalDamageThisFrame = totalDamageThisFrame > 0 ? totalDamageThisFrame : 0.1f;

                        enemyCombatData.formationHealth -= totalDamageThisFrame;
                        SystemAPI.SetComponent(enemyGroup, enemyCombatData);
                    }
                }
            }

            int livingSoldiers = 0;
            for (int i = 0; i < offsets.Length; i++)
            {
                if (!offsets[i].isDead) livingSoldiers++;
            }

            int expectedSoldiers = 0;
            if (combatData.ValueRO.healthPerTroop > 0)
            {
                expectedSoldiers = (int)System.Math.Ceiling(
                    combatData.ValueRO.formationHealth / combatData.ValueRO.healthPerTroop
                );
            }
            expectedSoldiers = Unity.Mathematics.math.max(0, expectedSoldiers);

            int deathsNeeded = livingSoldiers - expectedSoldiers;

            if (deathsNeeded > 0)
            {
                combatData.ValueRW.deathsQueued = deathsNeeded;
            }

            if (combatData.ValueRO.formationHealth <= 0 && livingSoldiers > 0 && expectedSoldiers == 0)
            {
                combatData.ValueRW.formationWiped = true;
            }
        }

        state.Dependency = new KillSoldiersJob().ScheduleParallel(state.Dependency);
        state.Dependency.Complete();
    }
}

[BurstCompile]
partial struct KillSoldiersJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<SoldierOffset> offsets, ref FormationCombatData combatData)
    {
        uint seed = (uint)(offsets.Length * 31 + 17);
        var rng = new Unity.Mathematics.Random(seed);

        if (combatData.formationWiped)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                var soldier = offsets[i];
                if (!soldier.isDead)
                {
                    soldier.isDead = true;
                    soldier.isEngaged = false;
                    soldier.isMoving = false;
                    soldier.deathTimer = rng.NextFloat(0f, 0.3f);
                    offsets[i] = soldier;
                }
            }
            combatData.formationWiped = false;
            combatData.deathsQueued = 0;
            return;
        }

        if (combatData.deathsQueued <= 0) return;

        int killed = 0;

        for (int i = 0; i < offsets.Length && killed < combatData.deathsQueued; i++)
        {
            var soldier = offsets[i];
            if (soldier.isEngaged && !soldier.isDead)
            {
                soldier.isDead = true;
                soldier.isEngaged = false;
                soldier.isMoving = false;
                soldier.deathTimer = rng.NextFloat(0f, 0.15f);
                offsets[i] = soldier;
                killed++;
            }
        }

        for (int i = 0; i < offsets.Length && killed < combatData.deathsQueued; i++)
        {
            var soldier = offsets[i];
            if (!soldier.isDead)
            {
                soldier.isDead = true;
                soldier.isEngaged = false;
                soldier.isMoving = false;
                soldier.deathTimer = rng.NextFloat(0f, 0.15f);
                offsets[i] = soldier;
                killed++;
            }
        }

        combatData.deathsQueued = 0;
    }
}

[UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
public partial struct DeadSoldierRemovalSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        float deathAnimationDuration = 2f;

        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
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

            if (offsets.Length == 0)
            {
                ecb.DestroyEntity(entity);
                continue;
            }

            if (needsRebuild)
            {
                positionData.ValueRW.soldierCount = offsets.Length;

                if (positionData.ValueRO.formationState == FormationState.idle || positionData.ValueRO.formationState == FormationState.retargeting)
                {
                    positionData.ValueRW.formationState = FormationState.rebuilding;
                }

                colliderData.ValueRW.needsRebuilt = true;
            }
        }
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}