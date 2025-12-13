using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Physics;

using Unity.Transforms;
using Unity.Rendering;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct BattleSetupSystem : ISystem
{
    // Track allocated blobs to dispose of them when the system is destroyed
    private NativeList<BlobAssetReference<Collider>> _allocatedColliders;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BattleSetupData>();
        _allocatedColliders = new NativeList<BlobAssetReference<Collider>>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
        // Dispose of all physics colliders created by this system
        if (_allocatedColliders.IsCreated)
        {
            foreach (var collider in _allocatedColliders)
            {
                if (collider.IsCreated)
                    collider.Dispose();
            }
            _allocatedColliders.Dispose();
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        var battleSetupData = SystemAPI.GetSingleton<BattleSetupData>();
        var battleSetupEntity = SystemAPI.GetSingletonEntity<BattleSetupData>();

        // Initialize with length 0 to avoid "default" uninitialized struct issues
        NativeArray<FixedString32Bytes> soldierTypesCopy = new NativeArray<FixedString32Bytes>(0, Allocator.Temp);

        if (state.EntityManager.HasBuffer<SoldierTypeBuffer>(battleSetupEntity))
        {
            var buffer = state.EntityManager.GetBuffer<SoldierTypeBuffer>(battleSetupEntity);
            // Resize and copy
            soldierTypesCopy = new NativeArray<FixedString32Bytes>(buffer.Length, Allocator.Temp);
            for (int i = 0; i < buffer.Length; i++)
            {
                soldierTypesCopy[i] = buffer[i].soldierType;
            }
        }

        // Use a cache to avoid creating duplicate colliders for the same unit type
        // Key: Soldier Type Name, Value: Collider Blob
        var colliderCache = new NativeHashMap<FixedString32Bytes, BlobAssetReference<Collider>>(4, Allocator.Temp);

        SpawnPlayerArmy(ref state, battleSetupData, battleSetupEntity, soldierTypesCopy, ref colliderCache);
        SpawnAIArmy(ref state, battleSetupData, battleSetupEntity, soldierTypesCopy, ref colliderCache);
        
        colliderCache.Dispose();
        state.Enabled = false;
    }

    public void SpawnPlayerArmy(ref SystemState state, BattleSetupData setupData, Entity setupEntity, NativeArray<FixedString32Bytes> soldierType, ref NativeHashMap<FixedString32Bytes, BlobAssetReference<Collider>> colliderCache)
    {
        var eManager = state.EntityManager;

        int formationCount = setupData.formationPreSide;
        float formationGap = 2f;
        float soldierSpacing = 2f;

        int soldiersPerFormation = setupData.soldiersPerSide;
        int columns = FormationBuilderUtility.CalculateSquareGridColumns(soldiersPerFormation) + 10;

        float singleFormationWidth = (columns - 1) * soldierSpacing + 1f;
        float totalArmyWidth = (singleFormationWidth * formationCount) + (formationGap * (formationCount - 1));
        float startXOffset = -totalArmyWidth * 0.5f;
        float currentXOffset = startXOffset + (singleFormationWidth * 0.5f);

        for (int i = 0; i < formationCount; i++)
        {
            Entity formation = eManager.CreateEntity(
                typeof(FormationPositionData),
                typeof(FormationMovementData),
                typeof(FormationRenderData),
                typeof(FormationCombatData),
                typeof(PlayerControlled),
                typeof(LocalTransform),
                typeof(PhysicsCollider),
                typeof(PhysicsVelocity),
                typeof(PhysicsWorldIndex),
                typeof(FormationColliderData),
                typeof(FormationEngagementData)
            );

            float3 spawnPosition = setupData.playerStartLocation;
            spawnPosition.x += currentXOffset;

            FixedString32Bytes currentSoldierType = "player";
            if (soldierType.Length > 0)
            {
                int typeIndex = i % soldierType.Length;
                currentSoldierType = soldierType[typeIndex];
            }

            #region Determine troop type info from formation troop type


            int detTroopType = 0;
            float detSpacing = 1;
            float detMoveSpeed = 5;
            int detHealthPerTroop = 100;

            if (currentSoldierType.Contains(new FixedString32Bytes("Infantry")))
            {
                detTroopType = 0;
                detSpacing = 2f;
                detMoveSpeed = 7;
                detHealthPerTroop = 150;
            }
            else if (currentSoldierType.Contains(new FixedString32Bytes("Archer")))
            {
                detTroopType = 1;
                detSpacing = 1.5f;
                detMoveSpeed = 6;
                detHealthPerTroop = 100;
            }
            else if (currentSoldierType.Contains(new FixedString32Bytes("Cavalry")))
            {
                detTroopType = 2;
                detSpacing = 3.5f;
                detMoveSpeed = 13;
                detHealthPerTroop = 300;
            }
            else if (currentSoldierType.Contains(new FixedString32Bytes("CavArcher")))
            {
                detTroopType = 3;
                detSpacing = 3f;
                detMoveSpeed = 15;
                detHealthPerTroop = 225;
            }

            float estRows = (setupData.soldiersPerSide / columns) + 1;
            float singleFormationDepth = estRows * detSpacing - detSpacing;
            float exactSingleFormWidth = (columns - 1) * detSpacing;

            float3 colliderCenter = new float3(0, .9f, -singleFormationDepth / 2);

            int detFormationHealth = detHealthPerTroop * soldiersPerFormation;

            #endregion

            eManager.SetComponentData(formation, new PhysicsVelocity
            {
                Linear = float3.zero,
                Angular = float3.zero
            });

            var filter = FormationPhysics.PlayerFilter;
            var material = FormationPhysics.TriggerMaterial;
            uint newWorldIndex = 0;

            eManager.SetComponentData(formation, LocalTransform.FromPositionRotation(spawnPosition, quaternion.identity));
            eManager.SetSharedComponentManaged(formation, new PhysicsWorldIndex(newWorldIndex));

            eManager.SetComponentData(formation, new FormationColliderData
            {
                lastColumns = columns,
                lastSoldierCount = soldiersPerFormation,
                spacing = detSpacing,
                needsRebuilt = true,
                filter = filter,
                material = material,
            });

            // Check cache for existing collider blob
            if (!colliderCache.TryGetValue(currentSoldierType, out BlobAssetReference<Collider> colliderBlob))
            {
                // Create new blob if not found
                colliderBlob = Unity.Physics.BoxCollider.Create(new BoxGeometry
                {
                    Center = colliderCenter,
                    Size = new float3(exactSingleFormWidth, 2f, singleFormationDepth),
                    Orientation = (quaternion.identity),
                    BevelRadius = 0.05f
                },
                filter,
                material
                );

                // Cache it and track for disposal
                colliderCache.Add(currentSoldierType, colliderBlob);
                _allocatedColliders.Add(colliderBlob);
            }

            eManager.SetComponentData(formation, new PhysicsCollider
            {
                Value = colliderBlob
            });

            eManager.SetComponentData(formation, new FormationEngagementData
            {
                isEngaged = false,
                needsEngagementUpdate = true,
                lastEngagementPos = float3.zero,
                lastEngagementRot = quaternion.identity
            });


            eManager.SetComponentData(formation, new FormationPositionData
            {
                centerPosition = spawnPosition,
                targetPosition = spawnPosition,
                facing = (quaternion.identity),
                targetFacing = (quaternion.identity),
                facingTolerance = .1f,
                spacing = detSpacing,
                columns = columns,
                soldierCount = soldiersPerFormation,
                formationState = FormationState.rebuilding
            });

            eManager.SetComponentData(formation, new FormationMovementData
            {
                moveSpeed = detMoveSpeed,
                rotationSpeed = math.radians(180f) / 2f
            });

            eManager.SetComponentData(formation, new FormationRenderData
            {
                currentAnimation = 0,
                animationFrame = 0,
                animationTimer = 0,
                soldierType = currentSoldierType,
                renderMaterial = setupData.playerMaterial
            });

            eManager.SetComponentData(formation, new FormationCombatData
            {
                meleeDamage = 5,
                meleeDefense = 5,
                troopType = detTroopType,
                formationHealth= detFormationHealth,
                lastDeathHealth = detFormationHealth,
                healthPerTroop = detHealthPerTroop,
            });

            eManager.AddBuffer<SoldierOffset>(formation);

            currentXOffset += singleFormationWidth + formationGap;
        }
    }


    public void SpawnAIArmy(ref SystemState state, BattleSetupData setupData, Entity setupEntity, NativeArray<FixedString32Bytes> soldierType, ref NativeHashMap<FixedString32Bytes, BlobAssetReference<Collider>> colliderCache)
    {
        var eManager = state.EntityManager;

        int formationCount = setupData.formationPreSide;
        float formationGap = 2f;
        float soldierSpacing = 2f;

        int soldiersPerFormation = setupData.soldiersPerSide;
        int columns = FormationBuilderUtility.CalculateSquareGridColumns(soldiersPerFormation) + 10;

        float singleFormationWidth = (columns - 1) * soldierSpacing + 1f;
        float totalArmyWidth = (singleFormationWidth * formationCount) + (formationGap * (formationCount - 1));
        float startXOffset = -totalArmyWidth * 0.5f;
        float currentXOffset = startXOffset + (singleFormationWidth * 0.5f);

        for (int i = 0; i < formationCount; i++)
        {
            Entity formation = eManager.CreateEntity(
                typeof(FormationPositionData),
                typeof(FormationMovementData),
                typeof(FormationRenderData),
                typeof(FormationCombatData),
                typeof(AiControlled),
                typeof(LocalTransform),
                typeof(PhysicsCollider),
                typeof(PhysicsVelocity),
                typeof(PhysicsWorldIndex),
                typeof(FormationColliderData),
                typeof(FormationEngagementData)
            );

            float3 spawnPosition = setupData.aiStartLocation;
            spawnPosition.x += currentXOffset;

            FixedString32Bytes currentSoldierType = "player";
            if (soldierType.Length > 0)
            {
                int typeIndex = i % soldierType.Length;
                currentSoldierType = soldierType[typeIndex];
            }

            #region Determine troop type info from formation troop type

            int detTroopType = 0;
            float detSpacing = 1;
            float detMoveSpeed = 5;
            int detHealthPerTroop = 100;

            if (currentSoldierType.Contains(new FixedString32Bytes("Infantry")))
            {
                detTroopType = 0;
                detSpacing = 2f;
                detMoveSpeed = 7;
                detHealthPerTroop = 150;
            }
            else if (currentSoldierType.Contains(new FixedString32Bytes("Archer")))
            {
                detTroopType = 1;
                detSpacing = 1.5f;
                detMoveSpeed = 6;
                detHealthPerTroop = 100;
            }
            else if (currentSoldierType.Contains(new FixedString32Bytes("Cavalry")))
            {
                detTroopType = 2;
                detSpacing = 3.5f;
                detMoveSpeed = 13;
                detHealthPerTroop = 300;
            }
            else if (currentSoldierType.Contains(new FixedString32Bytes("CavArcher")))
            {
                detTroopType = 3;
                detSpacing = 3f;
                detMoveSpeed = 15;
                detHealthPerTroop = 225;
            }

            float estRows = (setupData.soldiersPerSide / columns) + 1;
            float singleFormationDepth = estRows * detSpacing - detSpacing;     
            float exactSingleFormWidth = (columns - 1) * detSpacing;

            float3 colliderCenter = new float3(0, .9f, -singleFormationDepth / 2);

            int detFormationHealth = detHealthPerTroop * soldiersPerFormation;

            #endregion

            var filter = FormationPhysics.AIFilter;
            var material = FormationPhysics.TriggerMaterial;
            uint newWorldIndex = 0;

            eManager.SetComponentData(formation, LocalTransform.FromPositionRotation(spawnPosition, quaternion.identity));
            eManager.SetSharedComponentManaged(formation, new PhysicsWorldIndex(newWorldIndex));


            eManager.SetComponentData(formation, new PhysicsVelocity
            {
                Linear = float3.zero,
                Angular = float3.zero
            });

            eManager.SetComponentData(formation, new FormationColliderData
            {
                lastColumns = columns,
                lastSoldierCount = soldiersPerFormation,
                spacing = detSpacing,
                needsRebuilt = true,
                filter = filter,
                material = material
            });

            
            FixedString32Bytes cacheKey = currentSoldierType;
            cacheKey.Append(new FixedString32Bytes("_AI")); 

            if (!colliderCache.TryGetValue(cacheKey, out BlobAssetReference<Unity.Physics.Collider> colliderBlob))
            {
                colliderBlob = Unity.Physics.BoxCollider.Create(new BoxGeometry
                {
                    Center = colliderCenter,
                    Size = new float3(exactSingleFormWidth, 2f, singleFormationDepth),
                    Orientation = quaternion.identity,
                    BevelRadius = 0.05f
                },
                filter, // AI Filter
                material
                );

                colliderCache.Add(cacheKey, colliderBlob);
                _allocatedColliders.Add(colliderBlob);
            }

            eManager.SetComponentData(formation, new PhysicsCollider
            {
                Value = colliderBlob,
            });

            eManager.SetComponentData(formation, new FormationEngagementData
            {
                isEngaged = false,
                needsEngagementUpdate = true,
                lastEngagementPos = float3.zero,
                lastEngagementRot = quaternion.identity
            });

            eManager.SetComponentData(formation, new FormationPositionData
            {
                centerPosition = spawnPosition,
                targetPosition = spawnPosition,
                facing = quaternion.identity,
                targetFacing = quaternion.identity,
                facingTolerance = .1f,
                spacing = detSpacing,
                columns = columns,
                soldierCount = soldiersPerFormation,
                formationState = FormationState.rebuilding
            });

            eManager.SetComponentData(formation, new FormationMovementData
            {
                moveSpeed = detMoveSpeed,
                rotationSpeed = math.radians(180f) / 2f
            });

            eManager.SetComponentData(formation, new FormationRenderData
            {
                currentAnimation = 0,
                animationFrame = 0,
                animationTimer = 0,
                soldierType = currentSoldierType,
                renderMaterial = setupData.aiMaterial
            });

            eManager.SetComponentData(formation, new FormationCombatData
            {
                meleeDamage = 5,
                meleeDefense = 5,
                healthPerTroop = detHealthPerTroop,
                formationHealth = detFormationHealth,
                lastDeathHealth = detFormationHealth,
                troopType = detTroopType
            });

            eManager.AddBuffer<SoldierOffset>(formation);

            currentXOffset += singleFormationWidth + formationGap;
        }
    }
}

public static class FormationPhysics
{
    // Collision categories
    public const uint PlayerFormation = 1u << 8;
    public const uint AIFormation = 1u << 9;

    public static CollisionFilter PlayerFilter => new CollisionFilter
    {
        BelongsTo = PlayerFormation,
        CollidesWith = AIFormation,  // Only collide with enemy
        GroupIndex = 0
    };

    public static CollisionFilter AIFilter => new CollisionFilter
    {
        BelongsTo = AIFormation,
        CollidesWith = PlayerFormation,  // Only collide with enemy
        GroupIndex = 0
    };

    // Material that raises trigger events (no physical collision response)
    public static Unity.Physics.Material TriggerMaterial => new Unity.Physics.Material
    {
        CollisionResponse = CollisionResponsePolicy.RaiseTriggerEvents
    };
}