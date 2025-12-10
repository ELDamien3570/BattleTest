using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component with baker to setup the BattleSetupData component.
/// </summary>
public class BattleSetupAuthoring: MonoBehaviour
{
    public GameObject playerSoldierAuthor;
    public GameObject enemySoldierAuthor;
    public int soldiersPerSide;
    public Vector3 playerStartLocation = new Vector3(0,0,0);
    public Vector3 enemyStartLocation = new Vector3(10,10,10);

    class BattleBaker: Baker<BattleSetupAuthoring>
    {
        public override void Bake(BattleSetupAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            
            AddComponent(entity, new BattleSetupData 
            {
                playerSoldierAuthor = GetEntity(authoring.playerSoldierAuthor, TransformUsageFlags.Dynamic),
                enemySoldierAuthor = GetEntity(authoring.enemySoldierAuthor, TransformUsageFlags.Dynamic),
                
                soldiersPerSide = authoring.soldiersPerSide,
                playerStartLocation = authoring.playerStartLocation,
                enemyStartLocation = authoring.enemyStartLocation
            });
        }
    }   
}
