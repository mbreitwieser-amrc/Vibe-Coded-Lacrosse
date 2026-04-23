using UnityEngine;

/// <summary>
/// Stick state provider and AI interface. Unity 6000.x.
///
/// All ball-stick interaction is physics-based — the ball is pushed and carried
/// entirely by cup colliders on the StickHeadSocket, driven by StickInputController.
///
/// This script is responsible for:
///   HasBall              — whether CupPhysics currently contains the ball
///   OnShotFired          — event fired when stick velocity crosses the shot threshold
///                          (used for shot-sound feedback and game-manager hooks)
///   AIRelease(pos,speed) — lets AIController programmatically launch the ball
///                          (AI agents cannot physically swing the stick)
/// </summary>
public class StickController : MonoBehaviour
{
    [Header("Shot Detection (physics feedback)")]
    [Tooltip("Stick head speed (m/s) at which the shot audio/event fires.")]
    public float shotVelocityThreshold = 8f;

    // ── Public state ──────────────────────────────────────────────────────────
    public bool HasBall => _cup != null && _cup.BallInCup != null;

    public event System.Action OnShotFired;

    // ── Private ───────────────────────────────────────────────────────────────
    private StickInputController _stickInput;
    private CupPhysics           _cup;
    private bool                 _velocityShotTriggered;

    private void Awake()
    {
        _stickInput = GetComponent<StickInputController>();
        _cup        = GetComponentInChildren<CupPhysics>();
    }

    private void Update()
    {
        if (GameManager.Instance != null &&
            GameManager.Instance.State != GameState.Playing)
            return;

        DetectVelocityShot();
    }

    // ── Velocity-based shot detection (physics feedback) ──────────────────────

    private void DetectVelocityShot()
    {
        if (_stickInput == null) return;

        bool fast = _stickInput.StickVelocity.magnitude > shotVelocityThreshold;

        if (fast && !_velocityShotTriggered && HasBall)
        {
            _velocityShotTriggered = true;
            AudioManager.Instance?.PlaySFX(AudioManager.SFXType.Shoot,
                _stickInput.stickHeadSocket.position);
            OnShotFired?.Invoke();
        }
        else if (!fast)
        {
            _velocityShotTriggered = false;
        }
    }

    // ── AI interface ──────────────────────────────────────────────────────────

    /// <summary>
    /// Programmatically launches the ball toward a world-space position at the
    /// given speed. For AI use only — human players shoot via physics stick movement.
    /// </summary>
    public void AIRelease(Vector3 targetPosition, float speed)
    {
        if (!HasBall) return;
        _cup.BallInCup.LaunchToward(targetPosition, speed);
    }
}
