using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;

public partial struct FormationMovementSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (movementData, positionData, entity) in SystemAPI.Query<RefRW<FormationMovementData>, RefRW<FormationPositionData>>().WithEntityAccess())
        {
            float soldierMoveSpeed = 5f;
            float movementThreshold = 0.05f;

            positionData.ValueRW.facing = math.normalize(positionData.ValueRW.facing);
            positionData.ValueRW.targetFacing = math.normalize(positionData.ValueRW.targetFacing);

            // PHASE 1: Rotating
            if (positionData.ValueRW.formationState == FormationState.rotating)
            {
                quaternion oldFacing = positionData.ValueRW.facing;
                
                positionData.ValueRW.facing = math.normalize(math.slerp(
                    positionData.ValueRW.facing,
                    positionData.ValueRW.targetFacing,
                    math.min(1f, movementData.ValueRW.rotationSpeed * deltaTime)
                ));

                var offsets = state.EntityManager.GetBuffer<SoldierOffset>(entity);
                quaternion deltaRotation = math.mul(positionData.ValueRW.facing, math.inverse(oldFacing));
                quaternion inverseDelta = math.inverse(deltaRotation); // Defined here
                
                for (int i = 0; i < offsets.Length; i++)
                {
                    var soldier = offsets[i];
                    // Rotate currentOffset backwards to counteract formation rotation
                    soldier.currentOffset = math.rotate(inverseDelta, soldier.currentOffset);
                    soldier.isMoving = true;
                    offsets[i] = soldier;
                }

                float angleDiff = QuaternionAngle(positionData.ValueRO.facing, positionData.ValueRO.targetFacing);
                
                if (angleDiff < 0.05f)
                {
                    // Final snap logic to prevent jitter
                    quaternion snapDelta = math.mul(positionData.ValueRW.targetFacing, math.inverse(positionData.ValueRW.facing));
                    quaternion inverseSnap = math.inverse(snapDelta);
                    
                    for (int i = 0; i < offsets.Length; i++)
                    {
                        var soldier = offsets[i];
                        soldier.currentOffset = math.rotate(inverseSnap, soldier.currentOffset);
                        offsets[i] = soldier;
                    }

                    positionData.ValueRW.facing = positionData.ValueRW.targetFacing;
                    positionData.ValueRW.formationState = FormationState.reshaping;
                    UnityEngine.Debug.Log("Rotation complete, starting reshape");
                }
                
                continue;
            }

            // PHASE 2: Reshaping
            if (positionData.ValueRW.formationState == FormationState.reshaping)
            {
                var offsets = state.EntityManager.GetBuffer<SoldierOffset>(entity);
                
                bool allInPosition = true;
                for (int i = 0; i < offsets.Length; i++)
                {
                    SoldierOffset soldier = offsets[i];
                    float3 toTarget = soldier.targetOffset - soldier.currentOffset;
                    float distance = math.length(toTarget);

                    if (distance > movementThreshold)
                    {
                        float step = math.min(distance, soldierMoveSpeed * deltaTime);
                        soldier.currentOffset += math.normalize(toTarget) * step;
                        soldier.isMoving = true;
                        allInPosition = false;
                        offsets[i] = soldier;
                    }
                    else
                    {
                        if (distance > 0.001f)
                        {
                            soldier.currentOffset = soldier.targetOffset;
                        }
                        soldier.isMoving = false;
                        offsets[i] = soldier;
                    }
                }

                if (allInPosition)
                {
                    float distanceToTarget = math.distance(positionData.ValueRO.centerPosition, positionData.ValueRO.targetPosition);
                    
                    if (distanceToTarget > 0.5f)
                    {
                        positionData.ValueRW.formationState = FormationState.retargeting;
                    }
                    else
                    {
                        positionData.ValueRW.formationState = FormationState.idle;
                    }
                }
                
                continue;
            }

            // PHASE 3: Retargeting (Moving Center)
            if (positionData.ValueRW.formationState == FormationState.retargeting)
            {
                float3 toTarget = positionData.ValueRW.targetPosition - positionData.ValueRW.centerPosition;
                float distance = math.length(toTarget);
                
                if (distance > 0.1f)
                {
                    float3 targetDir = toTarget / distance;
                    float step = math.min(distance, movementData.ValueRW.moveSpeed * deltaTime);
                    positionData.ValueRW.centerPosition += targetDir * step;
                    
                    var offsets = state.EntityManager.GetBuffer<SoldierOffset>(entity);
                    for (int i = 0; i < offsets.Length; i++)
                    {
                        var soldier = offsets[i];
                        soldier.isMoving = true;
                        offsets[i] = soldier;
                    }
                }
                else
                {
                    positionData.ValueRW.centerPosition = positionData.ValueRW.targetPosition;
                    positionData.ValueRW.formationState = FormationState.idle;
                    
                    var offsets = state.EntityManager.GetBuffer<SoldierOffset>(entity);
                    for (int i = 0; i < offsets.Length; i++)
                    {
                        var soldier = offsets[i];
                        soldier.isMoving = false;
                        offsets[i] = soldier;
                    }
                }
                continue;
            }

            // PHASE 4: Idle
            if (positionData.ValueRW.formationState == FormationState.idle)
            {
                var offsets = state.EntityManager.GetBuffer<SoldierOffset>(entity);
                for (int i = 0; i < offsets.Length; i++)
                {
                    var soldier = offsets[i];
                    if (soldier.isMoving)
                    {
                        soldier.isMoving = false;
                        offsets[i] = soldier;
                    }
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