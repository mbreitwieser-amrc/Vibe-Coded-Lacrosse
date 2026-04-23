using UnityEngine;

/// <summary>
/// Attach to the StickHeadSocket GameObject alongside a SphereCollider (trigger).
///
/// When the ball enters the cup trigger, a settling force pulls it toward the
/// cup centre — but only while the cup is upright. As the cup tilts (player
/// stops cradling), the force fades and the ball rolls out naturally.
///
/// This gives physics-based cradling: the player must oscillate the stick with
/// the scroll wheel to keep the cup face upward, or the ball drops.
///
/// Unity 6000.x — uses Rigidbody.linearVelocity / AddForce.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class CupPhysics : MonoBehaviour
{
    [Header("Settling Force")]
    [Tooltip("Force (N) pulling ball toward cup centre when fully upright.")]
    public float settleForce     = 30f;

    [Tooltip("Velocity damping applied to ball while seated in cup (low-speed only).")]
    public float settleDamping   = 2f;

    [Tooltip("Ball speed below which damping is applied (prevents damping a launched ball).")]
    public float dampingSpeedCap = 4f;

    [Tooltip("Cup uprightness (dot product) below which no force is applied.")]
    public float minUprightness  = 0.05f;

    [Tooltip("Cup uprightness at which full force is applied.")]
    public float fullUprightness = 0.55f;

    [Header("Trigger Size")]
    public float triggerRadius = 0.13f;

    [Header("Debug")]
    public bool showDebug = false;

    // The ball currently inside the cup trigger (null = empty)
    public BallController BallInCup { get; private set; }

    private SphereCollider _trigger;

    private void Awake()
    {
        _trigger           = GetComponent<SphereCollider>();
        _trigger.isTrigger = true;
        _trigger.radius    = triggerRadius;
    }

    private void OnTriggerEnter(Collider other)
    {
        BallController ball = other.GetComponent<BallController>();
        if (ball == null) return;
        BallInCup = ball;
        ball.SetInCup(true);
    }

    private void OnTriggerStay(Collider other)
    {
        if (BallInCup == null) return;
        Rigidbody rb = BallInCup.GetComponent<Rigidbody>();
        if (rb != null) ApplySettleForce(rb);
    }

    private void OnTriggerExit(Collider other)
    {
        BallController ball = other.GetComponent<BallController>();
        if (ball == null || ball != BallInCup) return;
        ball.SetInCup(false);
        BallInCup = null;
    }

    private void ApplySettleForce(Rigidbody rb)
    {
        // Uprightness: 1 = cup opening faces world up, 0 = sideways or down
        float uprightness = Mathf.Clamp01(Vector3.Dot(transform.up, Vector3.up));
        float scale       = Mathf.InverseLerp(minUprightness, fullUprightness, uprightness);

        // Pull ball toward cup centre
        Vector3 toCenter = transform.position - rb.position;
        if (toCenter.sqrMagnitude > 0.0001f)
            rb.AddForce(toCenter.normalized * settleForce * scale, ForceMode.Force);

        // Damp only when ball is moving slowly (don't kill a pass/shot)
        if (rb.linearVelocity.magnitude < dampingSpeedCap)
            rb.AddForce(-rb.linearVelocity * settleDamping * scale, ForceMode.Force);

        if (showDebug)
            Debug.DrawRay(transform.position, transform.up * 0.4f,
                          Color.Lerp(Color.red, Color.green, scale));
    }
}
