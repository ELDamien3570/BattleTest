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
                // Rebuild formation (calculates new offsets based on current and target facing)
                FormationBuilderUtility.RebuildFormation(ref group.ValueRW, offsets, spacing: 1f);

                // Check if we need to rotate first
                float angleDiff = QuaternionAngle(group.ValueRO.facing, group.ValueRO.targetFacing);
                
                if (angleDiff > 0.1f)
                {
                    group.ValueRW.formationState = FormationState.rotating;
                }
                else
                {
                    group.ValueRW.formationState = FormationState.reshaping;
                }
            }
        }
    }
    
    private static float QuaternionAngle(quaternion a, quaternion b)
    {
        float dot = math.abs(math.dot(a, b));
        return math.acos(math.min(dot, 1.0f)) * 2.0f;
    }
}
