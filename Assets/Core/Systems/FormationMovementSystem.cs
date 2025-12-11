using System.Diagnostics;
using System.Text.RegularExpressions;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

public partial struct FormationMovementSystem : ISystem
{

    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (movementData, positionData, offsets) in SystemAPI.Query<RefRW<FormationMovementData>, RefRW<FormationPositionData>, DynamicBuffer<SoldierOffset>>())
        {
            if (positionData.ValueRW.formationState == FormationState.retargeting)
            {
                var troopBuffer = offsets;
                float3 toTarget = positionData.ValueRW.targetPosition - positionData.ValueRW.centerPosition;
                float distance = math.length(toTarget);
                if (distance < 0.01f) continue;

                float3 targetDir = toTarget / distance;

                quaternion targetRotation = quaternion.LookRotationSafe(targetDir, math.up());

                positionData.ValueRW.facing = math.slerp(
                    positionData.ValueRW.facing,
                    targetRotation,
                    math.min(1f, movementData.ValueRW.rotationSpeed * deltaTime)
                );

                // Check alignment before moving
                float angleDiff = math.acos(math.clamp(
                    math.dot(math.forward(positionData.ValueRW.facing), targetDir),
                    -1f, 1f
                ));
                if (angleDiff > positionData.ValueRW.facingTolerance)
                {
                    // Not aligned yet, stay in place
                    continue;
                }

                // Move along current facing
                positionData.ValueRW.centerPosition += targetDir * movementData.ValueRW.moveSpeed * deltaTime;
            }
        }
    }
}

