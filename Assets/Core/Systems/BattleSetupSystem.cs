using System.Diagnostics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Unity.Collections;
using JetBrains.Annotations;


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

        state.Enabled = false; 
    }

    public void SpawnPlayerArmy(ref SystemState state, BattleSetupData setupData, Entity setupEntity, NativeArray<FixedString32Bytes> soldierType)
    {
        var eManager = state.EntityManager;

        // Get the soldier type buffer
        

        // Configuration for multiple formations
        int formationCount = setupData.formationPreSide; // Number of formations to spawn
        float formationGap = 2f; // Space between formations
        float soldierSpacing = 1f; // Should match the spacing used in FormationBuilderUtility

        int soldiersPerFormation = setupData.soldiersPerSide;
        int columns = FormationBuilderUtility.CalculateSquareGridColumns(soldiersPerFormation);
        
        // Calculate the physical width of one formation
        // Width = (columns - 1) * spacing
        float singleFormationWidth = (columns - 1) * soldierSpacing;

        // Calculate the total width of the entire army line
        float totalArmyWidth = (singleFormationWidth * formationCount) + (formationGap * (formationCount - 1));

        // Calculate the starting X offset (leftmost edge relative to center)
        float startXOffset = -totalArmyWidth * 0.5f;

        // The center of the first formation is: StartEdge + HalfFormationWidth
        float currentXOffset = startXOffset + (singleFormationWidth * 0.5f);

        for (int i = 0; i < formationCount; i++)
        {
            Entity formation = eManager.CreateEntity(
                typeof(FormationPositionData),
                typeof(FormationMovementData),
                typeof(FormationRenderData)
            );

            // Calculate position for this specific formation
            float3 spawnPosition = setupData.playerStartLocation;
            spawnPosition.x += currentXOffset;

            // Determine soldier type for this formation
            FixedString32Bytes currentSoldierType = "player"; // Default fallback
            if (soldierType.Length > 0)
            {
                // Cycle through types if we have more formations than types
                int typeIndex = i % soldierType.Length;
                currentSoldierType = soldierType[typeIndex];
                UnityEngine.Debug.Log(currentSoldierType);
            }



            #region Decide Troop Type from current Soldier Type string

            int detTroopType = 0;
            float detSpacing = 1;
            float detMoveSpeed = 5;

            if (currentSoldierType.Contains(new FixedString32Bytes("Infantry")))
            {
                detTroopType = 0;
                detSpacing = 1;
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
                detSpacing = 2f;
                detMoveSpeed = 11;
            }
            else if (currentSoldierType.Contains(new FixedString32Bytes("CavArcher")))
            {
                detTroopType = 3;
                detSpacing = 2.5f;
                detMoveSpeed = 10;
            }



            #endregion

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
                formationState = FormationState.rebuilding ,
                troopType = detTroopType
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
                soldierType = currentSoldierType // Set the type from buffer
            });

            eManager.AddBuffer<SoldierOffset>(formation);

            // Advance offset for the next formation
            currentXOffset += singleFormationWidth + formationGap;
        }
    }
}
