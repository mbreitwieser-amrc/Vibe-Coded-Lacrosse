using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Game Settings")]
    public float gameDuration = 480f;  // 8 minutes (two 4-min halves)
    public float shotClockDuration = 60f;

    [Header("State")]
    public GameState State { get; private set; } = GameState.WaitingToStart;

    private float _gameTimeRemaining;
    private float _shotClockRemaining;

    public float GameTimeRemaining => _gameTimeRemaining;
    public float ShotClockRemaining => _shotClockRemaining;

    public event System.Action<GameState> OnStateChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        _gameTimeRemaining = gameDuration;
        _shotClockRemaining = shotClockDuration;
    }

    private void Update()
    {
        if (State != GameState.Playing) return;

        _gameTimeRemaining -= Time.deltaTime;
        _shotClockRemaining -= Time.deltaTime;

        if (_shotClockRemaining <= 0f)
        {
            // TODO: Turnover / violation
            ResetShotClock();
        }

        if (_gameTimeRemaining <= 0f)
        {
            SetState(GameState.GameOver);
        }
    }

    public void StartGame()
    {
        _gameTimeRemaining = gameDuration;
        ResetShotClock();
        SetState(GameState.Playing);
    }

    public void PauseGame()
    {
        if (State == GameState.Playing)
        {
            Time.timeScale = 0f;
            SetState(GameState.Paused);
        }
    }

    public void ResumeGame()
    {
        if (State == GameState.Paused)
        {
            Time.timeScale = 1f;
            SetState(GameState.Playing);
        }
    }

    public void ResetShotClock()
    {
        _shotClockRemaining = shotClockDuration;
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void SetState(GameState newState)
    {
        State = newState;
        OnStateChanged?.Invoke(newState);
    }
}

public enum GameState
{
    WaitingToStart,
    Playing,
    Paused,
    GameOver
}
