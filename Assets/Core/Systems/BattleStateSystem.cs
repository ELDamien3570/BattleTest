using Unity.Entities;
using Unity.Burst;

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct BattleStateSystem : ISystem
{
    // Add a local field to track if we've seen the battle start
    private bool _battleStarted;

    public void OnCreate(ref SystemState state)
    {
        var entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(entity, new BattleStateData());
        
        state.RequireForUpdate<BattleStateData>();
        _battleStarted = false;
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        int pTroops = 0;
        int pFormations = 0;
        int aTroops = 0;
        int aFormations = 0;

        foreach (var pos in SystemAPI.Query<RefRO<FormationPositionData>>().WithAll<PlayerControlled>())
        {
            pFormations++;
            pTroops += pos.ValueRO.soldierCount;
        }

        foreach (var pos in SystemAPI.Query<RefRO<FormationPositionData>>().WithAll<AiControlled>())
        {
            aFormations++;
            aTroops += pos.ValueRO.soldierCount;
        }

        // Update Singleton
        var stateData = SystemAPI.GetSingletonRW<BattleStateData>();
        stateData.ValueRW.playerTroops = pTroops;
        stateData.ValueRW.playerFormations = pFormations;
        stateData.ValueRW.aiTroops = aTroops;
        stateData.ValueRW.aiFormations = aFormations;

        // Only check for defeat if the battle has actually started (units have spawned)
        if (!_battleStarted)
        {
            if (pTroops > 0 && aTroops > 0)
            {
                _battleStarted = true;
            }
        }
        else
        {
            if (pTroops == 0 && pFormations == 0) { stateData.ValueRW.playerDead = true; }
            if (aTroops == 0 && aFormations == 0) { stateData.ValueRW.aiDead = true; }
        }
    }
}
