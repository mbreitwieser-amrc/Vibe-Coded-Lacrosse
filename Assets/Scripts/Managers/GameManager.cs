using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Singleton that owns game state, timers, and halftime logic. Unity 6000.x.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Game Settings")]
    public float halfDuration    = 240f;  // 4 minutes per half
    public float shotClockLimit  = 60f;
    public int   totalHalves     = 2;

    // Public read-only state
    public GameState State             { get; private set; } = GameState.WaitingToStart;
    public float     HalfTimeRemaining { get; private set; }
    public float     ShotClockRemaining{ get; private set; }
    public int       CurrentHalf       { get; private set; } = 1;

    // Events
    public event System.Action<GameState> OnStateChanged;
    public event System.Action<int>       OnHalfChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        HalfTimeRemaining  = halfDuration;
        ShotClockRemaining = shotClockLimit;
    }

    private void Update()
    {
        if (State != GameState.Playing) return;

        HalfTimeRemaining  -= Time.deltaTime;
        ShotClockRemaining -= Time.deltaTime;

        if (ShotClockRemaining <= 0f)
            ResetShotClock();

        if (HalfTimeRemaining <= 0f)
            EndHalf();
    }

    public void StartGame()
    {
        CurrentHalf        = 1;
        HalfTimeRemaining  = halfDuration;
        ResetShotClock();
        ScoreManager.Instance?.ResetScores();
        SetState(GameState.Playing);
    }

    public void PauseGame()
    {
        if (State != GameState.Playing) return;
        Time.timeScale = 0f;
        SetState(GameState.Paused);
    }

    public void ResumeGame()
    {
        if (State != GameState.Paused) return;
        Time.timeScale = 1f;
        SetState(GameState.Playing);
    }

    public void ResetShotClock() => ShotClockRemaining = shotClockLimit;

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void EndHalf()
    {
        if (CurrentHalf < totalHalves)
        {
            CurrentHalf++;
            HalfTimeRemaining = halfDuration;
            ResetShotClock();
            OnHalfChanged?.Invoke(CurrentHalf);
            SetState(GameState.Halftime);
            Invoke(nameof(StartSecondHalf), 3f);
        }
        else
        {
            SetState(GameState.GameOver);
            AudioManager.Instance?.PlaySFX(AudioManager.SFXType.FinalWhistle, Vector3.zero);
        }
    }

    private void StartSecondHalf() => SetState(GameState.Playing);

    private void SetState(GameState newState)
    {
        State = newState;
        OnStateChanged?.Invoke(newState);
    }
}

public enum GameState { WaitingToStart, Playing, Paused, Halftime, GameOver }
