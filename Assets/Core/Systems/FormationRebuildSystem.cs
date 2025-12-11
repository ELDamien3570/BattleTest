using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(FormationMovementSystem))]
public partial struct FormationRebuildSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (group, offsets) in SystemAPI.Query<RefRW<FormationPositionData>, DynamicBuffer<SoldierOffset>>())
        {
            if (group.ValueRO.formationState == FormationState.rebuilding)
            {
                // Rebuild using utility
                FormationBuilderUtility.RebuildFormation(ref group.ValueRW, offsets, spacing: 1f);

                UnityEngine.Debug.Log($"Formation rebuilt: {group.ValueRO.columns}x{group.ValueRO.rows} = {group.ValueRO.soldierCount} soldiers");

                // Check if we should move to target after rebuilding
                float distanceToTarget = math.distance(group.ValueRO.centerPosition, group.ValueRO.targetPosition);

                if (distanceToTarget > 0.5f) // Has a target to move to
                {
                    group.ValueRW.formationState = FormationState.retargeting;
                    UnityEngine.Debug.Log($"Formation rebuilt, now moving to target at {group.ValueRO.targetPosition}");
                }
                else // Already at target
                {
                    group.ValueRW.formationState = FormationState.idle;
                    UnityEngine.Debug.Log("Formation rebuilt at spawn location, now idle");
                }
            }
        }
    }
}






