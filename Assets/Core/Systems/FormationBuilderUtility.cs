using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public static class FormationBuilderUtility
{
    public static void RebuildFormation(ref FormationPositionData group, DynamicBuffer<SoldierOffset> offsets, float spacing = 1f)
    {
        int soldierCount = group.soldierCount;
        int columns = group.columns;
        int rows = (int)math.ceil((float)soldierCount / columns);
        group.rows = rows;

        bool isInitialSetup = offsets.Length == 0;
        
        if (isInitialSetup)
        {
            offsets.Clear();
            float halfWidth = (columns - 1) * 0.5f * spacing;

            for (int i = 0; i < soldierCount; i++)
            {
                int col = i % columns;
                int row = i / columns;

                float3 localOffset = new float3(
                    (col * spacing) - halfWidth,
                    0,
                    -row * spacing
                );

                offsets.Add(new SoldierOffset
                {
                    currentOffset = localOffset,
                    targetOffset = localOffset,
                    facing = quaternion.identity,
                    isMoving = false
                });
            }
            
            // Set initial facing to target facing
            group.targetFacing = math.normalize(group.facing);
        }
        else
        {
            // REMOVED: The logic that overwrote targetFacing. 
            // We now trust PlayerInputSystem to set group.targetFacing correctly.
            
            // Ensure quaternions are valid
            group.facing = math.normalize(group.facing);
            group.targetFacing = math.normalize(group.targetFacing);

            quaternion currentFacing = group.facing;
            
            // Store current world positions using CURRENT facing
            NativeArray<float3> soldierWorldPositions = new NativeArray<float3>(soldierCount, Allocator.Temp);
            for (int i = 0; i < offsets.Length; i++)
            {
                soldierWorldPositions[i] = group.centerPosition + math.rotate(currentFacing, offsets[i].currentOffset);
            }

            // Generate new formation positions in TARGET facing space
            NativeArray<float3> newLocalPositions = new NativeArray<float3>(soldierCount, Allocator.Temp);
            float halfWidth = (columns - 1) * 0.5f * spacing;

            for (int i = 0; i < soldierCount; i++)
            {
                int col = i % columns;
                int row = i / columns;

                newLocalPositions[i] = new float3(
                    (col * spacing) - halfWidth,
                    0,
                    -row * spacing
                );
            }

            // Assign soldiers to closest positions
            AssignSoldiersToClosestPositions(offsets, soldierWorldPositions, newLocalPositions, 
                group.centerPosition, currentFacing, group.targetFacing);
            
            soldierWorldPositions.Dispose();
            newLocalPositions.Dispose();
        }
        
        group.formationBlendProgress = 0f;
    }

    private static void AssignSoldiersToClosestPositions(
        DynamicBuffer<SoldierOffset> soldiers, 
        NativeArray<float3> soldierWorldPositions,
        NativeArray<float3> newLocalPositions,
        float3 centerPosition,
        quaternion currentFacing,
        quaternion targetFacing)
    {
        int count = soldiers.Length;
        
        NativeArray<bool> positionTaken = new NativeArray<bool>(count, Allocator.Temp);
        NativeArray<float3> newTargets = new NativeArray<float3>(count, Allocator.Temp);
        NativeArray<float3> newCurrents = new NativeArray<float3>(count, Allocator.Temp);
        
        for (int i = 0; i < count; i++)
        {
            float3 soldierWorldPos = soldierWorldPositions[i];
            int bestIndex = -1;
            float bestDistance = float.MaxValue;
            
            // Find closest position in TARGET facing space
            for (int j = 0; j < newLocalPositions.Length; j++)
            {
                if (positionTaken[j]) continue;
                
                float3 newWorldPos = centerPosition + math.rotate(targetFacing, newLocalPositions[j]);
                float distance = math.distancesq(soldierWorldPos, newWorldPos);
                
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = j;
                }
            }
            
            if (bestIndex >= 0)
            {
                positionTaken[bestIndex] = true;
                newTargets[i] = newLocalPositions[bestIndex];
                
                // Transform current world position into CURRENT facing space
                // This ensures the soldier stays visually in place relative to the center
                float3 offsetFromCenter = soldierWorldPos - centerPosition;
                newCurrents[i] = math.rotate(math.inverse(currentFacing), offsetFromCenter);
            }
            else
            {
                newTargets[i] = soldiers[i].currentOffset;
                newCurrents[i] = soldiers[i].currentOffset;
            }
        }
        
        for (int i = 0; i < count; i++)
        {
            var soldier = soldiers[i];
            soldier.currentOffset = newCurrents[i];
            soldier.targetOffset = newTargets[i];
            soldier.isMoving = true;
            soldiers[i] = soldier;
        }
        
        positionTaken.Dispose();
        newTargets.Dispose();
        newCurrents.Dispose();
    }

    public static int CalculateSquareGridColumns(int soldierCount)
    {
        return (int)math.ceil(math.sqrt(soldierCount));
    }

    public static int CalculateColumnsFromWidth(float width, float spacing = 1f)
    {
        return math.max(1, (int)(width / spacing));
    }
}
