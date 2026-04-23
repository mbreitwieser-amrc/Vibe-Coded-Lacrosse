using UnityEngine;

/// <summary>
/// Place on a trigger collider inside each goal net.
/// Set scoringTeam to the team that SCORES when the ball enters this goal.
/// Unity 6000.x.
/// </summary>
public class GoalTrigger : MonoBehaviour
{
    [Tooltip("Team that SCORES when the ball enters this net.")]
    public Team scoringTeam;

    [Tooltip("Spawn position after a goal. Defaults to trigger centre if unset.")]
    public Transform ballRespawnPoint;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Ball")) return;

        ScoreManager.Instance?.AddGoal(scoringTeam);

        BallController ball = other.GetComponent<BallController>();
        if (ball != null)
        {
            ball.Release(Vector3.zero);
            Vector3 spawnPos = ballRespawnPoint != null
                ? ballRespawnPoint.position
                : transform.position + Vector3.up * 1f;
            ball.transform.position = spawnPos;
        }
    }
}
