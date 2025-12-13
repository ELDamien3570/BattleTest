using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

[UpdateInGroup(typeof(PhysicsSystemGroup))]
[UpdateBefore(typeof(PhysicsSimulationGroup))]
public partial struct ClearEngagementSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Reverted to the stable "Clear All" approach.
        // This runs before physics, clearing the state.
        // The EngagementSystem runs after physics and re-applies state for valid engagements.

        var bufferLookup = SystemAPI.GetBufferLookup<SoldierOffset>(false);

        foreach (var (engagementData, entity) in SystemAPI.Query<RefRW<FormationEngagementData>>().WithEntityAccess())
        {
            if (engagementData.ValueRO.isEngaged)
            {
                engagementData.ValueRW.isEngaged = false;
                engagementData.ValueRW.targetFormation = Entity.Null;

                if (bufferLookup.HasBuffer(entity))
                {
                    var offsets = bufferLookup[entity];
                    for (int i = 0; i < offsets.Length; i++)
                    {
                        var soldier = offsets[i];
                        if (soldier.isEngaged)
                        {
                            soldier.isEngaged = false;
                            offsets[i] = soldier;
                        }
                    }
                }
            }
        }
    }
}

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
            EngagementDistance = 1.5f
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

        // MATCHING COLLIDER GENERATION LOGIC:
        // The collider system uses: ((columns - 1) * spacing + 2.0f) * 1.1f
        // We must use the same dimensions to ensure the OBB check matches the trigger event.
        
        float rawWidthA = (posA.columns - 1) * posA.spacing + 2.0f;
        float rawDepthA = (int)math.ceil((float)posA.soldierCount / posA.columns) * posA.spacing + 2.0f; // Using calculated rows
        
        float widthA = math.max(0.2f, rawWidthA) * 1.1f;
        float depthA = math.max(0.2f, rawDepthA) * 1.1f;

        float rawWidthB = (posB.columns - 1) * posB.spacing + 2.0f;
        float rawDepthB = (int)math.ceil((float)posB.soldierCount / posB.columns) * posB.spacing + 2.0f;
        
        float widthB = math.max(0.2f, rawWidthB) * 1.1f;
        float depthB = math.max(0.2f, rawDepthB) * 1.1f;

        // Process A against B
        bool anyEngagedA = UpdateSoldierEngagement(ref offsetsA, posA, posB, widthB, depthB, engagementDistSq);
        
        if (anyEngagedA)
        {
            var engDataA = EngagementDataLookup[entityA];
            engDataA.isEngaged = true;
            engDataA.targetFormation = entityB;
            engDataA.lastEngagementPos = posA.centerPosition;
            engDataA.lastEngagementRot = posA.facing;
            EngagementDataLookup[entityA] = engDataA;
        }

        // Process B against A
        bool anyEngagedB = UpdateSoldierEngagement(ref offsetsB, posB, posA, widthA, depthA, engagementDistSq);

        if (anyEngagedB)
        {
            var engDataB = EngagementDataLookup[entityB];
            engDataB.isEngaged = true;
            engDataB.targetFormation = entityA;
            engDataB.lastEngagementPos = posB.centerPosition;
            engDataB.lastEngagementRot = posB.facing;
            EngagementDataLookup[entityB] = engDataB;
        }
    }

    private bool UpdateSoldierEngagement(
        ref DynamicBuffer<SoldierOffset> offsets,
        FormationPositionData myPos,
        FormationPositionData enemyPos,
        float enemyWidth,
        float enemyDepth,
        float engagementDistSq)
    {
        bool anyEngaged = false;

        // The collider is anchored at the Front Center (Z=0 relative to formation center? No.)
        // In ColliderSystem: Center = new float3(0, .9f, -depth * .5f);
        // This means the collider box is centered at -depth/2.
        // So it extends from Z=0 (Front) to Z=-depth (Rear).
        
        float3 enemyAnchor = enemyPos.centerPosition; 
        quaternion enemyInverseRot = math.inverse(enemyPos.facing);
        
        // Local Bounds relative to enemyAnchor
        float minX = -enemyWidth * 0.5f;
        float maxX = enemyWidth * 0.5f;
        float minZ = -enemyDepth; // Extends backwards
        float maxZ = 0f;          // Ends at anchor

        for (int i = 0; i < offsets.Length; i++)
        {
            var soldier = offsets[i];
            
            if (soldier.isDead) continue;

            float3 soldierWorldPos = myPos.centerPosition + math.rotate(myPos.facing, soldier.currentOffset);
            float3 localPos = math.rotate(enemyInverseRot, soldierWorldPos - enemyAnchor);

            // Calculate distance to the OBB
            float dx = 0f;
            if (localPos.x < minX) dx = minX - localPos.x;
            else if (localPos.x > maxX) dx = localPos.x - maxX;

            float dz = 0f;
            if (localPos.z < minZ) dz = minZ - localPos.z;
            else if (localPos.z > maxZ) dz = localPos.z - maxZ;
            
            float distSq = dx * dx + dz * dz;

            if (distSq < engagementDistSq)
            {
                soldier.isEngaged = true;
                anyEngaged = true;
                offsets[i] = soldier;
            }
            // We DO NOT set isEngaged = false here. 
            // This prevents race conditions if the unit is overlapping multiple triggers.
            // The ClearEngagementSystem handles the reset at the start of the frame.
        }

        return anyEngaged;
    }
}