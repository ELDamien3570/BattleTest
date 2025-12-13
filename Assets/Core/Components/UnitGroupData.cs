using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

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
    
    //blend progress
    public float formationBlendProgress;

    public float desiredAttackDepth; //0-1, how close they'll get to the center of the enemy formation

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
    public int troopType; //0 = infantry, 1 = archer, 2 = cav, 3 = cav archer
    public int meleeDamage;
    public int meleeDefense;
    public float formationHealth;
    public float lastDeathHealth;
    public bool markedForDeath;
    public int healthPerTroop;
    public bool formationWiped;

}

public struct FormationRenderData : IComponentData
{
    public int currentAnimation;       //0 = idle, 1 = walk, 2 = combat
    public int animationFrame;
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


//tag components
public struct FormationSelected : IComponentData { }
public struct PlayerControlled : IComponentData { }
public struct AiControlled : IComponentData { }

