using Unity.Entities;

[InternalBufferCapacity(8)]
public struct UnitGroupBuffer: IBufferElementData
{
    public Entity unitGroupEntity;
}
