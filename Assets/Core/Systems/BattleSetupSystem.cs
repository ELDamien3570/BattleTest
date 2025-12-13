using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Physics;
using Unity.Transforms;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct BattleSetupSystem : ISystem
{
    private NativeList<BlobAssetReference<Collider>> _allocatedColliders;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BattleSetupData>();
        _allocatedColliders = new NativeList<BlobAssetReference<Collider>>(Allocator.Persistent);
    }

    public void OnDestroy(ref SystemState state)
    {
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
        var setupData = SystemAPI.GetSingleton<BattleSetupData>();
        var setupEntity = SystemAPI.GetSingletonEntity<BattleSetupData>();

        var soldierTypes = new NativeArray<FixedString32Bytes>(0, Allocator.Temp);

        if (state.EntityManager.HasBuffer<SoldierTypeBuffer>(setupEntity))
        {
            var buffer = state.EntityManager.GetBuffer<SoldierTypeBuffer>(setupEntity);
            soldierTypes = new NativeArray<FixedString32Bytes>(buffer.Length, Allocator.Temp);
            for (int i = 0; i < buffer.Length; i++)
            {
                soldierTypes[i] = buffer[i].soldierType;
            }
        }

        var colliderCache = new NativeHashMap<FixedString32Bytes, BlobAssetReference<Collider>>(8, Allocator.Temp);

        SpawnArmy(ref state, setupData, soldierTypes, ref colliderCache, isPlayer: true);
        SpawnArmy(ref state, setupData, soldierTypes, ref colliderCache, isPlayer: false);

        colliderCache.Dispose();
        soldierTypes.Dispose();
        state.Enabled = false;
    }

    private void SpawnArmy(
        ref SystemState state,
        BattleSetupData setupData,
        NativeArray<FixedString32Bytes> soldierTypes,
        ref NativeHashMap<FixedString32Bytes, BlobAssetReference<Collider>> colliderCache,
        bool isPlayer)
    {
        var eManager = state.EntityManager;

        int formationCount = setupData.formationPreSide;
        int soldiersPerFormation = setupData.soldiersPerSide;
        int columns = FormationBuilderUtility.CalculateSquareGridColumns(soldiersPerFormation) + 10;

        float formationGap = 4f;
        float3 startLocation = isPlayer ? setupData.playerStartLocation : setupData.aiStartLocation;
        FixedString32Bytes materialName = isPlayer ? setupData.playerMaterial : setupData.aiMaterial;
        CollisionFilter filter = isPlayer ? FormationPhysics.PlayerFilter : FormationPhysics.AIFilter;

        var formationData = new NativeArray<TroopTypeData>(formationCount, Allocator.Temp);
        float totalArmyWidth = 0f;

        for (int i = 0; i < formationCount; i++)
        {
            FixedString32Bytes soldierType = GetSoldierType(soldierTypes, i);
            formationData[i] = GetTroopTypeData(soldierType, soldiersPerFormation, columns);
            totalArmyWidth += formationData[i].formationWidth;
        }
        totalArmyWidth += formationGap * (formationCount - 1);

        float currentX = -totalArmyWidth * 0.5f;

        for (int i = 0; i < formationCount; i++)
        {
            var data = formationData[i];

            float3 spawnPosition = startLocation;
            spawnPosition.x += currentX + (data.formationWidth * 0.5f);
            currentX += data.formationWidth + formationGap;

            Entity formation = CreateFormationEntity(ref state, isPlayer);

            float3 colliderCenter = new float3(0, 0.9f, -data.formationDepth * 0.5f);
            FixedString32Bytes cacheKey = data.soldierType;
            if (!isPlayer) cacheKey.Append("_AI");

            if (!colliderCache.TryGetValue(cacheKey, out var colliderBlob))
            {
                colliderBlob = BoxCollider.Create(new BoxGeometry
                {
                    Center = colliderCenter,
                    Size = new float3(data.formationWidth, 2f, data.formationDepth),
                    Orientation = quaternion.identity,
                    BevelRadius = 0.05f
                },
                filter,
                FormationPhysics.TriggerMaterial);

                colliderCache.Add(cacheKey, colliderBlob);
                _allocatedColliders.Add(colliderBlob);
            }

            eManager.SetComponentData(formation, LocalTransform.FromPositionRotation(spawnPosition, quaternion.identity));
            eManager.SetSharedComponentManaged(formation, new PhysicsWorldIndex(0));

            eManager.SetComponentData(formation, new PhysicsVelocity
            {
                Linear = float3.zero,
                Angular = float3.zero
            });

            eManager.SetComponentData(formation, new PhysicsCollider { Value = colliderBlob });

            eManager.SetComponentData(formation, new FormationColliderData
            {
                lastColumns = columns,
                lastSoldierCount = soldiersPerFormation,
                spacing = data.spacing,
                needsRebuilt = true,
                filter = filter,
                material = FormationPhysics.TriggerMaterial,
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
                facingTolerance = 0.1f,
                spacing = data.spacing,
                columns = columns,
                soldierCount = soldiersPerFormation,
                formationState = FormationState.rebuilding,
                desiredAttackDepth = data.attackDepth
            });

            eManager.SetComponentData(formation, new FormationMovementData
            {
                moveSpeed = data.moveSpeed,
                rotationSpeed = math.radians(90f)
            });

            eManager.SetComponentData(formation, new FormationRenderData
            {
                currentAnimation = 0,
                animationFrame = 0,
                animationTimer = 0,
                soldierType = data.soldierType,
                renderMaterial = materialName
            });

            eManager.SetComponentData(formation, new FormationCombatData
            {
                meleeDamage = data.damage,
                meleeDefense = data.defense,
                troopType = data.troopType,
                formationHealth = data.formationHealth,
                healthPerTroop = data.healthPerTroop,
            });

            eManager.AddBuffer<SoldierOffset>(formation);
        }

        formationData.Dispose();
    }

    private Entity CreateFormationEntity(ref SystemState state, bool isPlayer)
    {
        var eManager = state.EntityManager;

        Entity formation = eManager.CreateEntity(
            typeof(FormationPositionData),
            typeof(FormationMovementData),
            typeof(FormationRenderData),
            typeof(FormationCombatData),
            typeof(LocalTransform),
            typeof(PhysicsCollider),
            typeof(PhysicsVelocity),
            typeof(PhysicsWorldIndex),
            typeof(FormationColliderData),
            typeof(FormationEngagementData)
        );

        if (isPlayer)
            eManager.AddComponent<PlayerControlled>(formation);
        else
            eManager.AddComponent<AiControlled>(formation);

        return formation;
    }

    private static FixedString32Bytes GetSoldierType(NativeArray<FixedString32Bytes> soldierTypes, int index)
    {
        if (soldierTypes.Length == 0)
            return "Infantry";
        return soldierTypes[index % soldierTypes.Length];
    }

    private static TroopTypeData GetTroopTypeData(FixedString32Bytes soldierType, int soldierCount, int columns)
    {
        var data = new TroopTypeData
        {
            soldierType = soldierType,
            troopType = 0,
            spacing = 1f,
            moveSpeed = 5f,
            healthPerTroop = 100,
            damage = 5,
            defense = 5,
            attackDepth = 0f
        };

        if (soldierType.Contains(new FixedString32Bytes("Infantry")))
        {
            data.troopType = 0;
            data.spacing = 1.5f;
            data.moveSpeed = 7f;
            data.healthPerTroop = 400;
            data.damage = 8;
            data.defense = 5;
            data.attackDepth = 0.1f;
        }
        else if (soldierType.Contains(new FixedString32Bytes("Spearmen")))
        {
            data.troopType = 1;
            data.spacing = 1.25f;
            data.moveSpeed = 5f;
            data.healthPerTroop = 350;
            data.damage = 6;
            data.defense = 10;
            data.attackDepth = 0.1f;
        }
        else if (soldierType.Contains(new FixedString32Bytes("HeavyCavalry")))
        {
            data.troopType = 2;
            data.spacing = 3f;
            data.moveSpeed = 13f;
            data.healthPerTroop = 1000;
            data.damage = 13;
            data.defense = 8;
            data.attackDepth = 0.4f;
        }
        else if (soldierType.Contains(new FixedString32Bytes("LightCavalry")))
        {
            data.troopType = 3;
            data.spacing = 3f;
            data.moveSpeed = 15f;
            data.healthPerTroop = 800;
            data.damage = 10;
            data.defense = 4;
            data.attackDepth = 0.4f;
        }

        int rows = (int)math.ceil((float)soldierCount / columns);
        data.formationWidth = (columns - 1) * data.spacing;
        data.formationDepth = (rows - 1) * data.spacing;
        data.formationHealth = data.healthPerTroop * soldierCount;

        return data;
    }

    private struct TroopTypeData
    {
        public FixedString32Bytes soldierType;
        public int troopType;
        public float spacing;
        public float moveSpeed;
        public int healthPerTroop;
        public int damage;
        public int defense;
        public float attackDepth;

        // Derived
        public float formationWidth;
        public float formationDepth;
        public int formationHealth;
    }
}

public static class FormationPhysics
{
    public const uint PlayerFormation = 1u << 8;
    public const uint AIFormation = 1u << 9;

    public static CollisionFilter PlayerFilter => new CollisionFilter
    {
        BelongsTo = PlayerFormation,
        CollidesWith = AIFormation,
        GroupIndex = 0
    };

    public static CollisionFilter AIFilter => new CollisionFilter
    {
        BelongsTo = AIFormation,
        CollidesWith = PlayerFormation,
        GroupIndex = 0
    };

    public static Unity.Physics.Material TriggerMaterial => new Unity.Physics.Material
    {
        CollisionResponse = CollisionResponsePolicy.RaiseTriggerEvents
    };
}