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
    public quaternion targetFacing; 
    public float facingTolerance;

    //Soldier Placement
    public int columns;
    public int rows;
    public int soldierCount;
    public float spacing;

    //State
    public FormationState formationState;
    
    // Add blend progress
    public float formationBlendProgress;
    public int troopType; //0 = infantry, 1 = archer, 2 = cav, 3 = cav archer
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
    rebuilding,    // Trigger state: Calculates new positions once, then transitions
    rotating,      // Rotating to face target direction
    reshaping,     // Moving soldiers to new local offsets
    retargeting,   // Moving the whole formation center to target
    idle
}      

public struct SoldierOffset : IBufferElementData
{
    public float3 currentOffset;
    public float3 targetOffset; 
    public quaternion facing;
    public bool isMoving; 
    public float speedModifier;
}

// NEW: Tag component for selection
public struct FormationSelected : IComponentData { }
