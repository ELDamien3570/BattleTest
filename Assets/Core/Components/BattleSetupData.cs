using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public struct BattleSetupData: IComponentData
{
    public int soldiersPerSide;
    public int formationPreSide;

    public float3 playerStartLocation;
    public float3 aiStartLocation;

    public FixedString32Bytes playerMaterial;
    public FixedString32Bytes aiMaterial;
}

public struct SoldierTypeBuffer: IBufferElementData
{
    public FixedString32Bytes soldierType;
}
