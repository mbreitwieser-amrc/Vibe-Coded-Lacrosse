using UnityEngine;

/// <summary>
/// High-level stick game-logic layer.
///
/// Works alongside:
///   StickInputController — mouse/scroll drives stick head position/rotation
///   CupPhysics           — physics cup holds ball, cradling emerges from physics
///
/// Responsibilities here:
///   • Fallback explicit pass/shoot buttons (Fire1 / Fire2)
///   • Velocity-threshold shot detection (for audio / events)
///   • AI release (force-based)
///
/// Unity 6000.x.
/// </summary>
public class StickController : MonoBehaviour
{
    [Header("Pass / Shoot")]
    public float passSpeed       = 15f;
    public float shootSpeed      = 32f;
    public float shootChargeTime = 0.5f;

    [Header("Velocity Shot Detection")]
    [Tooltip("Stick head speed (m/s) that triggers the shot sound/event.")]
    public float shotVelocityThreshold = 8f;

    [Header("References")]
    public UnityEngine.Camera playerCamera;

    // ── Public state ──────────────────────────────────────────────────────────
    public bool  HasBall        => _cup != null && _cup.BallInCup != null;
    public float ShotChargeRatio { get; private set; }

    // Events
    public event System.Action OnShotFired;
    public event System.Action OnPassMade;

    // ── Private ───────────────────────────────────────────────────────────────
    private StickInputController _stickInput;
    private CupPhysics           _cup;
    private float                _chargeStart;
    private bool                 _isCharging;
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

        HandleExplicitInputs();
        DetectVelocityShot();
    }

    // ── Explicit inputs ───────────────────────────────────────────────────────

    private void HandleExplicitInputs()
    {
        if (!HasBall) return;

        // Quick pass — tap Fire1
        if (UnityEngine.Input.GetButtonDown("Fire1"))
        {
            _cup.BallInCup.Release(GetAimDirection() * passSpeed);
            AudioManager.Instance?.PlaySFX(AudioManager.SFXType.Pass,
                _stickInput.stickHeadSocket.position);
            OnPassMade?.Invoke();
        }

        // Charge shot — hold Fire2, release to fire
        if (UnityEngine.Input.GetButtonDown("Fire2"))
        {
            _isCharging  = true;
            _chargeStart = UnityEngine.Time.time;
        }

        if (_isCharging)
        {
            ShotChargeRatio = UnityEngine.Mathf.Clamp01(
                (UnityEngine.Time.time - _chargeStart) / shootChargeTime);
        }

        if (_isCharging && UnityEngine.Input.GetButtonUp("Fire2"))
        {
            float speed = UnityEngine.Mathf.Lerp(passSpeed, shootSpeed, ShotChargeRatio);
            _cup.BallInCup.Release(GetAimDirection() * speed);
            AudioManager.Instance?.PlaySFX(AudioManager.SFXType.Shoot,
                _stickInput.stickHeadSocket.position);
            OnShotFired?.Invoke();
            _isCharging     = false;
            ShotChargeRatio = 0f;
        }
    }

    // ── Velocity-based shot detection ─────────────────────────────────────────

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

    // ── AI methods ────────────────────────────────────────────────────────────

    /// <summary>Programmatically launch the ball toward a world position (AI use).</summary>
    public void AIRelease(Vector3 targetPosition, float speed)
    {
        if (!HasBall) return;
        _cup.BallInCup.LaunchToward(targetPosition, speed);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private UnityEngine.Vector3 GetAimDirection()
    {
        if (playerCamera == null || _stickInput == null)
            return transform.forward;

        var ray = playerCamera.ViewportPointToRay(
            new UnityEngine.Vector3(0.5f, 0.5f, 0f));

        UnityEngine.Vector3 target =
            UnityEngine.Physics.Raycast(ray, out UnityEngine.RaycastHit hit, 60f)
            ? hit.point
            : ray.GetPoint(60f);

        return (target - _stickInput.stickHeadSocket.position).normalized;
    }
}
