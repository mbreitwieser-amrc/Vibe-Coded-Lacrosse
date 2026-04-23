using UnityEngine;

/// <summary>
/// Handles stick interactions: cradling, passing, shooting, and scooping.
/// Attach to the player GameObject alongside PlayerController.
/// </summary>
public class StickController : MonoBehaviour
{
    [Header("References")]
    public Transform stickHeadSocket;   // Empty GameObject at the stick pocket position
    public Transform shootOrigin;       // Where the ball launches from
    public Camera playerCamera;

    [Header("Passing")]
    public float passSpeed = 15f;

    [Header("Shooting")]
    public float shootSpeed = 30f;
    public float shootChargeTime = 0.5f;  // Hold shoot button to max power

    [Header("Scoop")]
    public float scoopRadius = 0.6f;
    public LayerMask ballLayer;

    // State
    public bool HasBall => _carriedBall != null;

    private BallController _carriedBall;
    private float _shootChargeStart;
    private bool _isChargingShot;

    private void Update()
    {
        HandlePickup();
        HandlePass();
        HandleShoot();
    }

    // ── Scoop / Pickup ───────────────────────────────────────────────────────

    private void HandlePickup()
    {
        if (HasBall) return;

        // Auto-scoop nearby loose balls
        Collider[] hits = Physics.OverlapSphere(stickHeadSocket.position, scoopRadius, ballLayer);
        foreach (Collider hit in hits)
        {
            BallController ball = hit.GetComponent<BallController>();
            if (ball != null && !ball.IsCarried)
            {
                ball.Scoop(stickHeadSocket);
                _carriedBall = ball;
                break;
            }
        }
    }

    // ── Passing ──────────────────────────────────────────────────────────────

    private void HandlePass()
    {
        if (!HasBall) return;

        if (Input.GetButtonDown("Fire1"))  // Left Mouse / X button
        {
            Vector3 aimDir = GetAimDirection();
            _carriedBall.Release(aimDir * passSpeed);
            _carriedBall = null;
        }
    }

    // ── Shooting ─────────────────────────────────────────────────────────────

    private void HandleShoot()
    {
        if (!HasBall) return;

        if (Input.GetButtonDown("Fire2"))  // Right Mouse / Circle button
        {
            _isChargingShot = true;
            _shootChargeStart = Time.time;
        }

        if (_isChargingShot && Input.GetButtonUp("Fire2"))
        {
            float chargeRatio = Mathf.Clamp01((Time.time - _shootChargeStart) / shootChargeTime);
            float speed = Mathf.Lerp(passSpeed, shootSpeed, chargeRatio);

            Vector3 aimDir = GetAimDirection();
            _carriedBall.Release(aimDir * speed);
            _carriedBall = null;
            _isChargingShot = false;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Returns a world-space direction from the camera toward the aim point.</summary>
    private Vector3 GetAimDirection()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 targetPoint = Physics.Raycast(ray, out RaycastHit hit, 50f)
            ? hit.point
            : ray.GetPoint(50f);

        return (targetPoint - shootOrigin.position).normalized;
    }

    private void OnDrawGizmosSelected()
    {
        if (stickHeadSocket == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(stickHeadSocket.position, scoopRadius);
    }
}
