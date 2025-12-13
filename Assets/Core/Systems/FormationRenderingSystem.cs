using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(FormationCombatSystem))]
[UpdateAfter(typeof(DeadSoldierRemovalSystem))]
public partial struct FormationRenderSystem : ISystem
{
    private static Dictionary<FixedString64Bytes, (AnimatedMeshScriptableObject so, Material mat)> renderCache;

    private const int BatchSize = 1023;
    private const int TimingGroups = 4;
    private const int MaxDeathFrames = 60; // Adjust to your longest death animation

    // 4 timing groups × 3 animation states (idle, walk, combat) = 12 buckets
    private static Matrix4x4[][] matrices;
    private static int[] counts;

    // Separate death buckets - one per frame
    private static Matrix4x4[][] deathMatrices;
    private static int[] deathCounts;

    // Timing offsets (in seconds) for each group
    private static readonly float[] timingOffsets = { 0f, 0.25f, 0.5f, 0.75f };

    public void OnCreate(ref SystemState state)
    {
        if (renderCache == null)
        {
            renderCache = new Dictionary<FixedString64Bytes, (AnimatedMeshScriptableObject, Material)>();

            // 12 buckets for living soldiers: [idle0-3, walk0-3, combat0-3]
            matrices = new Matrix4x4[3 * TimingGroups][];
            counts = new int[3 * TimingGroups];

            for (int i = 0; i < matrices.Length; i++)
            {
                matrices[i] = new Matrix4x4[BatchSize];
            }

            // Death buckets - one per possible frame
            deathMatrices = new Matrix4x4[MaxDeathFrames][];
            deathCounts = new int[MaxDeathFrames];

            for (int i = 0; i < MaxDeathFrames; i++)
            {
                deathMatrices[i] = new Matrix4x4[BatchSize];
            }
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

            // Cache lookup
            FixedString64Bytes cacheKey = default;
            cacheKey.Append(renderData.ValueRO.soldierType);
            cacheKey.Append('_');
            cacheKey.Append(renderData.ValueRO.renderMaterial);

            if (!renderCache.TryGetValue(cacheKey, out var data))
            {
                string meshName = renderData.ValueRO.soldierType.ToString();
                string matName = renderData.ValueRO.renderMaterial.ToString();

                AnimatedMeshScriptableObject so = Resources.Load<AnimatedMeshScriptableObject>($"meshes/{meshName}");
                Material mat = Resources.Load<Material>($"materials/{matName}");

                if (so == null || mat == null) continue;

                data = (so, mat);
                renderCache[cacheKey] = data;
            }

            var playerMeshes = data.so;
            var soldierMaterial = data.mat;

            renderData.ValueRW.animationTimer += deltaTime;
            float baseTime = renderData.ValueRO.animationTimer;
            float secondsPerFrame = 1f / playerMeshes.AnimationFPS;

            var idleAnim = playerMeshes.Animations[0];
            var walkAnim = playerMeshes.Animations[1];
            var combatAnim = playerMeshes.Animations[2];
            var deathAnim = playerMeshes.Animations[3];

            int deathFrameCount = deathAnim.Meshes.Count;

            // Pre-calculate frame indices for each timing group (living animations only)
            int[] idleFrames = new int[TimingGroups];
            int[] walkFrames = new int[TimingGroups];
            int[] combatFrames = new int[TimingGroups];

            for (int g = 0; g < TimingGroups; g++)
            {
                float groupTime = baseTime + timingOffsets[g];
                idleFrames[g] = (int)(groupTime / secondsPerFrame) % idleAnim.Meshes.Count;
                walkFrames[g] = (int)(groupTime / secondsPerFrame) % walkAnim.Meshes.Count;
                combatFrames[g] = (int)(groupTime / secondsPerFrame) % combatAnim.Meshes.Count;
            }

            // Reset counts
            for (int i = 0; i < counts.Length; i++) counts[i] = 0;
            for (int i = 0; i < deathFrameCount; i++) deathCounts[i] = 0;

            // Sort soldiers into buckets
            for (int i = 0; i < offsets.Length; i++)
            {
                float3 soldierWorldPos = positionData.ValueRO.centerPosition +
                    math.rotate(positionData.ValueRO.facing, offsets[i].currentOffset);
                Matrix4x4 matrix = Matrix4x4.TRS(soldierWorldPos, offsets[i].facing, Vector3.one);

                // Handle dead soldiers separately with individual timing
                if (offsets[i].isDead)
                {
                    // Calculate frame from soldier's personal death timer, clamped to last frame
                    int deathFrame = (int)(offsets[i].deathTimer / secondsPerFrame);
                    deathFrame = math.min(deathFrame, deathFrameCount - 1);

                    deathMatrices[deathFrame][deathCounts[deathFrame]++] = matrix;

                    // Flush if batch is full
                    if (deathCounts[deathFrame] == BatchSize)
                    {
                        Graphics.DrawMeshInstanced(deathAnim.Meshes[deathFrame], 0, soldierMaterial,
                                                   deathMatrices[deathFrame], deathCounts[deathFrame]);
                        deathCounts[deathFrame] = 0;
                    }
                    continue;
                }

                // Living soldiers use timing groups
                int group = offsets[i].animationGroup;
                int bucketIndex;

                if (offsets[i].isEngaged)
                    bucketIndex = 8 + group;  // combat buckets: 8, 9, 10, 11
                else if (offsets[i].isMoving)
                    bucketIndex = 4 + group;  // walk buckets: 4, 5, 6, 7
                else
                    bucketIndex = group;      // idle buckets: 0, 1, 2, 3

                matrices[bucketIndex][counts[bucketIndex]++] = matrix;

                // Flush if batch is full
                if (counts[bucketIndex] == BatchSize)
                {
                    Mesh mesh = GetMeshForBucket(bucketIndex, idleAnim, walkAnim, combatAnim,
                                                  idleFrames, walkFrames, combatFrames);
                    Graphics.DrawMeshInstanced(mesh, 0, soldierMaterial, matrices[bucketIndex], counts[bucketIndex]);
                    counts[bucketIndex] = 0;
                }
            }

            // Flush remaining living soldiers
            for (int b = 0; b < 3 * TimingGroups; b++)
            {
                if (counts[b] > 0)
                {
                    Mesh mesh = GetMeshForBucket(b, idleAnim, walkAnim, combatAnim,
                                                  idleFrames, walkFrames, combatFrames);
                    Graphics.DrawMeshInstanced(mesh, 0, soldierMaterial, matrices[b], counts[b]);
                }
            }

            // Flush remaining dead soldiers
            for (int f = 0; f < deathFrameCount; f++)
            {
                if (deathCounts[f] > 0)
                {
                    Graphics.DrawMeshInstanced(deathAnim.Meshes[f], 0, soldierMaterial,
                                               deathMatrices[f], deathCounts[f]);
                }
            }
        }
    }

    private Mesh GetMeshForBucket(int bucket,
        AnimatedMeshScriptableObject.Animation idle,
        AnimatedMeshScriptableObject.Animation walk,
        AnimatedMeshScriptableObject.Animation combat,
        int[] idleFrames, int[] walkFrames, int[] combatFrames)
    {
        int animType = bucket / TimingGroups;  // 0=idle, 1=walk, 2=combat
        int group = bucket % TimingGroups;

        return animType switch
        {
            0 => idle.Meshes[idleFrames[group]],
            1 => walk.Meshes[walkFrames[group]],
            2 => combat.Meshes[combatFrames[group]],
            _ => idle.Meshes[0]
        };
    }
}