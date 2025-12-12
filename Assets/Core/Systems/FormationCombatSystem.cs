using Unity.Entities;

public partial struct FormationCombatSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
       // float damagePerSecond = 10f;

        foreach (var offsets in SystemAPI.Query<DynamicBuffer<SoldierOffset>>())
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                var soldier = offsets[i];
                if (soldier.isEngaged)
                {
                    // Apply damage, play effects, etc.
                }
            }
        }
    }
}