using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct FormationColliderPositioningSystem : ISystem
{ 

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        foreach (var (posData, colliderData, transform, entity) in SystemAPI.Query<RefRO<FormationPositionData>, RefRW<FormationColliderData>, RefRW<LocalTransform>>().WithAll<PhysicsCollider>().WithEntityAccess())
        {
            transform.ValueRW.Position = posData.ValueRO.centerPosition;
            transform.ValueRW.Rotation = posData.ValueRO.facing;

            bool columnsChanged = colliderData.ValueRO.lastColumns != posData.ValueRO.columns;
            bool countChanged = colliderData.ValueRO.lastSoldierCount != posData.ValueRO.soldierCount;
            bool manualRebuild = colliderData.ValueRO.needsRebuilt;

            if (columnsChanged || countChanged || manualRebuild)
            {
                float spacing = colliderData.ValueRO.spacing;
                int columns = posData.ValueRO.columns;
                int soldierCount = posData.ValueRO.soldierCount;
                int rows = (int)math.ceil((float)soldierCount / columns);

                // Ensure dimensions are large enough for the BevelRadius (0.05f requires min size > 0.1f)
                float width = math.max(0.2f, (columns - 1) * spacing + 2.0f); 
                float depth = math.max(0.2f, (rows - 1) * spacing + 2.0f);
                float3 localCenter = new float3(0, .9f, -depth * .5f);


                var newCollider = BoxCollider.Create(new BoxGeometry
                {
                    Center = localCenter,
                    Size = new float3(width, 2f, depth*1.1f),
                    Orientation = quaternion.identity,
                    BevelRadius = 0.05f
                },
                    colliderData.ValueRO.filter, 
                    colliderData.ValueRO.material
                );

                ecb.SetComponent(entity, new PhysicsCollider { Value = newCollider });

                colliderData.ValueRW.lastColumns = columns;
                colliderData.ValueRW.lastSoldierCount = soldierCount;
                colliderData.ValueRW.needsRebuilt = false;

            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }

}