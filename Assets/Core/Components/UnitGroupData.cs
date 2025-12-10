using Unity.Entities;
using Unity.Mathematics;

public struct UnitGroupData: IComponentData
{
    public int soldierCount;
    public float3 centerPosition;
    public float3 targetPosition;
    public UnitGroupState currentState;
    public Entity commander;
}

public enum UnitGroupState
{
    MovingToTarget,
    EngagingEnemy,
    HoldingPosition,
    Retreating
}