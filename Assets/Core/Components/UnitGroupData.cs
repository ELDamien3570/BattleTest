using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

public struct FormationPositionData : IComponentData
{
    //Location
    public float3 centerPosition;
    public float3 targetPosition;

    //Rotation
    public quaternion facing;
    public float facingTolerance;

    //Soldier Placement
    public int columns;
    public int rows;
    public int soldierCount;

    //State
    public FormationState formationState;
}

public struct FormationMovementData : IComponentData
{
    public float moveSpeed;
    public float rotationSpeed;
}

public struct FormationRenderData : IComponentData
{
    public int currentAnimation;       // 0 = idle, 1 = walk
    public int animationFrame;
    public float animationTimer;
    public FixedString64Bytes soldierType;
}

public enum FormationState
{
    rebuilding,
    retargeting,
    idle
}

public struct SoldierOffset : IBufferElementData
{
    public float3 Value;
    public quaternion facing;
}
