using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

/// <summary>
/// Singleton Component for data related to battle setup. 
/// </summary>
public struct BattleSetupData: IComponentData
{

    // Formation and positioning info
    public int soldiersPerSide;
    public int formationPreSide;
    public float3 playerStartLocation;

}

public struct SoldierTypeBuffer: IBufferElementData
{
    public FixedString32Bytes soldierType;
}
