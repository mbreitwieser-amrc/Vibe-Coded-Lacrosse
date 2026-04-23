using UnityEngine;

/// <summary>
/// Lacrosse movement system. Unity 6000.x.
///
/// Body rotation is owned here via _bodyYaw. FirstPersonCamera can call
/// SetBodyYaw() in LateUpdate to drag the body when the player looks beyond
/// headFreedom degrees. Movement wish direction is always camera-relative.
///
/// Controls:
///   WASD          — move; body auto-turns to face movement direction
///   Left Shift    — sprint (rebindable: sprintKey)
///   Left Ctrl     — freeze body auto-turn / defensive stance (strafeKey)
///                   Body holds its facing while you shuffle laterally.
///   Left Alt      — hard brake / plant (brakeKey)
///   Double-tap WASD — dodge burst
///   Space         — jump
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Speeds")]
    public float walkSpeed   = 5f;
    public float sprintSpeed = 9f;
    [Tooltip("Speed while strafe-lock is held (Ctrl). Slower but highly responsive.")]
    public float strafeSpeed = 3.8f;

    [Header("Acceleration")]
    public float acceleration  = 18f;
    public float sprintAccel   = 26f;
    public float strafeAccel   = 28f;
    public float deceleration  = 14f;
    public float brakeDecel    = 50f;
    [Tooltip("Deceleration multiplier when input opposes current velocity >90° (outside-foot plant).")]
    public float plantBoost    = 2.6f;

    [Header("Body Rotation")]
    [Tooltip("Speed (deg/s) body auto-turns to face movement direction.")]
    public float bodyTurnSpeed = 480f;

    [Header("Dodge")]
    public float dodgeSpeed      = 13f;
    public float dodgeDuration   = 0.22f;
    public float dodgeCooldown   = 0.85f;
    public float doubleTapWindow = 0.26f;

    [Header("Stamina")]
    public float maxStamina       = 100f;
    public float sprintDrain      = 22f;
    public float strafeDrain      = 7f;
    public float staminaRegen     = 14f;
    public float dodgeStaminaCost = 22f;
    public float sprintMinStamina = 12f;

    [Header("Jump")]
    public float jumpHeight = 1.4f;
    public float gravity    = -20f;

    [Header("Ground Detection")]
    public Transform groundCheck;
    public float     groundCheckRadius = 0.25f;
    public LayerMask groundMask;

    [Header("Key Bindings")]
    public KeyCode sprintKey = KeyCode.LeftShift;
    [Tooltip("Hold to freeze body auto-turn (defensive shuffle / strafe lock).")]
    public KeyCode strafeKey = KeyCode.LeftControl;
    public KeyCode brakeKey  = KeyCode.LeftAlt;

    [Header("References")]
    public Transform cameraTransform;
    public Animator  animator;

    // ── Public state ──────────────────────────────────────────────────────────
    public bool  IsGrounded   { get; private set; }
    public bool  IsSprinting  { get; private set; }
    public bool  IsStrafing   { get; private set; }
    public bool  IsBraking    { get; private set; }
    public bool  IsDodging    { get; private set; }
    public float Stamina      { get; private set; }
    public float StaminaRatio => Stamina / maxStamina;
    public float MoveSpeed    { get; private set; }
    /// <summary>Current body yaw in world degrees. Read by FirstPersonCamera.</summary>
    public float BodyYaw      { get; private set; }

    public event System.Action<Vector3> OnDodge;

    // ── Animator hashes ───────────────────────────────────────────────────────
    private static readonly int _hashSpeed     = Animator.StringToHash("Speed");
    private static readonly int _hashGrounded  = Animator.StringToHash("IsGrounded");
    private static readonly int _hashSprinting = Animator.StringToHash("IsSprinting");
    private static readonly int _hashStrafing  = Animator.StringToHash("IsStrafing");
    private static readonly int _hashDodging   = Animator.StringToHash("IsDodging");
    private static readonly int _hashJump      = Animator.StringToHash("Jump");

    // ── Private fields ────────────────────────────────────────────────────────
    private CharacterController _controller;
    private Vector3 _horizontalVelocity;
    private float   _verticalVelocity;
    private float   _bodyYaw;

    private float _dodgeTimer;
    private float _dodgeCooldownTimer;
    private float _lastTapW, _lastTapA, _lastTapS, _lastTapD;
    private bool  _prevW, _prevA, _prevS, _prevD;
    private bool  _inputEnabled = true;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (groundCheck == null)
        {
            var go = new UnityEngine.GameObject("GroundCheck");
            go.transform.SetParent(transform);
            go.transform.localPosition =
                new Vector3(0f, -(_controller.height * 0.5f + 0.05f), 0f);
            groundCheck = go.transform;
        }

        Stamina  = maxStamina;
        _bodyYaw = transform.eulerAngles.y;
        BodyYaw  = _bodyYaw;
    }

    private void Update()
    {
        CheckGround();
        TickTimers();

        if (_inputEnabled)
        {
            DetectDoubleTap();
            HandleHorizontalMovement();
            HandleJump();
        }
        else
        {
            ApplyFriction(brakeDecel);
        }

        ApplyGravity();
        CommitHorizontalVelocity();
        UpdateStamina();
        UpdateAnimator();
    }

    // ── Public API — called by FirstPersonCamera.LateUpdate to drag body ──────

    /// <summary>
    /// Sets body yaw and immediately applies it to transform.rotation.
    /// Called by FirstPersonCamera when the camera exceeds head-freedom limit.
    /// LateUpdate wins over Update's write — this is intentional.
    /// </summary>
    public void SetBodyYaw(float yaw)
    {
        _bodyYaw = yaw;
        BodyYaw  = yaw;
        transform.rotation = Quaternion.Euler(0f, _bodyYaw, 0f);
    }

    // ── Ground check ──────────────────────────────────────────────────────────

    private void CheckGround()
    {
        IsGrounded = Physics.CheckSphere(
            groundCheck.position, groundCheckRadius,
            groundMask, QueryTriggerInteraction.Ignore);
    }

    // ── Timers ────────────────────────────────────────────────────────────────

    private void TickTimers()
    {
        if (_dodgeCooldownTimer > 0f) _dodgeCooldownTimer -= Time.deltaTime;
        if (_dodgeTimer > 0f)
        {
            _dodgeTimer -= Time.deltaTime;
            if (_dodgeTimer <= 0f) IsDodging = false;
        }
    }

    // ── Double-tap dodge ──────────────────────────────────────────────────────

    private void DetectDoubleTap()
    {
        if (!IsGrounded || _dodgeCooldownTimer > 0f) return;

        bool w = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        bool a = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
        bool s = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
        bool d = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);

        float t = Time.time;
        if (w && !_prevW) { if (t - _lastTapW < doubleTapWindow) TryDodge(0); _lastTapW = t; }
        if (a && !_prevA) { if (t - _lastTapA < doubleTapWindow) TryDodge(2); _lastTapA = t; }
        if (s && !_prevS) { if (t - _lastTapS < doubleTapWindow) TryDodge(1); _lastTapS = t; }
        if (d && !_prevD) { if (t - _lastTapD < doubleTapWindow) TryDodge(3); _lastTapD = t; }

        _prevW = w; _prevA = a; _prevS = s; _prevD = d;
    }

    private void TryDodge(int dir)
    {
        if (Stamina < dodgeStaminaCost) return;

        Vector3 dodgeDir = dir switch
        {
            0 =>  GetCameraForward(),
            1 => -GetCameraForward(),
            2 => -GetCameraRight(),
            3 =>  GetCameraRight(),
            _ =>  GetCameraForward()
        };

        _horizontalVelocity = dodgeDir * dodgeSpeed;
        _dodgeTimer         = dodgeDuration;
        _dodgeCooldownTimer = dodgeCooldown;
        IsDodging           = true;
        Stamina             = Mathf.Max(Stamina - dodgeStaminaCost, 0f);
        OnDodge?.Invoke(dodgeDir);
    }

    // ── Horizontal movement ───────────────────────────────────────────────────

    private void HandleHorizontalMovement()
    {
        // During dodge burst: preserve velocity, skip all other movement
        if (IsDodging) return;

        float h        = Input.GetAxisRaw("Horizontal");
        float v        = Input.GetAxisRaw("Vertical");
        bool  hasInput = (Mathf.Abs(h) + Mathf.Abs(v)) > 0.1f;

        IsBraking  = Input.GetKey(brakeKey);
        IsStrafing = Input.GetKey(strafeKey);

        bool wantSprint = Input.GetKey(sprintKey) && hasInput && !IsStrafing && !IsBraking;
        if (wantSprint && Stamina <= 0f)                          wantSprint = false;
        if (wantSprint && !IsSprinting && Stamina < sprintMinStamina) wantSprint = false;
        IsSprinting = wantSprint;

        Vector3 wishDir = Vector3.zero;
        if (hasInput)
            wishDir = (GetCameraForward() * v + GetCameraRight() * h).normalized;

        if (IsBraking)
        {
            ApplyFriction(brakeDecel);
        }
        else if (!hasInput)
        {
            ApplyFriction(deceleration);
        }
        else
        {
            float targetSpeed    = IsSprinting ? sprintSpeed
                                 : IsStrafing  ? strafeSpeed
                                 :               walkSpeed;
            float baseAccel      = IsSprinting ? sprintAccel
                                 : IsStrafing  ? strafeAccel
                                 :               acceleration;
            float dot            = _horizontalVelocity.sqrMagnitude > 0.01f
                                 ? Vector3.Dot(_horizontalVelocity.normalized, wishDir)
                                 : 1f;
            float effectiveAccel = baseAccel * (dot < -0.3f ? plantBoost : 1f);

            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity, wishDir * targetSpeed,
                effectiveAccel * Time.deltaTime);
        }

        // Auto-turn body to face movement direction.
        // Strafe lock (Ctrl): freeze body turn — body holds facing for defenders.
        if (hasInput && !IsBraking && !IsStrafing && wishDir.sqrMagnitude > 0.01f)
        {
            float targetYaw = Mathf.Atan2(wishDir.x, wishDir.z) * Mathf.Rad2Deg;
            _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, targetYaw, bodyTurnSpeed * Time.deltaTime);
        }

        BodyYaw            = _bodyYaw;
        transform.rotation = Quaternion.Euler(0f, _bodyYaw, 0f);
        MoveSpeed          = _horizontalVelocity.magnitude;
    }

    private void ApplyFriction(float rate)
    {
        _horizontalVelocity = Vector3.MoveTowards(
            _horizontalVelocity, Vector3.zero, rate * Time.deltaTime);
    }

    private void CommitHorizontalVelocity()
    {
        if (_horizontalVelocity.sqrMagnitude > 0.0001f)
            _controller.Move(_horizontalVelocity * Time.deltaTime);
    }

    // ── Jump ─────────────────────────────────────────────────────────────────

    private void HandleJump()
    {
        if (IsGrounded && Input.GetButtonDown("Jump"))
        {
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            animator?.SetTrigger(_hashJump);
        }
    }

    // ── Gravity ──────────────────────────────────────────────────────────────

    private void ApplyGravity()
    {
        if (IsGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
        _verticalVelocity += gravity * Time.deltaTime;
        _controller.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
    }

    // ── Stamina ───────────────────────────────────────────────────────────────

    private void UpdateStamina()
    {
        if      (IsSprinting) Stamina -= sprintDrain  * Time.deltaTime;
        else if (IsStrafing)  Stamina -= strafeDrain  * Time.deltaTime;
        else                  Stamina += staminaRegen * Time.deltaTime;
        Stamina = Mathf.Clamp(Stamina, 0f, maxStamina);
    }

    // ── Animator ─────────────────────────────────────────────────────────────

    private void UpdateAnimator()
    {
        if (animator == null) return;
        animator.SetFloat(_hashSpeed,     MoveSpeed);
        animator.SetBool (_hashGrounded,  IsGrounded);
        animator.SetBool (_hashSprinting, IsSprinting);
        animator.SetBool (_hashStrafing,  IsStrafing);
        animator.SetBool (_hashDodging,   IsDodging);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Vector3 GetCameraForward()
    {
        Vector3 f = cameraTransform != null ? cameraTransform.forward : transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.001f ? f.normalized : transform.forward;
    }

    private Vector3 GetCameraRight()
    {
        Vector3 r = cameraTransform != null ? cameraTransform.right : transform.right;
        r.y = 0f;
        return r.sqrMagnitude > 0.001f ? r.normalized : transform.right;
    }

    public void SetInputEnabled(bool enabled) => _inputEnabled = enabled;

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
        Gizmos.color = IsDodging ? Color.magenta : Color.yellow;
        Gizmos.DrawRay(transform.position + Vector3.up * 0.5f, _horizontalVelocity * 0.25f);
    }
}
