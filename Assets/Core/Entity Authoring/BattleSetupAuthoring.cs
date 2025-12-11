using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using Unity.Rendering;

/// <summary>
/// Authoring component with baker to setup the BattleSetupData component.
/// </summary>
public class BattleSetupAuthoring : MonoBehaviour
{
    public string soldierType;

    public int soldiersPerSide;
    public Vector3 playerStartLocation = new Vector3(0, 0, 0);

    class BattleBaker : Baker<BattleSetupAuthoring>
    {
        public override void Bake(BattleSetupAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new BattleSetupData
            {
                soldiersPerSide = authoring.soldiersPerSide,
                playerStartLocation = authoring.playerStartLocation,
                soldierType = authoring.soldierType
            });

        }
    }
}
