using UnityEngine;

/// <summary>
/// Physics-only ball controller for Unity 6000.x.
///
/// The ball is NEVER kinematic — it is always a live Rigidbody.
/// Possession is determined entirely by the CupPhysics trigger on the
/// stick head socket; there is no "attach to carrier" snapping.
///
/// Cradling emerges from physics: the player must keep the cup upright
/// (via scroll wheel roll) or the ball rolls out naturally.
///
/// APIs:
///   SetInCup(bool)    — called by CupPhysics trigger
///   Release(velocity) — force-sets velocity (AI / fallback shoot button)
///   LaunchToward(pos) — convenience launch toward a world position
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public class BallController : MonoBehaviour
{
    [Header("Physics")]
    [Tooltip("Assign a PhysicsMaterial with appropriate bounciness (0.4-0.6).")]
    public PhysicsMaterial ballPhysicsMaterial;

    public float mass          = 0.145f;   // Lacrosse ball ~145 g
    public float linearDamping = 0.08f;    // Low drag — fast outdoor ball
    public float angularDamping = 0.8f;

    // ── Public state ──────────────────────────────────────────────────────────
    public bool IsInCup  { get; private set; }
    public bool IsCarried => IsInCup;  // Alias for legacy/AI compatibility

    // Events
    public event System.Action<BallController> OnEnteredCup;
    public event System.Action<BallController> OnLeftCup;

    // ── Private ───────────────────────────────────────────────────────────────
    private Rigidbody      _rb;
    private SphereCollider _col;

    private void Awake()
    {
        _rb  = GetComponent<Rigidbody>();
        _col = GetComponent<SphereCollider>();

        // Unity 6000.x Rigidbody API
        _rb.mass                   = mass;
        _rb.linearDamping          = linearDamping;
        _rb.angularDamping         = angularDamping;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation          = RigidbodyInterpolation.Interpolate;
        _rb.isKinematic            = false;  // Always physical

        if (ballPhysicsMaterial != null)
            _col.material = ballPhysicsMaterial;

        gameObject.tag = "Ball";
    }

    // ── Called by CupPhysics ──────────────────────────────────────────────────
    public void SetInCup(bool inCup)
    {
        if (IsInCup == inCup) return;
        IsInCup = inCup;
        if (inCup) OnEnteredCup?.Invoke(this);
        else       OnLeftCup?.Invoke(this);
    }

    // ── Launch methods (AI / fallback input) ──────────────────────────────────

    /// <summary>
    /// Sets the ball's velocity directly — used by AI or the fallback shoot button.
    /// In physics-based play the ball launches naturally when the cup flicks forward.
    /// </summary>
    public void Release(Vector3 launchVelocity)
    {
        _rb.linearVelocity = launchVelocity;
    }

    /// <summary>Launches ball toward a world-space position at the given speed.</summary>
    public void LaunchToward(Vector3 targetPos, float speed)
    {
        Vector3 dir = (targetPos - transform.position).normalized;
        _rb.linearVelocity = dir * speed;
    }

    // ── Collision feedback ────────────────────────────────────────────────────
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.relativeVelocity.magnitude > 2f)
            AudioManager.Instance?.PlaySFX(AudioManager.SFXType.BallImpact, transform.position);
    }
}
