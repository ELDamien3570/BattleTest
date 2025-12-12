using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

[UpdateInGroup(typeof(PhysicsSystemGroup))]
[UpdateAfter(typeof(PhysicsSimulationGroup))]
public partial struct FormationEngagementSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SimulationSingleton>();
    }

    public void OnUpdate(ref SystemState state)
    {


        var simulation = SystemAPI.GetSingleton<SimulationSingleton>();

        // Get lookups for component access in the job
        var positionLookup = SystemAPI.GetComponentLookup<FormationPositionData>(true);
        var colliderDataLookup = SystemAPI.GetComponentLookup<FormationColliderData>(true);
        var offsetLookup = SystemAPI.GetBufferLookup<SoldierOffset>(false);

        state.Dependency = new ProcessEngagementsJob
        {
            PositionLookup = positionLookup,
            ColliderDataLookup = colliderDataLookup,
            OffsetLookup = offsetLookup,
            EngagementDistance = 2f  // How close soldiers must be to engage
        }.Schedule(simulation, state.Dependency);
    }
}

[BurstCompile]
struct ProcessEngagementsJob : ITriggerEventsJob
{

    
    [ReadOnly] public ComponentLookup<FormationPositionData> PositionLookup;
    [ReadOnly] public ComponentLookup<FormationColliderData> ColliderDataLookup;
    public BufferLookup<SoldierOffset> OffsetLookup;

    public float EngagementDistance;

    public void Execute(TriggerEvent triggerEvent)
    {
        Entity entityA = triggerEvent.EntityA;
        Entity entityB = triggerEvent.EntityB;
        // Make sure both are formations
        if (!PositionLookup.HasComponent(entityA) || !PositionLookup.HasComponent(entityB))
            return;

        var posA = PositionLookup[entityA];
        var posB = PositionLookup[entityB];

        var offsetsA = OffsetLookup[entityA];
        var offsetsB = OffsetLookup[entityB];

        // Flag soldiers that are close to enemy soldiers
        FlagEngagedSoldiers(ref offsetsA, posA, ref offsetsB, posB);
    }

    private void FlagEngagedSoldiers(
        ref DynamicBuffer<SoldierOffset> offsetsA, FormationPositionData posA,
        ref DynamicBuffer<SoldierOffset> offsetsB, FormationPositionData posB)
    {
        // Check each soldier in A against each soldier in B
        for (int i = 0; i < offsetsA.Length; i++)
        {
            var soldierA = offsetsA[i];
            float3 worldPosA = posA.centerPosition + math.rotate(posA.facing, soldierA.currentOffset);

            for (int j = 0; j < offsetsB.Length; j++)
            {
                var soldierB = offsetsB[j];
                float3 worldPosB = posB.centerPosition + math.rotate(posB.facing, soldierB.currentOffset);

                float dist = math.distance(worldPosA, worldPosB);

                if (dist < EngagementDistance)
                {
                    // Flag both soldiers as engaged
                    soldierA.isEngaged = true;
                    soldierB.isEngaged = true;
                    offsetsB[j] = soldierB;
                }
            }

            offsetsA[i] = soldierA;
        }
    }
}

[UpdateInGroup(typeof(PhysicsSystemGroup))]
[UpdateBefore(typeof(PhysicsSimulationGroup))]
public partial struct ClearEngagementSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (offsets, entity) in SystemAPI.Query<DynamicBuffer<SoldierOffset>>().WithEntityAccess())
        {
            var buffer = state.EntityManager.GetBuffer<SoldierOffset>(entity);

            for (int i = 0; i < buffer.Length; i++)
            {
                var soldier = buffer[i];
                soldier.isEngaged = false;
                buffer[i] = soldier;
            }
        }
    }
}