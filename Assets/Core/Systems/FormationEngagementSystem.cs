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
            EngagementDistance = 3f,
            MovementThreshold = 0.1f,
            RotationThreshold = 0.01f
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
    public float MovementThreshold;
    public float RotationThreshold;

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
        var engDataA = EngagementDataLookup[entityA];
        var engDataB = EngagementDataLookup[entityB];

        // Check if either formation has moved significantly
        bool aMoved = HasMoved(posA, engDataA);
        bool bMoved = HasMoved(posB, engDataB);

        // Skip if neither formation moved and both were already processed
        if (!aMoved && !bMoved && !engDataA.needsEngagementUpdate && !engDataB.needsEngagementUpdate)
            return;

        var offsetsA = OffsetLookup[entityA];
        var offsetsB = OffsetLookup[entityB];

        float engagementDistSq = EngagementDistance * EngagementDistance;

        // Get front line info
        float3 rightA = math.mul(posA.facing, new float3(1, 0, 0));
        float halfWidthA = (posA.columns - 1) * posA.spacing * 0.5f;

        float3 rightB = math.mul(posB.facing, new float3(1, 0, 0));
        float halfWidthB = (posB.columns - 1) * posB.spacing * 0.5f;

        // Process engagements
        bool anyEngagedA = FlagSoldiersNearEnemyFront(ref offsetsA, posA, posB.centerPosition, rightB, halfWidthB, engagementDistSq);
        bool anyEngagedB = FlagSoldiersNearEnemyFront(ref offsetsB, posB, posA.centerPosition, rightA, halfWidthA, engagementDistSq);

        // Update engagement data
        engDataA.isEngaged = anyEngagedA;
        engDataA.needsEngagementUpdate = false;
        engDataA.lastEngagementPos = posA.centerPosition;
        engDataA.lastEngagementRot = posA.facing;
        EngagementDataLookup[entityA] = engDataA;

        engDataB.isEngaged = anyEngagedB;
        engDataB.needsEngagementUpdate = false;
        engDataB.lastEngagementPos = posB.centerPosition;
        engDataB.lastEngagementRot = posB.facing;
        EngagementDataLookup[entityB] = engDataB;
    }

    private bool HasMoved(FormationPositionData pos, FormationEngagementData engData)
    {
        float posDiff = math.distancesq(pos.centerPosition, engData.lastEngagementPos);
        float rotDiff = 1f - math.abs(math.dot(pos.facing, engData.lastEngagementRot));

        return posDiff > MovementThreshold * MovementThreshold || rotDiff > RotationThreshold;
    }

    private bool FlagSoldiersNearEnemyFront(
        ref DynamicBuffer<SoldierOffset> offsets,
        FormationPositionData pos,
        float3 enemyFrontCenter,
        float3 enemyRight,
        float enemyHalfWidth,
        float engagementDistSq)
    {
        bool anyEngaged = false;

        // --- OPTIMIZATION START ---
        
        // 1. Calculate World Space Line
        float3 lineStartWorld = enemyFrontCenter - enemyRight * enemyHalfWidth;
        float3 lineEndWorld = enemyFrontCenter + enemyRight * enemyHalfWidth;

        // 2. Transform Enemy Line into THIS Formation's Local Space
        // This allows us to compare against soldier.currentOffset directly, avoiding 
        // a matrix rotation for every single soldier in the loop.
        quaternion inverseRot = math.inverse(pos.facing);
        float3 localLineStart = math.rotate(inverseRot, lineStartWorld - pos.centerPosition);
        float3 localLineEnd = math.rotate(inverseRot, lineEndWorld - pos.centerPosition);

        // 3. Pre-calculate Line Segment Math
        float3 lineVec = localLineEnd - localLineStart;
        float lineLenSq = math.lengthsq(lineVec);
        float invLineLenSq = (lineLenSq > 0.0001f) ? 1.0f / lineLenSq : 0.0f;

        // 4. Calculate AABB Bounds for fast culling
        // We only check soldiers that are within the bounding box of the enemy line + engagement distance.
        // This effectively skips all soldiers in the back rows.
        float engagementDist = math.sqrt(engagementDistSq);
        
        float minZ = math.min(localLineStart.z, localLineEnd.z) - engagementDist;
        float maxZ = math.max(localLineStart.z, localLineEnd.z) + engagementDist;
        float minX = math.min(localLineStart.x, localLineEnd.x) - engagementDist;
        float maxX = math.max(localLineStart.x, localLineEnd.x) + engagementDist;

        for (int i = 0; i < offsets.Length; i++)
        {
            var soldier = offsets[i];
            float3 p = soldier.currentOffset;

            // Fast AABB Cull
            if (p.z < minZ || p.z > maxZ || p.x < minX || p.x > maxX)
                continue;

            // Inlined DistanceToLineSegmentSq (Local Space)
            float3 toPoint = p - localLineStart;
            float t = math.dot(toPoint, lineVec) * invLineLenSq;
            t = math.clamp(t, 0f, 1f);
            
            float3 closestPoint = localLineStart + t * lineVec;
            float3 diff = p - closestPoint;
            
            // Only considering X and Z distance as per original logic
            float distSq = diff.x * diff.x + diff.z * diff.z;

            if (distSq < engagementDistSq)
            {
                soldier.isEngaged = true;
                offsets[i] = soldier;
                anyEngaged = true;
            }
        }
        // --- OPTIMIZATION END ---

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
        // Only clear formations that are flagged as engaged AND need update
        new ClearEngagementsJob().ScheduleParallel();
    }
}

[BurstCompile]
partial struct ClearEngagementsJob : IJobEntity
{
    public void Execute(ref DynamicBuffer<SoldierOffset> offsets, ref FormationEngagementData engagementData)
    {
        // Skip if formation hasn't moved and doesn't need update
        if (!engagementData.needsEngagementUpdate && !engagementData.isEngaged)
            return;

        // Only clear if we were engaged (avoid unnecessary writes)
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