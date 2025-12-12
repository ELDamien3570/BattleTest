using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public partial struct FormationRenderSystem : ISystem
{
    private static Dictionary<FixedString64Bytes, (AnimatedMeshScriptableObject so, Material mat)> renderCache;

    // Persistent arrays - allocated once, reused every frame
    private static Matrix4x4[] idleMatrices;
    private static Matrix4x4[] movingMatrices;
    private static Matrix4x4[] combatMatrices;
    private const int BatchSize = 1023;

    public void OnCreate(ref SystemState state)
    {
        if (renderCache == null)
        {
            renderCache = new Dictionary<FixedString64Bytes, (AnimatedMeshScriptableObject, Material)>();
            idleMatrices = new Matrix4x4[BatchSize];
            movingMatrices = new Matrix4x4[BatchSize];
            combatMatrices = new Matrix4x4[BatchSize];
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (renderData, positionData, offsets) in
            SystemAPI.Query<RefRW<FormationRenderData>, RefRW<FormationPositionData>, DynamicBuffer<SoldierOffset>>())
        {
            if (math.any(math.isnan(positionData.ValueRO.facing.value)))
            {
                positionData.ValueRW.facing = quaternion.identity;
            }

            if (offsets.Length == 0) continue;

            // Build cache key without string allocations
            FixedString64Bytes cacheKey = default;
            cacheKey.Append(renderData.ValueRO.soldierType);
            cacheKey.Append('_');
            cacheKey.Append(renderData.ValueRO.renderMaterial);

            if (!renderCache.TryGetValue(cacheKey, out var data))
            {
                // Only allocate strings on cache miss (rare - only happens once per type)
                string meshName = renderData.ValueRO.soldierType.ToString();
                string matName = renderData.ValueRO.renderMaterial.ToString();

                AnimatedMeshScriptableObject so = Resources.Load<AnimatedMeshScriptableObject>($"meshes/{meshName}");
                Material mat = Resources.Load<Material>($"materials/{matName}");

                if (so == null || mat == null)
                {
                    continue;
                }

                data = (so, mat);
                renderCache[cacheKey] = data;
            }

            var playerMeshes = data.so;
            var soldierMaterial = data.mat;

            renderData.ValueRW.animationTimer += deltaTime;
            float secondsPerFrame = 1f / playerMeshes.AnimationFPS;

            var idleAnim = playerMeshes.Animations[0];
            var walkAnim = playerMeshes.Animations[1];
            var combatAnim = playerMeshes.Animations[2];

            int idleFrameIndex = (int)(renderData.ValueRW.animationTimer / secondsPerFrame) % idleAnim.Meshes.Count;
            int walkFrameIndex = (int)(renderData.ValueRW.animationTimer / secondsPerFrame) % walkAnim.Meshes.Count;
            int combatFrameIndex = (int)(renderData.ValueRW.animationTimer / secondsPerFrame) % combatAnim.Meshes.Count;

            Mesh idleMesh = idleAnim.Meshes[idleFrameIndex];
            Mesh walkMesh = walkAnim.Meshes[walkFrameIndex];
            Mesh combatMesh = combatAnim.Meshes[combatFrameIndex];

            int idleCount = 0;
            int movingCount = 0;
            int combatCount = 0;

            for (int i = 0; i < offsets.Length; i++)
            {
                float3 soldierWorldPos = positionData.ValueRO.centerPosition +
                    math.rotate(positionData.ValueRO.facing, offsets[i].currentOffset);
                Matrix4x4 matrix = Matrix4x4.TRS(soldierWorldPos, offsets[i].facing, Vector3.one);

                if (offsets[i].isEngaged)
                {
                    combatMatrices[combatCount++] = matrix;
                    if (combatCount == BatchSize)
                    {
                        Graphics.DrawMeshInstanced(combatMesh, 0, soldierMaterial, combatMatrices, combatCount);
                        combatCount = 0;
                    }
                }
                else if (offsets[i].isMoving)
                {
                    movingMatrices[movingCount++] = matrix;
                    if (movingCount == BatchSize)
                    {
                        Graphics.DrawMeshInstanced(walkMesh, 0, soldierMaterial, movingMatrices, movingCount);
                        movingCount = 0;
                    }
                }
                else
                {
                    idleMatrices[idleCount++] = matrix;
                    if (idleCount == BatchSize)
                    {
                        Graphics.DrawMeshInstanced(idleMesh, 0, soldierMaterial, idleMatrices, idleCount);
                        idleCount = 0;
                    }
                }
            }

            if (combatCount > 0)
                Graphics.DrawMeshInstanced(combatMesh, 0, soldierMaterial, combatMatrices, combatCount);
            if (movingCount > 0)
                Graphics.DrawMeshInstanced(walkMesh, 0, soldierMaterial, movingMatrices, movingCount);
            if (idleCount > 0)
                Graphics.DrawMeshInstanced(idleMesh, 0, soldierMaterial, idleMatrices, idleCount);
        }
    }
}