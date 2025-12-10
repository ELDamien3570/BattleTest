using Unity.Entities;
using UnityEngine;

[UpdateInGroup(typeof(InitializationSystemGroup), OrderLast = true)]
public partial class PlayerInputSystem : SystemBase
{
    
    protected override void OnUpdate()
    {
        if (Input.GetMouseButtonDown(0))
        {

        }
    }
}
