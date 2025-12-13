using Unity.Entities;

public struct BattleStateData: IComponentData
{
    public int playerTroops;
    public int playerFormations;
    public int aiTroops;
    public int aiFormations;
    public bool playerDead;
    public bool aiDead;
}