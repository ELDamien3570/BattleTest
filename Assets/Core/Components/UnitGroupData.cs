using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

public struct FormationPositionData : IComponentData
{
    //location
    public float3 centerPosition;
    public float3 targetPosition;

    //rotation
    public quaternion facing;
    public quaternion targetFacing; 
    public float facingTolerance;

    //formations composition
    public int columns;
    public int rows;
    public int soldierCount;
    public float spacing;

    public FormationState formationState;
    public float desiredAttackDepth; //0-1, how close they'll get to the back of the enemy formation
}
public struct FormationEngagementData : IComponentData
{
    public bool isEngaged;       
    public bool needsEngagementUpdate;
    public Entity targetFormation;
    public float3 lastEngagementPos;   
    public quaternion lastEngagementRot;
}

public struct FormationMovementData : IComponentData
{
    public float moveSpeed;
    public float rotationSpeed;
}

public struct FormationCombatData : IComponentData
{
    public int troopType; 
    public int meleeDamage;
    public int meleeDefense;
    public float formationHealth;
    public int healthPerTroop;
    public bool formationWiped;
    public int deathsQueued;

}

public struct FormationRenderData : IComponentData
{
    public float animationTimer;
    public FixedString32Bytes soldierType;
    public FixedString32Bytes renderMaterial;
}

public struct FormationColliderData : IComponentData
{
    public int lastColumns;
    public int lastSoldierCount;
    public float spacing;
    public bool needsRebuilt;
    public CollisionFilter filter;
    public Material material;
    public float width;
    public float depth;
}

public struct SoldierOffset : IBufferElementData
{
    public float3 currentOffset;
    public float3 targetOffset; 
    public quaternion facing;
    public bool isMoving;
    public bool isEngaged;
    public bool isDead;
    public byte animationGroup;
    public float deathTimer;
    public float speedModifier;
}
public enum FormationState
{
    rebuilding,    //calculating troop offsets
    rotating,      //rotating to face target
    reshaping,     //moving soldiers to new offsets
    retargeting,   //moving formation center
    idle
}

public struct PlayerCommandData : IComponentData
{
    public Entity setTargetFormation;
    public float timeSinceCommand;
}
public struct AiCommandData : IComponentData
{
    public float timeSinceCommand;
}

//tag components
public struct FormationSelected : IComponentData { }
public struct PlayerControlled : IComponentData { }
public struct AiControlled : IComponentData { }

