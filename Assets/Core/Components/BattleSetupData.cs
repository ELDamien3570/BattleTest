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
    public FixedString32Bytes soldierType;
    // Formation and positioning info
    public int soldiersPerSide;
    public float3 playerStartLocation;

}
