using System.Diagnostics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

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

        SpawnPlayerArmy(ref state, battleSetupData);

        state.Enabled = false; 

        UnityEngine.Debug.Log("Battle Setup Complete: Player and AI armies spawned.");
    }

    public void SpawnPlayerArmy(ref SystemState state, BattleSetupData setupData)
    {
        var eManager = state.EntityManager;

        // Configuration for multiple formations
        int formationCount = 3; // Number of formations to spawn
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

            eManager.SetComponentData(formation, new FormationPositionData
            {
                centerPosition = spawnPosition,
                targetPosition = spawnPosition,
                facing = quaternion.identity,
                targetFacing = quaternion.identity, 
                facingTolerance = .1f,

                columns = columns,
                soldierCount = soldiersPerFormation,
                formationState = FormationState.rebuilding 
            });

            eManager.SetComponentData(formation, new FormationMovementData
            {
                moveSpeed = 5f,
                rotationSpeed = math.radians(180f) / 2f
            });

            eManager.SetComponentData(formation, new FormationRenderData
            {
                currentAnimation = 0,
                animationFrame = 0,
                animationTimer = 0,
                soldierType = setupData.soldierType
            });

            eManager.AddBuffer<SoldierOffset>(formation);

            // Advance offset for the next formation
            currentXOffset += singleFormationWidth + formationGap;
        }
    }
}
