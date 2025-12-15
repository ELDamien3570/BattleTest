using System.Collections.Generic;
using NUnit.Framework.Constraints;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
public partial struct FormationRenderSystem : ISystem
{
    private static Dictionary<FixedString64Bytes, (AnimatedMeshScriptableObject so, Material mat)> renderCache;

    private const int BatchSize = 1023;
    private const int TimingGroups = 4;
    private const int MaxDeathFrames = 60; 

    private static Matrix4x4[][] soldierTransformBuckets;
    private static int[] livingCount;

    private static Matrix4x4[][] deadSoldierTransformBuckets;
    private static int[] deadCount;

   
    private static readonly float[] animTimingOffsets = { 0f, 0.11f, 0.35f, 0.6f };
    private static Terrain activeTerrain;

    public void OnCreate(ref SystemState state)
    {
        if (renderCache == null)
        {
            renderCache = new Dictionary<FixedString64Bytes, (AnimatedMeshScriptableObject, Material)>();

            //idle0-3, walk0-3, combat0-3
            soldierTransformBuckets = new Matrix4x4[3 * TimingGroups][];
            livingCount = new int[3 * TimingGroups];

            for (int i = 0; i < soldierTransformBuckets.Length; i++)
            {
                soldierTransformBuckets[i] = new Matrix4x4[BatchSize];
            }

            deadSoldierTransformBuckets = new Matrix4x4[MaxDeathFrames][];
            deadCount = new int[MaxDeathFrames];

            for (int i = 0; i < MaxDeathFrames; i++)
            {
                deadSoldierTransformBuckets[i] = new Matrix4x4[BatchSize];
            }

            activeTerrain = Terrain.activeTerrain;

        }
    }

    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (renderData, positionData, offsets) in SystemAPI.Query<RefRW<FormationRenderData>, RefRW<FormationPositionData>, DynamicBuffer<SoldierOffset>>())
        {
            if (math.any(math.isnan(positionData.ValueRO.facing.value)))
            {
                positionData.ValueRW.facing = quaternion.identity;
            }

            if (offsets.Length == 0) continue;

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

            //pre-calculate frame indices for each timing group
            int[] idleFrames = new int[TimingGroups];
            int[] walkFrames = new int[TimingGroups];
            int[] combatFrames = new int[TimingGroups];
            for (int g = 0; g < TimingGroups; g++)
            {
                float groupTime = baseTime + animTimingOffsets[g];
                idleFrames[g] = (int)(groupTime / secondsPerFrame) % idleAnim.Meshes.Count;
                walkFrames[g] = (int)(groupTime / secondsPerFrame) % walkAnim.Meshes.Count;
                combatFrames[g] = (int)(groupTime / secondsPerFrame) % combatAnim.Meshes.Count;
            }

            //reset counts
            for (int i = 0; i < livingCount.Length; i++) livingCount[i] = 0;
            for (int i = 0; i < deathFrameCount; i++) deadCount[i] = 0;

            //sort soldiers into buckets
            for (int i = 0; i < offsets.Length; i++)
            {
                float3 soldierWorldPos = positionData.ValueRO.centerPosition + math.rotate(positionData.ValueRO.facing, offsets[i].currentOffset);

                if (activeTerrain == null) 
                {
                    activeTerrain = Terrain.activeTerrain;
                }


                if (activeTerrain != null)
                {
                    soldierWorldPos.y = activeTerrain.SampleHeight(soldierWorldPos);
                }

                Matrix4x4 matrix = Matrix4x4.TRS(soldierWorldPos, offsets[i].facing, Vector3.one);

                if (offsets[i].isDead)
                {
                    int deathFrame = (int)(offsets[i].deathTimer / secondsPerFrame);
                    deathFrame = math.min(deathFrame, deathFrameCount - 1);

                    deadSoldierTransformBuckets[deathFrame][deadCount[deathFrame]++] = matrix;

                    if (deadCount[deathFrame] == BatchSize)
                    {
                        Graphics.DrawMeshInstanced(deathAnim.Meshes[deathFrame], 0, soldierMaterial, deadSoldierTransformBuckets[deathFrame], deadCount[deathFrame]);
                        deadCount[deathFrame] = 0;
                    }
                    continue;
                }

                //assigning timing groups
                int group = offsets[i].animationGroup;
                int bucketIndex;

                if (offsets[i].isEngaged)
                    bucketIndex = 8 + group;  //combat buckets: 8, 9, 10, 11
                else if (offsets[i].isMoving)
                    bucketIndex = 4 + group;  //walk buckets: 4, 5, 6, 7
                else
                    bucketIndex = group;      //idle buckets: 0, 1, 2, 3

                soldierTransformBuckets[bucketIndex][livingCount[bucketIndex]++] = matrix;
            }

            //flush buckets
            for (int b = 0; b < 3 * TimingGroups; b++)
            {
                if (livingCount[b] > 0)
                {
                    Mesh mesh = GetMeshForBucket(b, idleAnim, walkAnim, combatAnim, idleFrames, walkFrames, combatFrames);
                    Graphics.DrawMeshInstanced(mesh, 0, soldierMaterial, soldierTransformBuckets[b], livingCount[b]);
                }
            }
            for (int f = 0; f < deathFrameCount; f++)
            {
                if (deadCount[f] > 0)
                {
                    Graphics.DrawMeshInstanced(deathAnim.Meshes[f], 0, soldierMaterial, deadSoldierTransformBuckets[f], deadCount[f]);
                }
            }
        }
    }

    private Mesh GetMeshForBucket(int bucketRow, AnimatedMeshScriptableObject.Animation idle, AnimatedMeshScriptableObject.Animation walk, AnimatedMeshScriptableObject.Animation combat, int[] idleFrames, int[] walkFrames, int[] combatFrames)
    {
        int animType = bucketRow / TimingGroups;  
        int timingGroup = bucketRow % TimingGroups; 

        return animType switch
        {
            0 => idle.Meshes[idleFrames[timingGroup]],
            1 => walk.Meshes[walkFrames[timingGroup]],
            2 => combat.Meshes[combatFrames[timingGroup]],
            _ => idle.Meshes[0]
        };
    }
}