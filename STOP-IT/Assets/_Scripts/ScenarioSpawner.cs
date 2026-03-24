using UnityEngine;

/// <summary>
/// STOP IT! — ScenarioSpawner
/// Repositions the ChildNPC at a spawn point at the start of each scenario.
/// Also resets the NPC's NavMeshAgent.
/// </summary>
public class ScenarioSpawner : MonoBehaviour
{
    [Header("References")]
    public ChildNPC childNPC;
    public Transform spawnPoint;

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnStateChanged);
    }

    private void OnStateChanged(GameManager.GameState state)
    {
        if (state != GameManager.GameState.Playing) return;
        if (childNPC == null || spawnPoint == null) return;

        var agent = childNPC.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null)
        {
            agent.Warp(spawnPoint.position);
        }
        else
        {
            childNPC.transform.position = spawnPoint.position;
        }
        childNPC.transform.rotation = spawnPoint.rotation;
    }
}
