using UnityEngine;

/// <summary>
/// Controls ball physics and carry/release state.
/// Unity 6000.x: uses linearVelocity, linearDamping, PhysicsMaterial.
/// Tag this GameObject "Ball".
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public class BallController : MonoBehaviour
{
    [Header("Physics")]
    [Tooltip("Create a PhysicsMaterial with bounciness and assign here.")]
    public PhysicsMaterial ballPhysicsMaterial;
    public float linearDamping  = 0.3f;   // Unity 6: was Rigidbody.drag
    public float angularDamping = 2f;     // Unity 6: was Rigidbody.angularDrag
    public float mass = 0.14f;            // Lacrosse ball ~140 g

    [Header("Cradle")]
    [Tooltip("How much the ball oscillates side-to-side while carried.")]
    public float cradleAmplitude = 0.08f;
    public float cradleFrequency = 3f;

    // ── Public state ─────────────────────────────────────────────────────────
    public bool      IsCarried   { get; private set; }
    public Transform CarrierRoot { get; private set; }  // The carrier's Transform

    // Events
    public event System.Action<BallController> OnPickedUp;
    public event System.Action<BallController> OnReleased;

    // ── Private fields ───────────────────────────────────────────────────────
    private Rigidbody      _rb;
    private SphereCollider _col;
    private Transform      _carrierSocket;
    private float          _cradleTime;

    private void Awake()
    {
        _rb  = GetComponent<Rigidbody>();
        _col = GetComponent<SphereCollider>();

        // Unity 6 Rigidbody properties
        _rb.mass                       = mass;
        _rb.linearDamping              = linearDamping;
        _rb.angularDamping             = angularDamping;
        _rb.collisionDetectionMode     = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation              = RigidbodyInterpolation.Interpolate;

        if (ballPhysicsMaterial != null)
            _col.material = ballPhysicsMaterial;

        gameObject.tag = "Ball";
    }

    private void FixedUpdate()
    {
        if (!IsCarried || _carrierSocket == null) return;

        // Cradle oscillation
        _cradleTime += Time.fixedDeltaTime;
        float sway = Mathf.Sin(_cradleTime * cradleFrequency * Mathf.PI * 2f) * cradleAmplitude;
        Vector3 swayOffset = _carrierSocket.right * sway;

        _rb.MovePosition(_carrierSocket.position + swayOffset);
        _rb.linearVelocity  = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Attaches ball to a carrier’s stick socket (pickup / catch).</summary>
    public void AttachToCarrier(Transform stickSocket, Transform carrierRoot)
    {
        IsCarried      = true;
        _carrierSocket = stickSocket;
        CarrierRoot    = carrierRoot;
        _cradleTime    = 0f;
        _rb.isKinematic = true;
        _col.isTrigger  = false;
        OnPickedUp?.Invoke(this);
    }

    /// <summary>Releases ball with the given world-space velocity (pass / shot).</summary>
    public void Release(Vector3 launchVelocity)
    {
        IsCarried       = false;
        _carrierSocket  = null;
        CarrierRoot     = null;
        _rb.isKinematic = false;
        _rb.linearVelocity = launchVelocity;
        OnReleased?.Invoke(this);
    }

    /// <summary>Convenience overload used for ground-ball scoops.</summary>
    public void Scoop(Transform stickSocket, Transform carrierRoot)
        => AttachToCarrier(stickSocket, carrierRoot);

    // ── Collision feedback ────────────────────────────────────────────────────
    private void OnCollisionEnter(Collision collision)
    {
        if (IsCarried) return;
        float speed = collision.relativeVelocity.magnitude;
        if (speed > 2f)
            AudioManager.Instance?.PlaySFX(AudioManager.SFXType.BallImpact, transform.position);
    }
}
