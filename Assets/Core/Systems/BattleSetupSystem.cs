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
        SpawnAIArmy(ref state, battleSetupData);

        state.Enabled = false; 

        UnityEngine.Debug.Log("Battle Setup Complete: Player and AI armies spawned.");
    }

    public void SpawnPlayerArmy(ref SystemState state, BattleSetupData setupData)
    {
        Entity playerCommander = state.EntityManager.CreateEntity(
            typeof(Commander),
            typeof(PlayerControlled),
            typeof(UnitGroupBuffer)
            );

        SystemAPI.SetComponent(playerCommander, new Commander
        {
            teamID = 0
        });

        Entity unitGroup = state.EntityManager.CreateEntity(
            typeof(UnitGroupData),
            typeof(LocalTransform)
            );

        SystemAPI.SetComponent(unitGroup, new UnitGroupData
        {
            centerPosition = setupData.playerStartLocation,
            targetPosition = setupData.playerStartLocation,
            soldierCount = setupData.soldiersPerSide,
            currentState = UnitGroupState.HoldingPosition,
            commander = playerCommander
        });

        SystemAPI.SetComponent(unitGroup, LocalTransform.FromPosition(setupData.playerStartLocation));
        var commanderGroupsBuffer = state.EntityManager.GetBuffer<UnitGroupBuffer>(playerCommander);

        commanderGroupsBuffer.Add(new UnitGroupBuffer
        {
            unitGroupEntity = unitGroup
        });

        SpawnSoldiers(ref state, setupData.playerSoldierAuthor, unitGroup, setupData.playerStartLocation, 0, setupData.soldiersPerSide);
    }

    public void SpawnAIArmy(ref SystemState state, BattleSetupData battleSetupData)
    {
        Entity AICommander = state.EntityManager.CreateEntity(
            typeof(Commander),
            typeof(AIControlled),
            typeof(UnitGroupBuffer)
            );

        SystemAPI.SetComponent(AICommander, new Commander
        {
            teamID = 1
        });


        Entity unitGroup = state.EntityManager.CreateEntity(
            typeof(UnitGroupData),
            typeof(LocalTransform)
            );

        SystemAPI.SetComponent(unitGroup, new UnitGroupData
        {
            
            centerPosition = battleSetupData.enemyStartLocation,
            targetPosition = battleSetupData.enemyStartLocation,
            soldierCount = battleSetupData.soldiersPerSide,
            currentState = UnitGroupState.HoldingPosition,
            commander = AICommander
        });

        SystemAPI.SetComponent(unitGroup, LocalTransform.FromPosition(battleSetupData.enemyStartLocation));
        var commanderGroupsBuffer = state.EntityManager.GetBuffer<UnitGroupBuffer>(AICommander);

        commanderGroupsBuffer.Add(new UnitGroupBuffer
        {
            unitGroupEntity = unitGroup
        });


        SpawnSoldiers(ref state, battleSetupData.enemySoldierAuthor, unitGroup, battleSetupData.enemyStartLocation, 1, battleSetupData.soldiersPerSide);
    }

    public void SpawnSoldiers(ref SystemState state, Entity soldierPrefab, Entity unitGroup, float3 startingPos, int teamID, int count)
    {
        float maxFormationWidth = 50f;
        float soldierSpacing = 1.5f;
        int maxSoldiersPerRow = (int)(maxFormationWidth / soldierSpacing);  // e.g., 200 soldiers per row

        UnityEngine.Debug.Log($"SpawnSoldiers called: {count} soldiers, max {maxSoldiersPerRow} per row");

        for (int i = 0; i < count; i++)
        {
            Entity soldier = state.EntityManager.Instantiate(soldierPrefab);

            // Calculate row and column
            int row = i / maxSoldiersPerRow;  // Which row (0, 1, 2, ...)
            int col = i % maxSoldiersPerRow;  // Which column in that row (0 to maxSoldiersPerRow-1)

            // Position: spread columns along X, rows along Z
            float3 offset = new float3(
                col * soldierSpacing,      // X: spread across width
                0,
                row * soldierSpacing       // Z: add depth for each row
            );

            float3 soldierPos = startingPos + offset;

            SystemAPI.SetComponent(soldier, LocalTransform.FromPosition(soldierPos));
            SystemAPI.SetComponent(soldier, new SoldierData
            {
                parentUnitGroup = unitGroup,
                formationIndex = i,
                teamID = teamID
            });
        }
    }
}
