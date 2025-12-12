using Unity.Entities;
using Unity.Mathematics;

public partial struct FormationMovementSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (movementData, positionData, entity) in SystemAPI.Query<RefRW<FormationMovementData>, RefRW<FormationPositionData>>().WithEntityAccess())
        {
            float soldierMoveSpeed = movementData.ValueRW.moveSpeed;
            float movementThreshold = 0.05f;
            float soldierRotationSpeed = movementData.ValueRW.rotationSpeed; //speed for individual soldier turns

            positionData.ValueRW.facing = math.normalize(positionData.ValueRW.facing);
            positionData.ValueRW.targetFacing = math.normalize(positionData.ValueRW.targetFacing);

            //phase 1: rotating
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
                quaternion inverseDelta = math.inverse(deltaRotation); 
                
                for (int i = 0; i < offsets.Length; i++)
                {
                    var soldier = offsets[i];
                    soldier.currentOffset = math.rotate(inverseDelta, soldier.currentOffset);
                    soldier.isMoving = false; 

                    soldier.facing = math.slerp(soldier.facing, positionData.ValueRW.facing, math.min(1f, soldierRotationSpeed * deltaTime));
                    
                    offsets[i] = soldier;
                }

                float angleDiff = QuaternionAngle(positionData.ValueRO.facing, positionData.ValueRO.targetFacing);
                
                if (angleDiff < positionData.ValueRO.facingTolerance) //review later
                {
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
                }
                
                continue;
            }

            //phase 2: reshaping
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
                        float individualSpeed = soldierMoveSpeed * soldier.speedModifier;
                        float step = math.min(distance, individualSpeed * deltaTime);

                        float3 localDir = math.normalize(toTarget);
                        float3 worldDir = math.rotate(positionData.ValueRO.facing, localDir);
                        quaternion targetLook = quaternion.LookRotationSafe(worldDir, math.up());
                        
                        soldier.facing = math.slerp(soldier.facing, targetLook, math.min(1f, soldierRotationSpeed * deltaTime));

                        soldier.currentOffset += localDir * step;
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
                        
                        soldier.facing = math.slerp(soldier.facing, positionData.ValueRO.facing, math.min(1f, soldierRotationSpeed * deltaTime));
                        
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

            //phase 3: retargetting
            if (positionData.ValueRW.formationState == FormationState.retargeting)
            {
                float3 toTarget = positionData.ValueRW.targetPosition - positionData.ValueRW.centerPosition;
                float distance = math.length(toTarget);
                
                if (distance > 0.1f)
                {
                    float3 targetDir = toTarget / distance;
                    float step = math.min(distance, movementData.ValueRW.moveSpeed * deltaTime);
                    positionData.ValueRW.centerPosition += targetDir * step;
                    
                    quaternion moveFacing = quaternion.LookRotationSafe(targetDir, math.up());

                    var offsets = state.EntityManager.GetBuffer<SoldierOffset>(entity);
                    for (int i = 0; i < offsets.Length; i++)
                    {
                        var soldier = offsets[i];
                        soldier.isMoving = true;
                        
                        soldier.facing = math.slerp(soldier.facing, moveFacing, math.min(1f, soldierRotationSpeed * deltaTime));
                        
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

            //phase 4: idle
            if (positionData.ValueRW.formationState == FormationState.idle)
            {
                var offsets = state.EntityManager.GetBuffer<SoldierOffset>(entity);
                for (int i = 0; i < offsets.Length; i++)
                {
                    var soldier = offsets[i];
                    
                    soldier.facing = math.slerp(soldier.facing, positionData.ValueRW.facing, math.min(1f, soldierRotationSpeed * deltaTime));
                    
                    if (soldier.isMoving)
                    {
                        soldier.isMoving = false;
                    }
                    offsets[i] = soldier;
                }
            }

            if (SystemAPI.HasComponent<FormationEngagementData>(entity))
            {
                var engData = SystemAPI.GetComponent<FormationEngagementData>(entity);
                engData.needsEngagementUpdate = true;
                SystemAPI.SetComponent(entity, engData);
            }
        }


    }
    
    private static float QuaternionAngle(quaternion a, quaternion b)
    {
        float dot = math.abs(math.dot(a, b));
        return math.acos(math.min(dot, 1.0f)) * 2.0f;
    }
}