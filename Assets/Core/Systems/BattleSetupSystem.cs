using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Physics;
using UnityEditor.Search;
using Unity.Transforms;


[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct BattleSetupSystem : ISystem
{

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BattleSetupData>();     
    }

    public void OnUpdate(ref SystemState state)
    {
        
        var battleSetupData = SystemAPI.GetSingleton<BattleSetupData>();
        var battleSetupEntity = SystemAPI.GetSingletonEntity<BattleSetupData>(); // Get the entity to access buffer


        NativeArray<FixedString32Bytes> soldierTypesCopy = default;

        if (state.EntityManager.HasBuffer<SoldierTypeBuffer>(battleSetupEntity))
        {
            var buffer = state.EntityManager.GetBuffer<SoldierTypeBuffer>(battleSetupEntity);
            soldierTypesCopy = new NativeArray<FixedString32Bytes>(buffer.Length, Allocator.Temp);

            for (int i = 0; i < buffer.Length; i++)
            {
                soldierTypesCopy[i] = buffer[i].soldierType;
            }
        }


        SpawnPlayerArmy(ref state, battleSetupData, battleSetupEntity, soldierTypesCopy);
        //SpawnAIArmy(ref state, battleSetupData, battleSetupEntity, soldierTypesCopy);
        state.Enabled = false; 
    }

    public void SpawnPlayerArmy(ref SystemState state, BattleSetupData setupData, Entity setupEntity, NativeArray<FixedString32Bytes> soldierType)
    {
        var eManager = state.EntityManager;

        int formationCount = setupData.formationPreSide; 
        float formationGap = 2f; 
        float soldierSpacing = 2f; 

        int soldiersPerFormation = setupData.soldiersPerSide;
        int columns = FormationBuilderUtility.CalculateSquareGridColumns(soldiersPerFormation);
        
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
                typeof(FormationColliderData)
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

            if (currentSoldierType.Contains(new FixedString32Bytes("Infantry")))
            {
                detTroopType = 0;
                detSpacing = 2f;
                detMoveSpeed = 7;
            }
            else if (currentSoldierType.Contains(new FixedString32Bytes("Archer")))
            {
                detTroopType = 1;
                detSpacing = 1.5f;
                detMoveSpeed = 6;
            }
            else if (currentSoldierType.Contains(new FixedString32Bytes("Cavalry")))
            {
                detTroopType = 2;
                detSpacing = 3.5f;
                detMoveSpeed = 13;
            }
            else if (currentSoldierType.Contains(new FixedString32Bytes("CavArcher")))
            {
                detTroopType = 3;
                detSpacing = 3f;
                detMoveSpeed = 15;
            }

            float estRows = (setupData.soldiersPerSide / columns) + 1;
            float singleFormationDepth = estRows * detSpacing - detSpacing;
            float exactSingleFormWidth = (columns - 1) * detSpacing;


            float3 colliderCenter = new float3(0, .9f, -singleFormationDepth / 2);

            #endregion

            eManager.SetComponentData(formation, LocalTransform.FromPositionRotation(spawnPosition, quaternion.identity));

            eManager.SetComponentData(formation, new FormationColliderData
            {
                lastColumns = columns,
                lastSoldierCount = soldiersPerFormation,
                spacing = detSpacing,
                needsRebuilt = true,
                filter = new CollisionFilter
                {
                    BelongsTo = PhysicsLayers.Formations,
                    CollidesWith = PhysicsLayers.Formations,
                    GroupIndex = 0
                },
                material = new Unity.Physics.Material
                {
                   CollisionResponse = CollisionResponsePolicy.RaiseTriggerEvents
                }
            });

            eManager.SetComponentData(formation, new PhysicsCollider
            {
                Value = Unity.Physics.BoxCollider.Create(new BoxGeometry
                {
                    Center = colliderCenter,
                    Size = new float3(exactSingleFormWidth, 2f, singleFormationDepth),
                    Orientation = quaternion.identity,
                    BevelRadius = 0.05f
                }),
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
                renderMaterial = setupData.playerMaterial
            });

            eManager.SetComponentData(formation, new FormationCombatData
            {
                meleeDamage = 5,
                meleeDefense = 5,
                troopType = detTroopType
            });

            eManager.AddBuffer<SoldierOffset>(formation);

            currentXOffset += singleFormationWidth + formationGap;
        }
    }


    public void SpawnAIArmy(ref SystemState state, BattleSetupData setupData, Entity setupEntity, NativeArray<FixedString32Bytes> soldierType)
    {
        var eManager = state.EntityManager;

        int formationCount = setupData.formationPreSide;
        float formationGap = 2f;
        float soldierSpacing = 2f;

        int soldiersPerFormation = setupData.soldiersPerSide;
        int columns = FormationBuilderUtility.CalculateSquareGridColumns(soldiersPerFormation);

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
                typeof(FormationColliderData)
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

            if (currentSoldierType.Contains(new FixedString32Bytes("Infantry")))
            {
                detTroopType = 0;
                detSpacing = 2f;
                detMoveSpeed = 7;
            }
            else if (currentSoldierType.Contains(new FixedString32Bytes("Archer")))
            {
                detTroopType = 1;
                detSpacing = 1.5f;
                detMoveSpeed = 6;
            }
            else if (currentSoldierType.Contains(new FixedString32Bytes("Cavalry")))
            {
                detTroopType = 2;
                detSpacing = 3.5f;
                detMoveSpeed = 13;
            }
            else if (currentSoldierType.Contains(new FixedString32Bytes("CavArcher")))
            {
                detTroopType = 3;
                detSpacing = 3f;
                detMoveSpeed = 15;
            }

            float estRows = (setupData.soldiersPerSide / columns) + 1;
            float singleFormationDepth = estRows * detSpacing - detSpacing;
            float exactSingleFormWidth = (columns - 1) * detSpacing;


            float3 colliderCenter = new float3(0, .9f, -singleFormationDepth / 2);

            #endregion

            eManager.SetComponentData(formation, LocalTransform.FromPositionRotation(spawnPosition, quaternion.identity));

            eManager.SetComponentData(formation, new FormationColliderData
            {
                lastColumns = columns,
                lastSoldierCount = soldiersPerFormation,
                spacing = detSpacing,
                needsRebuilt = true,
                filter = new CollisionFilter
                {
                    BelongsTo = PhysicsLayers.Formations,
                    CollidesWith = PhysicsLayers.Formations,
                    GroupIndex = 1
                },
                material = new Unity.Physics.Material
                {
                    CollisionResponse = CollisionResponsePolicy.RaiseTriggerEvents
                }
            });

            eManager.SetComponentData(formation, new PhysicsCollider
            {
                Value = Unity.Physics.BoxCollider.Create(new BoxGeometry
                {
                    Center = colliderCenter,
                    Size = new float3(exactSingleFormWidth, 2f, singleFormationDepth),
                    Orientation = quaternion.identity,
                    BevelRadius = 0.05f
                }),
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
                troopType = detTroopType
            });

            eManager.AddBuffer<SoldierOffset>(formation);

            currentXOffset += singleFormationWidth + formationGap;
        }
    }
}



public static class PhysicsLayers
{
    public const uint Nothing = 0;

    public const uint Formations = 1u << 8;
    public const uint Projectile = 1u << 10;

    public const uint Everything = ~0u;
}
