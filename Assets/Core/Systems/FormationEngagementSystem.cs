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
        float3 lineStart = enemyFrontCenter - enemyRight * enemyHalfWidth;
        float3 lineEnd = enemyFrontCenter + enemyRight * enemyHalfWidth;

        for (int i = 0; i < offsets.Length; i++)
        {
            var soldier = offsets[i];
            float3 worldPos = pos.centerPosition + math.rotate(pos.facing, soldier.currentOffset);

            float distToLine = DistanceToLineSegmentSq(worldPos, lineStart, lineEnd);

            if (distToLine < engagementDistSq)
            {
                soldier.isEngaged = true;
                offsets[i] = soldier;
                anyEngaged = true;
            }
        }

        return anyEngaged;
    }

    private float DistanceToLineSegmentSq(float3 point, float3 lineStart, float3 lineEnd)
    {
        float3 line = lineEnd - lineStart;
        float3 toPoint = point - lineStart;

        float lineLengthSq = math.lengthsq(line);
        if (lineLengthSq < 0.0001f)
            return math.lengthsq(toPoint);

        float t = math.saturate(math.dot(toPoint, line) / lineLengthSq);
        float3 closestPoint = lineStart + t * line;

        float3 diff = point - closestPoint;
        return diff.x * diff.x + diff.z * diff.z;
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