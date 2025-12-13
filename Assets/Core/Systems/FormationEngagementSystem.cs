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

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var simulation = SystemAPI.GetSingleton<SimulationSingleton>();

        state.Dependency = new ProcessEngagementsJob
        {
            PositionLookup = SystemAPI.GetComponentLookup<FormationPositionData>(true),
            EngagementDataLookup = SystemAPI.GetComponentLookup<FormationEngagementData>(false),
            OffsetLookup = SystemAPI.GetBufferLookup<SoldierOffset>(false),
            EngagementDistance = 1.5f // Tightened range to prevent back-rank engagement
        }.Schedule(simulation, state.Dependency);
    }
}

[BurstCompile]
struct ProcessEngagementsJob : ITriggerEventsJob
{
    [ReadOnly] public ComponentLookup<FormationPositionData> PositionLookup;
    public ComponentLookup<FormationEngagementData> EngagementDataLookup;
    public BufferLookup<SoldierOffset> OffsetLookup;
    public float EngagementDistance;

    public void Execute(TriggerEvent triggerEvent)
    {
        Entity entityA = triggerEvent.EntityA;
        Entity entityB = triggerEvent.EntityB;

        if (!PositionLookup.HasComponent(entityA) || !PositionLookup.HasComponent(entityB))
            return;

        if (!EngagementDataLookup.HasComponent(entityA) || !EngagementDataLookup.HasComponent(entityB))
            return;

        var posA = PositionLookup[entityA];
        var posB = PositionLookup[entityB];
        
        var offsetsA = OffsetLookup[entityA];
        var offsetsB = OffsetLookup[entityB];

        float engagementDistSq = EngagementDistance * EngagementDistance;

        // Calculate formation dimensions
        float widthA = math.max(0, (posA.columns - 1) * posA.spacing);
        float depthA = math.max(0, (posA.rows - 1) * posA.spacing);

        float widthB = math.max(0, (posB.columns - 1) * posB.spacing);
        float depthB = math.max(0, (posB.rows - 1) * posB.spacing);

        // Check if soldiers are within engagement distance of the enemy formation's bounds
        bool anyEngagedA = FlagSoldiersInEngagementRange(ref offsetsA, posA, posB, widthB, depthB, engagementDistSq);
        
        if (anyEngagedA)
        {
            var engDataA = EngagementDataLookup[entityA];
            engDataA.isEngaged = true;
            engDataA.needsEngagementUpdate = false;
            engDataA.targetFormation = entityB;
            engDataA.lastEngagementPos = posA.centerPosition;
            engDataA.lastEngagementRot = posA.facing;
            EngagementDataLookup[entityA] = engDataA;
        }

        bool anyEngagedB = FlagSoldiersInEngagementRange(ref offsetsB, posB, posA, widthA, depthA, engagementDistSq);

        if (anyEngagedB)
        {
            var engDataB = EngagementDataLookup[entityB];
            engDataB.isEngaged = true;
            engDataB.needsEngagementUpdate = false;
            engDataB.targetFormation = entityA;
            engDataB.lastEngagementPos = posB.centerPosition;
            engDataB.lastEngagementRot = posB.facing;
            EngagementDataLookup[entityB] = engDataB;
        }
    }

    private bool FlagSoldiersInEngagementRange(
        ref DynamicBuffer<SoldierOffset> offsets,
        FormationPositionData myPos,
        FormationPositionData enemyPos,
        float enemyWidth,
        float enemyDepth,
        float engagementDistSq)
    {
        bool anyEngaged = false;

        // Prepare Enemy Transformation
        // The formation anchor (centerPosition) is at the FRONT center of the formation.
        float3 enemyAnchor = enemyPos.centerPosition; 
        quaternion enemyInverseRot = math.inverse(enemyPos.facing);
        
        // Define Enemy Bounds in Local Space relative to the anchor.
        // X is centered: [-Width/2, Width/2]
        // Z extends backwards: [-Depth, 0]
        float minX = -enemyWidth * 0.5f;
        float maxX = enemyWidth * 0.5f;
        float minZ = -enemyDepth;
        float maxZ = 0f;

        for (int i = 0; i < offsets.Length; i++)
        {
            var soldier = offsets[i];
            
            // Calculate Soldier World Position
            float3 soldierWorldPos = myPos.centerPosition + math.rotate(myPos.facing, soldier.currentOffset);

            // Transform Soldier to Enemy Local Space
            float3 localPos = math.rotate(enemyInverseRot, soldierWorldPos - enemyAnchor);

            // Calculate distance to the Enemy Rectangle in local space
            
            // Distance in X
            float dx = 0f;
            if (localPos.x < minX) dx = minX - localPos.x;
            else if (localPos.x > maxX) dx = localPos.x - maxX;

            // Distance in Z
            float dz = 0f;
            if (localPos.z < minZ) dz = minZ - localPos.z;
            else if (localPos.z > maxZ) dz = localPos.z - maxZ;
            
            float distSq = dx * dx + dz * dz;

            if (distSq < engagementDistSq && !soldier.isDead)
            {
                soldier.isEngaged = true;
                offsets[i] = soldier;
                anyEngaged = true;
            }
        }

        return anyEngaged;
    }
}


[UpdateInGroup(typeof(PhysicsSystemGroup))]
[UpdateBefore(typeof(PhysicsSimulationGroup))]
[BurstCompile]
public partial struct ClearEngagementSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var groups in SystemAPI.Query<RefRW<FormationEngagementData>>()) groups.ValueRW.targetFormation = default;
        new ClearEngagementsJob().ScheduleParallel();
    }
}

[BurstCompile]
partial struct ClearEngagementsJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<SoldierOffset> offsets, ref FormationEngagementData engagementData)
    {
        if (engagementData.isEngaged)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                var soldier = offsets[i];
                if (soldier.isEngaged)
                {
                        soldier.isEngaged = false;
                        offsets[i] = soldier;
                }
            }
            engagementData.isEngaged = false;
        }
    }
}