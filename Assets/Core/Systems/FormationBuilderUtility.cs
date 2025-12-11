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

        offsets.Clear();

        float halfWidth = (columns - 1) * 0.5f * spacing;
        // No halfDepth - we build backwards from front line

        for (int i = 0; i < soldierCount; i++)
        {
            int col = i % columns;
            int row = i / columns;

            float3 localOffset = new float3(
                (col * spacing) - halfWidth,  // X: left to right, centered
                0,
                -row * spacing                // Z: backwards from front (negative = behind center)
            );

            offsets.Add(new SoldierOffset
            {
                Value = localOffset,
                facing = quaternion.identity
            });
        }
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
