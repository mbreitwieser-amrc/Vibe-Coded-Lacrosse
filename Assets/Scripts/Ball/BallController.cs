using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BallController : MonoBehaviour
{
    [Header("Physics")]
    public float bounciness = 0.5f;
    public PhysicMaterial ballPhysicMaterial;

    [Header("State")]
    public bool IsCarried { get; private set; }

    private Rigidbody _rb;
    private Transform _carrierSocket;  // The stick-head socket on the carrying player

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void FixedUpdate()
    {
        if (IsCarried && _carrierSocket != null)
        {
            // Snap ball to stick pocket each physics step
            _rb.MovePosition(_carrierSocket.position);
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>Called by StickController when a player picks up / catches the ball.</summary>
    public void AttachToCarrier(Transform stickSocket)
    {
        IsCarried = true;
        _carrierSocket = stickSocket;
        _rb.isKinematic = true;
    }

    /// <summary>Called by StickController when passing or shooting.</summary>
    public void Release(Vector3 launchVelocity)
    {
        IsCarried = false;
        _carrierSocket = null;
        _rb.isKinematic = false;
        _rb.linearVelocity = launchVelocity;
    }

    /// <summary>Called by StickController when scooping a ground ball.</summary>
    public void Scoop(Transform stickSocket)
    {
        AttachToCarrier(stickSocket);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsCarried) return;
        // Hook for sound effects / particles on impact
        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed > 2f)
        {
            // AudioManager.Instance?.PlayBallImpact(transform.position);
        }
    }
}
