using TMPro;
using Unity.Entities;
using UnityEngine;

public class ScorePanel : MonoBehaviour
{
    public TMP_Text playerFormText;
    public TMP_Text playerTroopText;
    public TMP_Text aiFormText;
    public TMP_Text aiTroopText;
    public GameObject scorePanel;

    public GameObject WinPanel;
    public GameObject LostPanel;

    private EntityManager entityManager;
    private EntityQuery stateQuery;

    private void Start()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        stateQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<BattleStateData>());

        // Ensure panels are hidden at start
        if (WinPanel) WinPanel.SetActive(false);
        if (LostPanel) LostPanel.SetActive(false);
        if (scorePanel) scorePanel.SetActive(true);
    }

    private void Update()
    {
        if (stateQuery.IsEmpty) return;

        var state = stateQuery.GetSingleton<BattleStateData>();


        playerFormText.text = state.playerFormations.ToString();
        playerTroopText.text = state.playerTroops.ToString();
        aiFormText.text = state.aiFormations.ToString();
        aiTroopText.text = state.aiTroops.ToString();

        if (state.playerDead)
        {
            scorePanel.SetActive(false);
            LostPanel.SetActive(true);
        }

        if (state.aiDead)
        {
            scorePanel.SetActive(false);
            WinPanel.SetActive(true);
        }
    }
}
