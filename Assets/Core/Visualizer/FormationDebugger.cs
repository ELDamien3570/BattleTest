using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public partial struct FormationColliderDebugSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (!Debug.isDebugBuild && !Application.isEditor) return;

        foreach (var (posData, colliderData) in
            SystemAPI.Query<RefRO<FormationPositionData>, RefRO<FormationColliderData>>())
        {
            float spacing = colliderData.ValueRO.spacing;
            int columns = posData.ValueRO.columns;
            int soldierCount = posData.ValueRO.soldierCount;
            int rows = (int)math.ceil((float)soldierCount / columns);

            float width = (columns - 1) * spacing + 1f;
            float depth = (rows - 1) * spacing + 1f;
            float height = 2f;

            
            quaternion rot = posData.ValueRO.facing;
            float3 trueCenter = posData.ValueRO.centerPosition + math.rotate(rot, new float3(0, 0, -depth * 0.5f));

            DrawBox(trueCenter, rot, new float3(width, height, depth), Color.green);
        }
    }

    private void DrawBox(float3 center, quaternion rot, float3 size, Color color)
    {
        float3 halfSize = size * 0.5f;

        // 8 corners in local space
        float3[] localCorners = new float3[]
        {
            new float3(-halfSize.x, -halfSize.y, -halfSize.z),
            new float3( halfSize.x, -halfSize.y, -halfSize.z),
            new float3( halfSize.x, -halfSize.y,  halfSize.z),
            new float3(-halfSize.x, -halfSize.y,  halfSize.z),
            new float3(-halfSize.x,  halfSize.y, -halfSize.z),
            new float3( halfSize.x,  halfSize.y, -halfSize.z),
            new float3( halfSize.x,  halfSize.y,  halfSize.z),
            new float3(-halfSize.x,  halfSize.y,  halfSize.z),
        };

        // Transform to world space
        Vector3[] worldCorners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            worldCorners[i] = center + math.rotate(rot, localCorners[i]);
        }

        // Draw 12 edges
        // Bottom face
        Debug.DrawLine(worldCorners[0], worldCorners[1], color);
        Debug.DrawLine(worldCorners[1], worldCorners[2], color);
        Debug.DrawLine(worldCorners[2], worldCorners[3], color);
        Debug.DrawLine(worldCorners[3], worldCorners[0], color);

        // Top face
        Debug.DrawLine(worldCorners[4], worldCorners[5], color);
        Debug.DrawLine(worldCorners[5], worldCorners[6], color);
        Debug.DrawLine(worldCorners[6], worldCorners[7], color);
        Debug.DrawLine(worldCorners[7], worldCorners[4], color);

        // Vertical edges
        Debug.DrawLine(worldCorners[0], worldCorners[4], color);
        Debug.DrawLine(worldCorners[1], worldCorners[5], color);
        Debug.DrawLine(worldCorners[2], worldCorners[6], color);
        Debug.DrawLine(worldCorners[3], worldCorners[7], color);
    }
}