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
                FormationBuilderUtility.RebuildFormation(ref group.ValueRW, offsets, group.ValueRW.spacing);

                float angleDiff = AngleComparator(group.ValueRO.facing, group.ValueRO.targetFacing);
                
                if (angleDiff > group.ValueRO.facingTolerance) 
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
    
    private static float AngleComparator(quaternion a, quaternion b)
    {
        float dot = math.abs(math.dot(a, b));
        return math.acos(math.min(dot, 1.0f)) * 2.0f;
    }
}
