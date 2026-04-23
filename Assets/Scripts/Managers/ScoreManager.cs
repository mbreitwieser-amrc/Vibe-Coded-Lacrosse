using UnityEngine;

public enum Team { Home, Away }

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    private int _homeScore;
    private int _awayScore;

    public int HomeScore => _homeScore;
    public int AwayScore => _awayScore;

    public event System.Action<Team, int> OnScoreChanged;  // (team, newScore)

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void AddGoal(Team team)
    {
        if (team == Team.Home)
        {
            _homeScore++;
            OnScoreChanged?.Invoke(Team.Home, _homeScore);
        }
        else
        {
            _awayScore++;
            OnScoreChanged?.Invoke(Team.Away, _awayScore);
        }

        GameManager.Instance?.ResetShotClock();
        Debug.Log($"Goal! Home {_homeScore} - Away {_awayScore}");
    }

    public void ResetScores()
    {
        _homeScore = 0;
        _awayScore = 0;
        OnScoreChanged?.Invoke(Team.Home, 0);
        OnScoreChanged?.Invoke(Team.Away, 0);
    }

    public Team GetLeader()
    {
        return _homeScore >= _awayScore ? Team.Home : Team.Away;
    }
}
