using UnityEngine;

/// <summary>
/// First-person camera for Vibe Coded Lacrosse. Attach to the Camera GameObject.
///
/// Default state   : camera fixed at eye level, pitched down by defaultDownTilt degrees.
///                   Yaw follows the player body (PlayerController drives body via movement).
///
/// ballFocusKey  (hold) : camera smoothly rotates to look at the ball; FOV narrows slightly.
/// cameraLookKey (hold) : mouse X/Y rotate the camera instead of moving the stick.
///
/// Runs in LateUpdate so it always wins the final body rotation after PlayerController.Update.
/// Exposes IsLookModeActive — StickInputController reads this and suppresses stick mouse input.
/// Owns Cursor.lockState for the whole game (remove cursor lock calls from other scripts).
///
/// All key bindings, FOV, down-tilt angle, sensitivity, and pitch limits are Inspector-exposed.
/// Unity 6000.x.
/// </summary>
public class FirstPersonCamera : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The player root Transform. Camera is parented to this in world space.")]
    public Transform      playerBody;
    [Tooltip("Auto-found if left empty.")]
    public BallController ball;

    [Header("Eye Position")]
    [Tooltip("Camera height above the player root (feet level).")]
    public float eyeHeight = 1.72f;

    [Header("Default Pose")]
    [Tooltip("Degrees the camera tilts downward at rest.\n0 = level horizon, ~12 = looking at stick head area, 45 = steeply downward.")]
    [Range(0f, 45f)]
    public float defaultDownTilt = 12f;

    [Tooltip("Speed (deg/s) at which pitch drifts back to defaultDownTilt when not in any mode.")]
    public float pitchReturnSpeed = 90f;

    [Header("Field of View")]
    [Range(40f, 120f)]
    public float defaultFov = 80f;

    [Tooltip("FOV while ball-focus key is held (slight zoom helps track the ball).")]
    [Range(30f, 110f)]
    public float focusFov = 72f;

    [Tooltip("Higher values = snappier FOV transitions.")]
    public float fovLerpSpeed = 6f;

    [Header("Look Mode")]
    [Tooltip("Mouse sensitivity when cameraLookKey is held.")]
    public float lookSensitivity = 2.5f;

    [Tooltip("Degrees per second the camera rotates toward the ball when ballFocusKey is held.\nHigher = faster snap, lower = floatier feel.")]
    public float ballFocusRotSpeed = 360f;

    [Header("Pitch Limits (Look Mode)")]
    [Tooltip("Maximum look-up angle (negative pitch).")]
    public float pitchMin = -80f;
    [Tooltip("Maximum look-down angle (positive pitch in Unity convention = down).")]
    public float pitchMax =  80f;

    [Header("Key Bindings")]
    [Tooltip("Hold to smoothly rotate camera toward the ball (left click by default).")]
    public KeyCode ballFocusKey  = KeyCode.Mouse0;   // left click

    [Tooltip("Hold to rotate camera with mouse instead of moving stick (right click by default).")]
    public KeyCode cameraLookKey = KeyCode.Mouse1;   // right click

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>
    /// True while cameraLookKey is held.
    /// StickInputController reads this and suppresses mouse X/Y stick input.
    /// </summary>
    public bool IsLookModeActive { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────

    private Camera _cam;
    private float  _yaw;
    private float  _pitch;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null) _cam = GetComponentInChildren<Camera>(true);

        if (playerBody != null)
            _yaw = playerBody.eulerAngles.y;

        // Start pitched downward (Unity pitch convention: negative = looking down)
        _pitch = -defaultDownTilt;

        if (_cam != null)
            _cam.fieldOfView = defaultFov;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    private void Start()
    {
        if (ball == null)
            ball = FindFirstObjectByType<BallController>();
    }

    // LateUpdate: runs after all Update() calls, so camera always wins the final
    // body rotation that frame (PlayerController.Update set it first for movement).
    private void LateUpdate()
    {
        bool playing = GameManager.Instance == null ||
                       GameManager.Instance.State == GameState.Playing;

        Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !playing;

        if (!playing)
        {
            IsLookModeActive = false;
            return;
        }

        IsLookModeActive = Input.GetKey(cameraLookKey);
        bool ballFocus   = Input.GetKey(ballFocusKey) && ball != null;

        if (IsLookModeActive)
            HandleLookMode();
        else if (ballFocus)
            HandleBallFocus();
        else
            HandleDefaultMode();

        ApplyPose();
        UpdateFov(ballFocus);
    }

    // ── Modes ─────────────────────────────────────────────────────────────────

    // Right-click held: mouse X/Y rotate camera (and body yaw, so movement stays aligned).
    private void HandleLookMode()
    {
        float mx = Input.GetAxis("Mouse X") * lookSensitivity;
        float my = Input.GetAxis("Mouse Y") * lookSensitivity;

        _yaw   += mx;
        _pitch  = Mathf.Clamp(_pitch - my, pitchMin, pitchMax);
        // Note: Unity mouse Y is inverted relative to screen — subtract to feel natural.
    }

    // Left-click held: camera smoothly rotates to look at the ball.
    private void HandleBallFocus()
    {
        if (ball == null || playerBody == null) return;

        Vector3 eyePos = playerBody.position + Vector3.up * eyeHeight;
        Vector3 toBall = ball.transform.position - eyePos;
        if (toBall.sqrMagnitude < 0.0001f) return;

        Quaternion current   = Quaternion.Euler(_pitch, _yaw, 0f);
        Quaternion targetRot = Quaternion.LookRotation(toBall.normalized, Vector3.up);
        Quaternion smoothed  = Quaternion.RotateTowards(
                                   current, targetRot,
                                   ballFocusRotSpeed * Time.deltaTime);

        // Decompose euler angles, normalising from Unity's 0-360 to -180–180
        Vector3 e = smoothed.eulerAngles;
        _pitch = Mathf.Clamp(e.x > 180f ? e.x - 360f : e.x, pitchMin, pitchMax);
        _yaw   = e.y;
    }

    // No key held: follow player body yaw, drift pitch back to default down-tilt.
    private void HandleDefaultMode()
    {
        if (playerBody != null)
            _yaw = playerBody.eulerAngles.y;

        _pitch = Mathf.MoveTowards(
                     _pitch, -defaultDownTilt,
                     pitchReturnSpeed * Time.deltaTime);
    }

    // ── Apply pose ────────────────────────────────────────────────────────────

    private void ApplyPose()
    {
        if (playerBody == null) return;

        transform.position  = playerBody.position + Vector3.up * eyeHeight;
        transform.rotation  = Quaternion.Euler(_pitch, _yaw, 0f);

        // Keep player body yaw synchronised with camera so movement direction stays correct.
        // PlayerController.HandleMovement() reads cameraTransform.eulerAngles.y for move dir,
        // but also rotates the body — this overwrite in LateUpdate keeps everything aligned.
        playerBody.rotation = Quaternion.Euler(0f, _yaw, 0f);
    }

    // ── FOV ───────────────────────────────────────────────────────────────────

    private void UpdateFov(bool focusing)
    {
        if (_cam == null) return;
        float target = focusing ? focusFov : defaultFov;
        _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, target, fovLerpSpeed * Time.deltaTime);
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (playerBody == null) return;
        Vector3 eyePos = playerBody.position + Vector3.up * eyeHeight;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(eyePos, 0.05f);
        Gizmos.DrawRay(eyePos, transform.forward * 0.5f);
    }
}
