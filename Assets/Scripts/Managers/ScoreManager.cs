using UnityEngine;

public enum Team { Home, Away }

/// <summary>Tracks goals for both teams. Unity 6000.x.</summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    public int HomeScore { get; private set; }
    public int AwayScore { get; private set; }

    public event System.Action<Team, int> OnScoreChanged;
    public event System.Action<Team>      OnGoalScored;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void AddGoal(Team team)
    {
        if (team == Team.Home) HomeScore++;
        else                   AwayScore++;

        OnScoreChanged?.Invoke(team, team == Team.Home ? HomeScore : AwayScore);
        OnGoalScored?.Invoke(team);
        GameManager.Instance?.ResetShotClock();
        AudioManager.Instance?.PlaySFX(AudioManager.SFXType.Goal, Vector3.zero);
        Debug.Log($"GOAL! Home {HomeScore} - Away {AwayScore}");
    }

    public void ResetScores()
    {
        HomeScore = 0;
        AwayScore = 0;
        OnScoreChanged?.Invoke(Team.Home, 0);
        OnScoreChanged?.Invoke(Team.Away, 0);
    }

    public Team GetLeader() => HomeScore >= AwayScore ? Team.Home : Team.Away;
    public bool IsTied()    => HomeScore == AwayScore;
}
