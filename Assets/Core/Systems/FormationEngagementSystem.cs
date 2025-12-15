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
[UpdateAfter(typeof(ClearEngagementSystem))]
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
            ColliderDataLookup = SystemAPI.GetComponentLookup<FormationColliderData>(true),
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
    [ReadOnly] public ComponentLookup<FormationColliderData> ColliderDataLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
    public ComponentLookup<FormationEngagementData> EngagementDataLookup;
    public BufferLookup<SoldierOffset> OffsetLookup;
    public float EngagementDistance;

    public void Execute(TriggerEvent triggerEvent)
    {
        Entity entityA = triggerEvent.EntityA;
        Entity entityB = triggerEvent.EntityB;

        if (!ColliderDataLookup.HasComponent(entityA) || !ColliderDataLookup.HasComponent(entityB)) return;
        if (!EngagementDataLookup.HasComponent(entityA) || !EngagementDataLookup.HasComponent(entityB)) return;
        if (!TransformLookup.HasComponent(entityA) || !TransformLookup.HasComponent(entityB)) return;
        if (!OffsetLookup.HasBuffer(entityA) || !OffsetLookup.HasBuffer(entityB)) return;

        var colliderA = ColliderDataLookup[entityA];
        var colliderB = ColliderDataLookup[entityB];
        
        var transA = TransformLookup[entityA];
        var transB = TransformLookup[entityB];
        
        var offsetsA = OffsetLookup[entityA];
        var offsetsB = OffsetLookup[entityB];

        float engagementDistSq = EngagementDistance * EngagementDistance;

        bool anyEngagedA = UpdateSoldierEngagement(ref offsetsA, transA, transB, colliderB.width, colliderB.depth, engagementDistSq);
        
        if (anyEngagedA)
        {
            var engDataA = EngagementDataLookup[entityA];
            engDataA.isEngaged = true;
            engDataA.targetFormation = entityB;
            engDataA.lastEngagementPos = transA.Position;
            engDataA.lastEngagementRot = transA.Rotation;
            EngagementDataLookup[entityA] = engDataA;
        }

        bool anyEngagedB = UpdateSoldierEngagement(ref offsetsB, transB, transA, colliderA.width, colliderA.depth, engagementDistSq);

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

    private bool UpdateSoldierEngagement(ref DynamicBuffer<SoldierOffset> offsets, LocalTransform myTrans, LocalTransform enemyTrans, float enemyWidth, float enemyDepth, float engagementDistSq)
    {
        bool anyEngaged = false;

        float3 enemyAnchor = enemyTrans.Position; 
        quaternion enemyInverseRot = math.inverse(enemyTrans.Rotation);

        float boundMinX = -enemyWidth * 0.5f;
        float boundMaxX = enemyWidth * 0.5f;       
        float boundMinZ = -enemyDepth; 
        float boundMaxZ = 0.2f;          

        for (int i = 0; i < offsets.Length; i++)
        {
            var soldier = offsets[i];
            
            if (soldier.isDead) continue;

            float3 soldierWorldPos = myTrans.Position + math.rotate(myTrans.Rotation, soldier.currentOffset);
            float3 soldierEnemyLocalPos = math.rotate(enemyInverseRot, soldierWorldPos - enemyAnchor);

            float distanceToBoxX = 0f;
            if (soldierEnemyLocalPos.x < boundMinX) 
            { 
                distanceToBoxX = boundMinX - soldierEnemyLocalPos.x; 
            }
            else if (soldierEnemyLocalPos.x > boundMaxX) 
            { 
                distanceToBoxX = soldierEnemyLocalPos.x - boundMaxX;
            }

            float distanceToBoxZ = 0f;
            if (soldierEnemyLocalPos.z < boundMinZ)
            {
                distanceToBoxZ = boundMinZ - soldierEnemyLocalPos.z;
            }
            else if (soldierEnemyLocalPos.z > boundMaxZ)
            {
                distanceToBoxZ = soldierEnemyLocalPos.z - boundMaxZ;
            }
            
            float distSq = distanceToBoxX * distanceToBoxX + distanceToBoxZ * distanceToBoxZ;

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