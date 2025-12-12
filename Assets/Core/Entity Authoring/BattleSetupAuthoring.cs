using Unity.Entities;
using UnityEngine;
using System.Collections.Generic;

public class BattleSetupAuthoring : MonoBehaviour
{
    public List<string> soldierType = new List<string>();
    public string playerMat;
    public string aiMat;

    public int soldiersPerSide;
    public int formationsPerSide;
    
    public Vector3 playerStartLocation = new Vector3(0, 0, 0);
    public Vector3 aiStartLocation = new Vector3(0, 0, 0);

    class BattleBaker : Baker<BattleSetupAuthoring>
    {
        public override void Bake(BattleSetupAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new BattleSetupData
            {
                soldiersPerSide = authoring.soldiersPerSide,
                formationPreSide = authoring.formationsPerSide, 
                playerStartLocation = authoring.playerStartLocation,
                aiStartLocation = authoring.aiStartLocation,
                playerMaterial = authoring.playerMat,
                aiMaterial = authoring.aiMat
            });

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

