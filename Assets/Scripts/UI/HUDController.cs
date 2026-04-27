using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the in-game HUD. Requires TextMeshPro (included in Unity 6 by default).
/// Unity 6000.x compatible.
/// </summary>
public class HUDController : MonoBehaviour
{
    [Header("Score")]
    public TMP_Text homeScoreText;
    public TMP_Text awayScoreText;
    public TMP_Text homeTeamLabel;
    public TMP_Text awayTeamLabel;

    [Header("Timers")]
    public TMP_Text gameTimerText;
    public TMP_Text shotClockText;
    public TMP_Text halfText;

    [Header("Panels")]
    public GameObject pausePanel;
    public GameObject gameOverPanel;
    public GameObject halftimePanel;
    public GameObject startPanel;

    [Header("Game Over")]
    public TMP_Text gameOverResultText;

    [Header("Debug / Speed")]
    public TMP_Text speedText;

    private PlayerController _playerController;

    private void Awake()
    {
        _playerController = FindFirstObjectByType<PlayerController>();
    }

    private void OnEnable()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnScoreChanged += HandleScoreChanged;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnStateChanged += HandleStateChanged;
            GameManager.Instance.OnHalfChanged  += HandleHalfChanged;
        }
    }

    private void OnDisable()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnScoreChanged -= HandleScoreChanged;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnStateChanged -= HandleStateChanged;
            GameManager.Instance.OnHalfChanged  -= HandleHalfChanged;
        }
    }

    private void Update()
    {
        if (GameManager.Instance == null) return;

        gameTimerText.text = FormatTime(GameManager.Instance.HalfTimeRemaining);
        int shotSecs       = Mathf.CeilToInt(GameManager.Instance.ShotClockRemaining);
        shotClockText.text = shotSecs.ToString();

        shotClockText.color = shotSecs <= 10
            ? new Color(1f, 0.2f, 0.2f, 1f)
            : Color.white;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (GameManager.Instance.State == GameState.Playing)
                GameManager.Instance.PauseGame();
            else if (GameManager.Instance.State == GameState.Paused)
                GameManager.Instance.ResumeGame();
        }

        if (speedText != null && _playerController != null)
            speedText.text = $"{_playerController.MoveSpeed:F1} m/s";
    }

    private void HandleScoreChanged(Team team, int newScore)
    {
        if (team == Team.Home) homeScoreText.text = newScore.ToString();
        else                   awayScoreText.text = newScore.ToString();
    }

    private void HandleStateChanged(GameState state)
    {
        pausePanel?.SetActive(state == GameState.Paused);
        halftimePanel?.SetActive(state == GameState.Halftime);
        startPanel?.SetActive(state == GameState.WaitingToStart);

        if (state == GameState.GameOver)
        {
            gameOverPanel?.SetActive(true);
            if (gameOverResultText != null && ScoreManager.Instance != null)
            {
                bool tied = ScoreManager.Instance.IsTied();
                gameOverResultText.text = tied
                    ? "DRAW"
                    : $"{ScoreManager.Instance.GetLeader()} WINS!";
            }
        }
        else
        {
            gameOverPanel?.SetActive(false);
        }
    }

    private void HandleHalfChanged(int half)
    {
        if (halfText != null) halfText.text = $"HALF {half}";
    }

    public void OnResumePressed()  => GameManager.Instance?.ResumeGame();
    public void OnRestartPressed() => GameManager.Instance?.RestartGame();
    public void OnStartPressed()   => GameManager.Instance?.StartGame();

    private static string FormatTime(float seconds)
    {
        int m = Mathf.FloorToInt(seconds / 60f);
        int s = Mathf.FloorToInt(seconds % 60f);
        return $"{m:00}:{s:00}";
    }
}
