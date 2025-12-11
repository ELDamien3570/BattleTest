using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public partial struct FormationRenderSystem : ISystem
{
    private static Dictionary<string, (AnimatedMeshScriptableObject so, Material mat)> playerCache;
    private static Dictionary<string, (AnimatedMeshScriptableObject so, Material mat)> aiCache;

    public void OnCreate(ref SystemState state)
    {
        if (playerCache == null)
            playerCache = new Dictionary<string, (AnimatedMeshScriptableObject, Material)>();
        if (aiCache == null)
            aiCache = new Dictionary<string, (AnimatedMeshScriptableObject, Material)>();
    }

    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        int batchSize = 1023;
        
        Matrix4x4[] idleMatrices = new Matrix4x4[batchSize];
        Matrix4x4[] movingMatrices = new Matrix4x4[batchSize];

        foreach (var (renderData, positionData, offsets) in SystemAPI.Query<RefRW<FormationRenderData>, RefRW<FormationPositionData>, DynamicBuffer<SoldierOffset>>())
        {
            if (math.any(math.isnan(positionData.ValueRO.facing.value)))
            {
                positionData.ValueRW.facing = quaternion.identity; 
            }
            
            if (offsets.Length == 0) continue;

            string key = renderData.ValueRO.soldierType.ToString();

            if (!playerCache.TryGetValue(key, out var data))
            {
                AnimatedMeshScriptableObject so = Resources.Load<AnimatedMeshScriptableObject>($"meshes/{key}");
                Material mat = Resources.Load<Material>($"materials/{key}");

                if (so == null || mat == null)
                {
                    continue;
                }

                data = (so, mat);
                playerCache[key] = data;
            }

            var playerMeshes = data.so;
            var soldierMaterial = data.mat;

            renderData.ValueRW.animationTimer += deltaTime;
            float secondsPerFrame = 1f / playerMeshes.AnimationFPS;
            
            var idleAnim = playerMeshes.Animations[0];
            var walkAnim = playerMeshes.Animations[1];
            
            int idleFrameCount = idleAnim.Meshes.Count;
            int walkFrameCount = walkAnim.Meshes.Count;
            
            int idleFrameIndex = (int)(renderData.ValueRW.animationTimer / secondsPerFrame) % idleFrameCount;
            int walkFrameIndex = (int)(renderData.ValueRW.animationTimer / secondsPerFrame) % walkFrameCount;
            
            Mesh idleMesh = idleAnim.Meshes[idleFrameIndex];
            Mesh walkMesh = walkAnim.Meshes[walkFrameIndex];

            int idleCount = 0;
            int movingCount = 0;

            for (int i = 0; i < offsets.Length; i++)
            {
                float3 soldierWorldPos = positionData.ValueRW.centerPosition + math.rotate(positionData.ValueRW.facing, offsets[i].currentOffset);
                
                Matrix4x4 matrix = Matrix4x4.TRS(soldierWorldPos, offsets[i].facing, Vector3.one);

                if (offsets[i].isMoving)
                {
                    movingMatrices[movingCount] = matrix;
                    movingCount++;
                    
                    if (movingCount == batchSize)
                    {
                        Graphics.DrawMeshInstanced(walkMesh, 0, soldierMaterial, movingMatrices, movingCount);
                        movingCount = 0;
                    }
                }
                else
                {
                    idleMatrices[idleCount] = matrix;
                    idleCount++;
                    
                    if (idleCount == batchSize)
                    {
                        Graphics.DrawMeshInstanced(idleMesh, 0, soldierMaterial, idleMatrices, idleCount);
                        idleCount = 0;
                    }
                }
            }

            if (movingCount > 0)
            {
                Graphics.DrawMeshInstanced(walkMesh, 0, soldierMaterial, movingMatrices, movingCount);
            }
            
            if (idleCount > 0)
            {
                Graphics.DrawMeshInstanced(idleMesh, 0, soldierMaterial, idleMatrices, idleCount);
            }
        }
    }
}
