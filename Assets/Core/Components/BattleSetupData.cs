using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Singleton Component for data related to battle setup. 
/// </summary>
public struct BattleSetupData: IComponentData
{
    // Prefabs for player and enemy soldiers.
    public Entity playerSoldierAuthor;
    public Entity enemySoldierAuthor;

    // Formation and positioning info
    public int soldiersPerSide;
    public float3 playerStartLocation;
    public float3 enemyStartLocation;
}
