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
        Matrix4x4[] matrices = new Matrix4x4[batchSize];

        foreach (var (renderData, positionData, offsets) in SystemAPI.Query<RefRW<FormationRenderData>, RefRW<FormationPositionData>, DynamicBuffer<SoldierOffset>>())
        {
            var dirOff = offsets;

            string key = renderData.ValueRO.soldierType.ToString();

            if (!playerCache.TryGetValue(key, out var data))
            {
                AnimatedMeshScriptableObject so = Resources.Load<AnimatedMeshScriptableObject>($"meshes/{key}");
                Material mat = Resources.Load<Material>($"materials/{key}");

                if (so == null || mat == null)
                {
                    UnityEngine.Debug.LogWarning($"Soldier type {key} not found!");
                    continue;
                }

                data = (so, mat);
                playerCache[key] = data;
            }

            var playerMeshes = data.so;
            var soldierMaterial = data.mat;

            int currentAnimationIndex = 0; 
            var animation = playerMeshes.Animations[currentAnimationIndex];
            int frameCount = animation.Meshes.Count;

            renderData.ValueRW.animationTimer += deltaTime;
            float secondsPerFrame = 1f / playerMeshes.AnimationFPS;
            while (renderData.ValueRW.animationTimer >= secondsPerFrame)
            {
                renderData.ValueRW.animationTimer -= secondsPerFrame;
                renderData.ValueRW.animationFrame = (renderData.ValueRW.animationFrame + 1) % frameCount;
            }

            Mesh mesh = animation.Meshes[renderData.ValueRW.animationFrame];

            int index = 0;
            for (int i = 0; i < offsets.Length; i++)
            {
                float3 soldierWorldPos = positionData.ValueRW.centerPosition + math.rotate(positionData.ValueRW.facing, offsets[i].Value);

                matrices[index] = Matrix4x4.TRS(soldierWorldPos, positionData.ValueRW.facing, Vector3.one);
                index++;

                if (index == batchSize || i == offsets.Length - 1)
                {
                    Graphics.DrawMeshInstanced(mesh, 0, soldierMaterial, matrices, index);

                    index = 0;
                }
            }
        }
    }

}
