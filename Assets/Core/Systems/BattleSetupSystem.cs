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

        int columns = FormationBuilderUtility.CalculateSquareGridColumns(setupData.soldiersPerSide);

        Entity formation = eManager.CreateEntity(
            typeof(FormationPositionData),
            typeof(FormationMovementData),
            typeof(FormationRenderData)
        );

        eManager.SetComponentData(formation, new FormationPositionData
        {
            centerPosition = setupData.playerStartLocation,
            targetPosition = setupData.playerStartLocation,
            facing = quaternion.identity,
            targetFacing = quaternion.identity, // Initialize targetFacing
            facingTolerance = .1f,

            columns = columns,
            soldierCount = setupData.soldiersPerSide,
            formationState = FormationState.rebuilding // CRITICAL: Set to rebuilding to trigger buffer population
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
    }
}
