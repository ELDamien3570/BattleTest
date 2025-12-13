using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Transforms;

[UpdateInGroup(typeof(PhysicsSystemGroup))]
[UpdateBefore(typeof(PhysicsSimulationGroup))]
public partial struct ClearEngagementSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
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
            TransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
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
    [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
    public ComponentLookup<FormationEngagementData> EngagementDataLookup;
    public BufferLookup<SoldierOffset> OffsetLookup;
    public float EngagementDistance;

    public void Execute(TriggerEvent triggerEvent)
    {
        Entity entityA = triggerEvent.EntityA;
        Entity entityB = triggerEvent.EntityB;

        // Check for components
        if (!PositionLookup.HasComponent(entityA) || !PositionLookup.HasComponent(entityB)) return;
        if (!EngagementDataLookup.HasComponent(entityA) || !EngagementDataLookup.HasComponent(entityB)) return;
        if (!TransformLookup.HasComponent(entityA) || !TransformLookup.HasComponent(entityB)) return;

        var posA = PositionLookup[entityA];
        var posB = PositionLookup[entityB];
        
        // Use LocalTransform for Physics-Synchronized Position/Rotation
        var transA = TransformLookup[entityA];
        var transB = TransformLookup[entityB];
        
        var offsetsA = OffsetLookup[entityA];
        var offsetsB = OffsetLookup[entityB];

        float engagementDistSq = EngagementDistance * EngagementDistance;

        // Calculate Dimensions (Same as Collider System)
        float rawWidthA = (posA.columns - 1) * posA.spacing + 2.0f;
        float rawDepthA = (int)math.ceil((float)posA.soldierCount / posA.columns) * posA.spacing + 2.0f;
        float widthA = math.max(0.2f, rawWidthA) * 1.1f;
        float depthA = math.max(0.2f, rawDepthA) * 1.1f;

        float rawWidthB = (posB.columns - 1) * posB.spacing + 2.0f;
        float rawDepthB = (int)math.ceil((float)posB.soldierCount / posB.columns) * posB.spacing + 2.0f;
        float widthB = math.max(0.2f, rawWidthB) * 1.1f;
        float depthB = math.max(0.2f, rawDepthB) * 1.1f;

        // Process A against B
        bool anyEngagedA = UpdateSoldierEngagement(ref offsetsA, transA, transB, widthB, depthB, engagementDistSq);
        
        if (anyEngagedA)
        {
            var engDataA = EngagementDataLookup[entityA];
            engDataA.isEngaged = true;
            engDataA.targetFormation = entityB;
            engDataA.lastEngagementPos = transA.Position;
            engDataA.lastEngagementRot = transA.Rotation;
            EngagementDataLookup[entityA] = engDataA;
        }

        // Process B against A
        bool anyEngagedB = UpdateSoldierEngagement(ref offsetsB, transB, transA, widthA, depthA, engagementDistSq);

        if (anyEngagedB)
        {
            var engDataB = EngagementDataLookup[entityB];
            engDataB.isEngaged = true;
            engDataB.targetFormation = entityA;
            engDataB.lastEngagementPos = transB.Position;
            engDataB.lastEngagementRot = transB.Rotation;
            EngagementDataLookup[entityB] = engDataB;
        }
    }

    private bool UpdateSoldierEngagement(
        ref DynamicBuffer<SoldierOffset> offsets,
        LocalTransform myTrans,
        LocalTransform enemyTrans,
        float enemyWidth,
        float enemyDepth,
        float engagementDistSq)
    {
        bool anyEngaged = false;

        float3 enemyAnchor = enemyTrans.Position; 
        quaternion enemyInverseRot = math.inverse(enemyTrans.Rotation);
        
        // Local Bounds relative to enemyAnchor (Front Center)
        float minX = -enemyWidth * 0.5f;
        float maxX = enemyWidth * 0.5f;
        
        // Expanded Z bounds slightly to account for collider bevel/padding
        // Collider extends from roughly 0 to -depth.
        // We allow a small positive buffer (+0.2f) for frontal engagement.
        float minZ = -enemyDepth; 
        float maxZ = 0.2f;          

        for (int i = 0; i < offsets.Length; i++)
        {
            var soldier = offsets[i];
            
            if (soldier.isDead) continue;

            // Calculate Soldier World Position using Transform (Physics Sync)
            float3 soldierWorldPos = myTrans.Position + math.rotate(myTrans.Rotation, soldier.currentOffset);
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
        }

        return anyEngaged;
    }
}