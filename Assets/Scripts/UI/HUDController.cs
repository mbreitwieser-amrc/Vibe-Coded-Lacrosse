using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the in-game HUD. Assign references in the Inspector.
/// </summary>
public class HUDController : MonoBehaviour
{
    [Header("Score")]
    public TMP_Text homeScoreText;
    public TMP_Text awayScoreText;

    [Header("Timers")]
    public TMP_Text gameTimerText;
    public TMP_Text shotClockText;

    [Header("Panels")]
    public GameObject pausePanel;
    public GameObject gameOverPanel;

    private void OnEnable()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnScoreChanged += HandleScoreChanged;

        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged += HandleStateChanged;
    }

    private void OnDisable()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnScoreChanged -= HandleScoreChanged;

        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void Update()
    {
        if (GameManager.Instance == null) return;

        gameTimerText.text = FormatTime(GameManager.Instance.GameTimeRemaining);
        shotClockText.text = Mathf.CeilToInt(GameManager.Instance.ShotClockRemaining).ToString();
    }

    private void HandleScoreChanged(Team team, int newScore)
    {
        if (team == Team.Home)
            homeScoreText.text = newScore.ToString();
        else
            awayScoreText.text = newScore.ToString();
    }

    private void HandleStateChanged(GameState state)
    {
        pausePanel.SetActive(state == GameState.Paused);
        gameOverPanel.SetActive(state == GameState.GameOver);
    }

    private string FormatTime(float seconds)
    {
        int m = Mathf.FloorToInt(seconds / 60f);
        int s = Mathf.FloorToInt(seconds % 60f);
        return $"{m:00}:{s:00}";
    }
}
