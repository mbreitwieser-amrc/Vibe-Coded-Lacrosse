using UnityEngine;

/// <summary>
/// Drives the stick head socket using a grip-anchor sphere model.
///
/// Mouse X/Y   → stick head orbits a sphere anchored to the player's hand grip.
///               Azimuth accumulated in world space so body rotation is independent.
/// Scroll wheel → rotates stick around its own shaft axis (cup face angle).
/// Q            → switch hands; grip anchor mirrors, azimuth reflects.
///
/// The socket has a kinematic Rigidbody so the cup colliders physically push
/// the ball as the stick moves. Unity 6000.x.
/// </summary>
public class StickInputController : MonoBehaviour
{
    [Header("Stick Geometry")]
    public float stickLength      = 1.0f;
    public float gripHeightOffset = 1.1f;   // metres above player feet
    public float gripSideOffset   = 0.28f;  // lateral metres from centre

    [Header("Sensitivity")]
    public float mouseSensX        = 600f;   // degrees/second
    public float mouseSensY        = 400f;
    public float scrollSensitivity = 180f;   // degrees per scroll notch

    [Header("Elevation Limits")]
    public float elevationMin = -75f;
    public float elevationMax =  55f;

    [Header("Roll Limits")]
    public float rollMin = -180f;
    public float rollMax =  180f;

    [Header("Soft Constraints")]
    [Tooltip("Mouse speed multiplier when stick is behind the player.")]
    [Range(0f, 1f)] public float rearResistance     = 0.35f;
    [Tooltip("Mouse speed multiplier when stick is above head height.")]
    [Range(0f, 1f)] public float overheadResistance = 0.50f;

    [Header("Hand Switch")]
    public UnityEngine.KeyCode switchHandKey     = UnityEngine.KeyCode.Q;
    public float               handSwitchLerpSpeed = 8f;
    public bool                isRightHanded    = true;

    [Header("References")]
    public UnityEngine.Transform stickHeadSocket;  // child GO — holds cup colliders
    public UnityEngine.Transform playerBody;       // player root transform

    [Header("Camera")]
    [Tooltip("Assign FirstPersonCamera — mouse input is yielded to camera while in look mode (right-click).")]
    public FirstPersonCamera cameraController;

    // ── Public read-only state ────────────────────────────────────────────────
    public UnityEngine.Vector3    GripAnchor    { get; private set; }
    public UnityEngine.Vector3    StickVelocity { get; private set; }
    public bool                   IsRightHanded => isRightHanded;

    // ── Spherical coordinates (world-space) ───────────────────────────────────
    private float _azimuth;          // degrees, world Y rotation
    private float _elevation = 10f;  // degrees, positive = upward
    private float _roll      =  0f;  // degrees around shaft axis

    // ── Hand switch ───────────────────────────────────────────────────────────
    private float _currentSide;   // lerped: +1 = right, -1 = left
    private float _targetSide;

    // ── Physics socket ────────────────────────────────────────────────────────
    private UnityEngine.Rigidbody  _socketRb;
    private UnityEngine.Vector3    _computedPos;
    private UnityEngine.Quaternion _computedRot;
    private UnityEngine.Vector3    _prevSocketPos;

    // ── Body yaw tracking — keeps stick at same relative angle when body turns ──
    private float _prevBodyYaw;

    private void Awake()
    {
        if (playerBody == null) playerBody = transform;

        if (stickHeadSocket == null)
        {
            var go = new UnityEngine.GameObject("StickHeadSocket");
            go.transform.SetParent(transform);
            stickHeadSocket = go.transform;
        }

        // Kinematic Rigidbody on socket — lets cup colliders physically push the ball
        _socketRb = stickHeadSocket.GetComponent<UnityEngine.Rigidbody>();
        if (_socketRb == null)
            _socketRb = stickHeadSocket.gameObject.AddComponent<UnityEngine.Rigidbody>();
        _socketRb.isKinematic   = true;
        _socketRb.interpolation = UnityEngine.RigidbodyInterpolation.Interpolate;
        _socketRb.useGravity    = false;

        _currentSide = isRightHanded ? 1f : -1f;
        _targetSide  = _currentSide;

        // Init azimuth to player facing so stick starts forward
        _azimuth     = playerBody.eulerAngles.y;
        _prevBodyYaw = playerBody.eulerAngles.y;
        // Note: Cursor lock is managed by FirstPersonCamera.
    }

    private void Update()
    {
        if (GameManager.Instance != null &&
            GameManager.Instance.State != GameState.Playing) return;

        // Carry the stick with the body: apply body yaw delta to world-space azimuth
        // so the stick maintains its relative angle when the player auto-turns.
        float currentBodyYaw = playerBody.eulerAngles.y;
        float bodyYawDelta   = UnityEngine.Mathf.DeltaAngle(_prevBodyYaw, currentBodyYaw);
        _azimuth            += bodyYawDelta;
        _prevBodyYaw         = currentBodyYaw;

        HandleHandSwitch();
        ReadInput();
        ComputeTransform();

        StickVelocity  = (_computedPos - _prevSocketPos) / UnityEngine.Time.deltaTime;
        _prevSocketPos = _computedPos;
    }

    private void FixedUpdate()
    {
        // Drive socket via MovePosition so physics correctly registers contact forces
        _socketRb.MovePosition(_computedPos);
        _socketRb.MoveRotation(_computedRot);
    }

    // ── Hand switch ───────────────────────────────────────────────────────────
    private void HandleHandSwitch()
    {
        if (UnityEngine.Input.GetKeyDown(switchHandKey))
        {
            isRightHanded = !isRightHanded;
            _targetSide   = isRightHanded ? 1f : -1f;

            // Mirror azimuth around player's current facing direction
            float playerYaw = playerBody.eulerAngles.y;
            float relAz     = UnityEngine.Mathf.DeltaAngle(playerYaw, _azimuth);
            _azimuth        = playerYaw + (-relAz);
            _roll           = -_roll;
        }

        _currentSide = UnityEngine.Mathf.Lerp(
            _currentSide, _targetSide,
            UnityEngine.Time.deltaTime * handSwitchLerpSpeed);
    }

    // ── Read input ────────────────────────────────────────────────────────────
    private void ReadInput()
    {
        // Yield mouse X/Y to FirstPersonCamera while it is in look mode (right-click held)
        bool cameraOwnsMouse = cameraController != null && cameraController.IsLookModeActive;

        if (!cameraOwnsMouse)
        {
            float mx = UnityEngine.Input.GetAxis("Mouse X");
            float my = UnityEngine.Input.GetAxis("Mouse Y");

            float constraintMult = ComputeRearFactor() * ComputeOverheadFactor();

            _azimuth   += mx * mouseSensX * UnityEngine.Time.deltaTime * constraintMult;
            _elevation += my * mouseSensY * UnityEngine.Time.deltaTime;
            _elevation  = UnityEngine.Mathf.Clamp(_elevation, elevationMin, elevationMax);
        }

        // Scroll wheel always drives cup roll — independent of look mode
        float sw = UnityEngine.Input.GetAxis("Mouse ScrollWheel");
        _roll += sw * scrollSensitivity;
        _roll  = UnityEngine.Mathf.Clamp(_roll, rollMin, rollMax);
    }

    // ── Compute stick head transform ──────────────────────────────────────────
    private void ComputeTransform()
    {
        // Grip anchor: player body + side offset in local space + height
        GripAnchor = playerBody.position
            + playerBody.right * (_currentSide * gripSideOffset)
            + UnityEngine.Vector3.up * gripHeightOffset;

        // World-space shaft direction from spherical coords
        var sphereRot  = UnityEngine.Quaternion.Euler(-_elevation, _azimuth, 0f);
        var shaftDir   = sphereRot * UnityEngine.Vector3.forward;

        _computedPos = GripAnchor + shaftDir * stickLength;

        // Cup opening direction is perpendicular to shaft, influenced by roll
        var refUp = UnityEngine.Mathf.Abs(
                        UnityEngine.Vector3.Dot(shaftDir, UnityEngine.Vector3.up)) > 0.99f
                    ? playerBody.forward
                    : UnityEngine.Vector3.up;

        var baseRight = UnityEngine.Vector3.Cross(refUp, shaftDir).normalized;
        var baseUp    = UnityEngine.Vector3.Cross(shaftDir, baseRight).normalized;

        var baseRot  = UnityEngine.Quaternion.LookRotation(shaftDir, baseUp);
        var rollRot  = UnityEngine.Quaternion.AngleAxis(_roll, UnityEngine.Vector3.forward);
        _computedRot = baseRot * rollRot;
    }

    // ── Soft constraints ──────────────────────────────────────────────────────
    private float ComputeRearFactor()
    {
        float relAz   = UnityEngine.Mathf.Abs(
                            UnityEngine.Mathf.DeltaAngle(playerBody.eulerAngles.y, _azimuth));
        float rearness = UnityEngine.Mathf.InverseLerp(80f, 160f, relAz);
        return UnityEngine.Mathf.Lerp(1f, rearResistance, rearness);
    }

    private float ComputeOverheadFactor()
    {
        float ov = UnityEngine.Mathf.InverseLerp(25f, elevationMax, _elevation);
        return UnityEngine.Mathf.Lerp(1f, overheadResistance, ov);
    }

    // ── Debug gizmos ──────────────────────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        if (stickHeadSocket == null) return;
        UnityEngine.Gizmos.color = UnityEngine.Color.green;
        UnityEngine.Gizmos.DrawWireSphere(GripAnchor, 0.06f);
        UnityEngine.Gizmos.color = UnityEngine.Color.yellow;
        UnityEngine.Gizmos.DrawLine(GripAnchor, stickHeadSocket.position);
        UnityEngine.Gizmos.color = UnityEngine.Color.cyan;
        UnityEngine.Gizmos.DrawRay(stickHeadSocket.position, stickHeadSocket.up * 0.25f);
    }
}
