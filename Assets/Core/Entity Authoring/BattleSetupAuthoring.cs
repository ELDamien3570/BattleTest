using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using System.Collections.Generic;
using Unity.Rendering;

/// <summary>
/// Authoring component with baker to setup the BattleSetupData component.
/// </summary>
public class BattleSetupAuthoring : MonoBehaviour
{
    public List<string> soldierType = new List<string>();

    public int soldiersPerSide;
    public int formationsPerSide;
    
    public Vector3 playerStartLocation = new Vector3(0, 0, 0);

    class BattleBaker : Baker<BattleSetupAuthoring>
    {
        public override void Bake(BattleSetupAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new BattleSetupData
            {
                soldiersPerSide = authoring.soldiersPerSide,
                formationPreSide = authoring.formationsPerSide, // Ensure this is passed
                playerStartLocation = authoring.playerStartLocation,
            });

            // Add and populate the buffer
            var buffer = AddBuffer<SoldierTypeBuffer>(entity);
            foreach (var type in authoring.soldierType)
            {
                if (!string.IsNullOrEmpty(type))
                {
                    

                    buffer.Add(new SoldierTypeBuffer { soldierType = type });
                }
            }
        }
    }
}

