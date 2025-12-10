using Unity.Entities;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Authoring component for soldier entity prefab.  
/// </summary>
public class SoldierPrefabAuthoring: MonoBehaviour
{
    public int health = 100;
    public int maxHealth = 100; 
    public float moveSpeed = 5f;

    class Baker : Baker<SoldierPrefabAuthoring>
    {
        public override void Bake(SoldierPrefabAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            

            AddComponent(entity, new SoldierData
            {
                health = authoring.health,
                maxHealth = authoring.maxHealth,
                moveSpeed = authoring.moveSpeed,
                parentUnitGroup = Entity.Null,
                formationIndex = -1,
                teamID = -1
            });

            
        }
    }
}

public class AnimationBakeHelper : IComponentData
{
    
}