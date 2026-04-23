using UnityEngine;

/// <summary>
/// First-person camera with body-following, head freedom, and stick-position bias.
/// Unity 6000.x.
///
/// Architecture:
///   - _cameraYaw is the camera's own tracked yaw, independent of body yaw.
///   - Default mode: _cameraYaw drifts quickly back to PlayerController.BodyYaw.
///   - Look mode (cameraLookKey held): _cameraYaw is mouse-driven within
///     ±headFreedom degrees of body. Exceeding the limit drags the body.
///   - Ball focus (ballFocusKey held): camera rotates toward ball, same freedom rules.
///   - Stick bias: camera leans passively toward the stick head socket position.
///     Cradling left shoulder → camera drifts slightly up-left. Purely additive
///     to the final rendered rotation; does not affect _cameraYaw tracking.
///   - Cursor lock owned here.
///
/// Runs in LateUpdate — always wins the final transform over PlayerController.Update.
/// </summary>
public class FirstPersonCamera : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The player root Transform.")]
    public Transform      playerBody;
    [Tooltip("Auto-found if left empty.")]
    public BallController ball;
    [Tooltip("StickHeadSocket Transform for stick-position camera bias. Assign in Inspector.")]
    public Transform      stickHeadSocket;

    [Header("Eye Position")]
    public float eyeHeight = 1.72f;

    [Header("Default Pose")]
    [Tooltip("Degrees camera tilts downward at rest (0 = horizon, 12 = looking at stick area).")]
    [Range(0f, 45f)]
    public float defaultDownTilt   = 12f;
    [Tooltip("Speed (deg/s) pitch returns to defaultDownTilt when no key held.")]
    public float pitchReturnSpeed  = 90f;
    [Tooltip("Speed (deg/s) camera yaw returns to body yaw in default mode.")]
    public float cameraReturnSpeed = 720f;

    [Header("Field of View")]
    [Range(40f, 120f)]
    public float defaultFov    = 80f;
    [Range(30f, 110f)]
    public float focusFov      = 72f;
    public float fovLerpSpeed  = 6f;

    [Header("Head Freedom")]
    [Tooltip("Maximum degrees camera yaw can offset from body yaw before dragging the body.")]
    [Range(0f, 80f)]
    public float headFreedom   = 40f;
    [Tooltip("Speed (deg/s) body is dragged when camera reaches the freedom limit.")]
    public float bodyDragSpeed = 360f;

    [Header("Look Mode (right-click)")]
    public float lookSensitivity   = 2.5f;
    public float ballFocusRotSpeed = 360f;

    [Header("Pitch Limits")]
    public float pitchMin = -80f;
    public float pitchMax =  80f;

    [Header("Stick Bias")]
    [Tooltip("How strongly the camera leans toward the stick head socket.\n0 = no bias, 1 = full.")]
    [Range(0f, 1f)]
    public float stickBiasStrength    = 0.6f;
    [Tooltip("Maximum horizontal (yaw) offset toward stick (degrees).")]
    public float maxStickBiasYaw      = 15f;
    [Tooltip("Maximum vertical (pitch) offset toward stick (degrees).")]
    public float maxStickBiasV        = 12f;
    [Tooltip("Smoothing speed for stick bias transitions.")]
    public float stickBiasSmoothSpeed = 5f;

    [Header("Key Bindings")]
    public KeyCode ballFocusKey  = KeyCode.Mouse0;
    public KeyCode cameraLookKey = KeyCode.Mouse1;

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>True while cameraLookKey is held. StickInputController reads this.</summary>
    public bool IsLookModeActive { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────

    private Camera           _cam;
    private PlayerController _playerController;
    private float            _cameraYaw;
    private float            _pitch;
    private float            _smoothBiasYaw;
    private float            _smoothBiasV;
    private bool             _ballFocusActive;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null) _cam = GetComponentInChildren<Camera>(true);

        if (playerBody != null)
        {
            _playerController = playerBody.GetComponent<PlayerController>();
            _cameraYaw = playerBody.eulerAngles.y;
        }

        _pitch = -defaultDownTilt;
        if (_cam != null) _cam.fieldOfView = defaultFov;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    private void Start()
    {
        if (ball == null)
            ball = FindFirstObjectByType<BallController>();

        if (_playerController == null && playerBody != null)
            _playerController = playerBody.GetComponent<PlayerController>();
    }

    private void LateUpdate()
    {
        bool playing = GameManager.Instance == null ||
                       GameManager.Instance.State == GameState.Playing;

        Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !playing;

        if (!playing) { IsLookModeActive = false; return; }

        IsLookModeActive = Input.GetKey(cameraLookKey);
        _ballFocusActive = Input.GetKey(ballFocusKey) && ball != null;

        if      (IsLookModeActive) HandleLookMode();
        else if (_ballFocusActive) HandleBallFocus();
        else                       HandleDefaultMode();

        ApplyStickBias();
        ApplyPose();
        UpdateFov(_ballFocusActive);
    }

    // ── Modes ─────────────────────────────────────────────────────────────────

    // Default: camera drifts back to body yaw quickly; pitch returns to tilt.
    private void HandleDefaultMode()
    {
        _cameraYaw = Mathf.MoveTowardsAngle(
            _cameraYaw, GetBodyYaw(), cameraReturnSpeed * Time.deltaTime);
        _pitch = Mathf.MoveTowards(
            _pitch, -defaultDownTilt, pitchReturnSpeed * Time.deltaTime);
    }

    // Right-click: free look within ±headFreedom of body yaw; drags body at limit.
    private void HandleLookMode()
    {
        float mx = Input.GetAxis("Mouse X") * lookSensitivity;
        float my = Input.GetAxis("Mouse Y") * lookSensitivity;
        _cameraYaw += mx;
        _pitch      = Mathf.Clamp(_pitch - my, pitchMin, pitchMax);
        EnforceHeadFreedom();
    }

    // Left-click: smoothly rotates camera toward ball; drags body at freedom limit.
    private void HandleBallFocus()
    {
        if (ball == null || playerBody == null) return;

        Vector3 eyePos = playerBody.position + Vector3.up * eyeHeight;
        Vector3 toBall = ball.transform.position - eyePos;
        if (toBall.sqrMagnitude < 0.0001f) return;

        Quaternion current   = Quaternion.Euler(_pitch, _cameraYaw, 0f);
        Quaternion targetRot = Quaternion.LookRotation(toBall.normalized, Vector3.up);
        Quaternion smoothed  = Quaternion.RotateTowards(
                                   current, targetRot, ballFocusRotSpeed * Time.deltaTime);

        Vector3 e = smoothed.eulerAngles;
        _pitch     = Mathf.Clamp(e.x > 180f ? e.x - 360f : e.x, pitchMin, pitchMax);
        _cameraYaw = e.y;

        EnforceHeadFreedom();
    }

    // Clamps _cameraYaw within ±headFreedom of body yaw.
    // When the limit is reached, drags the body toward the camera direction.
    private void EnforceHeadFreedom()
    {
        float bodyYaw = GetBodyYaw();
        float offset  = Mathf.DeltaAngle(bodyYaw, _cameraYaw);

        if (Mathf.Abs(offset) > headFreedom)
        {
            _cameraYaw = bodyYaw + Mathf.Clamp(offset, -headFreedom, headFreedom);

            if (_playerController != null)
            {
                float newBodyYaw = Mathf.MoveTowardsAngle(
                    bodyYaw, _cameraYaw, bodyDragSpeed * Time.deltaTime);
                _playerController.SetBodyYaw(newBodyYaw);
            }
        }
    }

    // ── Stick bias ────────────────────────────────────────────────────────────

    private void ApplyStickBias()
    {
        Vector2 target = ComputeStickBias();
        _smoothBiasYaw = Mathf.Lerp(_smoothBiasYaw, target.x, stickBiasSmoothSpeed * Time.deltaTime);
        _smoothBiasV   = Mathf.Lerp(_smoothBiasV,   target.y, stickBiasSmoothSpeed * Time.deltaTime);
    }

    private Vector2 ComputeStickBias()
    {
        if (stickHeadSocket == null || playerBody == null || stickBiasStrength < 0.001f)
            return Vector2.zero;

        Vector3 eyePos  = playerBody.position + Vector3.up * eyeHeight;
        Vector3 toStick = stickHeadSocket.position - eyePos;
        if (toStick.sqrMagnitude < 0.0001f) return Vector2.zero;

        Vector3 dir = toStick.normalized;

        // Camera right at current yaw (world-horizontal only)
        Vector3 camRight = Quaternion.Euler(0f, _cameraYaw, 0f) * Vector3.right;

        // Horizontal component: how far left/right the stick is relative to camera
        float horiz = Vector3.Dot(dir, camRight);
        // Vertical component: how far above/below eye level
        float vert  = dir.y;

        float biasYaw = Mathf.Asin(Mathf.Clamp(horiz, -1f, 1f)) * Mathf.Rad2Deg;
        float biasV   = Mathf.Asin(Mathf.Clamp(vert,  -1f, 1f)) * Mathf.Rad2Deg;
        // Negative biasV: stick above eye = look up = negative pitch in Unity convention
        biasV = -biasV;

        biasYaw = Mathf.Clamp(biasYaw * stickBiasStrength, -maxStickBiasYaw, maxStickBiasYaw);
        biasV   = Mathf.Clamp(biasV   * stickBiasStrength, -maxStickBiasV,   maxStickBiasV);

        return new Vector2(biasYaw, biasV);
    }

    // ── Apply pose ────────────────────────────────────────────────────────────

    private void ApplyPose()
    {
        if (playerBody == null) return;

        transform.position = playerBody.position + Vector3.up * eyeHeight;
        // Bias is additive to the rendered rotation only — does not affect _cameraYaw
        transform.rotation = Quaternion.Euler(
            _pitch     + _smoothBiasV,
            _cameraYaw + _smoothBiasYaw,
            0f);
    }

    // ── FOV ───────────────────────────────────────────────────────────────────

    private void UpdateFov(bool focusing)
    {
        if (_cam == null) return;
        float target = focusing ? focusFov : defaultFov;
        _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, target, fovLerpSpeed * Time.deltaTime);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private float GetBodyYaw()
    {
        if (_playerController != null) return _playerController.BodyYaw;
        return playerBody != null ? playerBody.eulerAngles.y : _cameraYaw;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (playerBody == null) return;
        Vector3 eyePos = playerBody.position + Vector3.up * eyeHeight;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(eyePos, 0.05f);
        Gizmos.DrawRay(eyePos, transform.forward * 0.5f);
        if (stickHeadSocket != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(eyePos, stickHeadSocket.position);
        }
    }
}
