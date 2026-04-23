using UnityEngine;

/// <summary>
/// Place this on a trigger collider inside each goal.
/// Set 'scoringTeam' to whichever team scores when the ball enters this goal.
/// </summary>
public class GoalTrigger : MonoBehaviour
{
    [Tooltip("The team that SCORES when the ball enters this goal (i.e., the opposing team's goal).")]
    public Team scoringTeam;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Ball"))
        {
            ScoreManager.Instance?.AddGoal(scoringTeam);
        }
    }
}
