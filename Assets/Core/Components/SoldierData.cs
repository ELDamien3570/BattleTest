using Unity.Entities;
using Unity.Rendering;
using UnityEngine;

/// <summary>   
/// Component to hold soldier data related to stats, state, and ownership
/// </summary>
public struct SoldierData : IComponentData  
{
    public float health;
    public float maxHealth;
    public float moveSpeed;

    public Entity parentUnitGroup;
    public int formationIndex;
    public int teamID;

}

