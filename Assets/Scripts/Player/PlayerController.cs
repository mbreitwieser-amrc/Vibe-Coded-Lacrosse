using UnityEngine;

/// <summary>
/// Lacrosse movement system — ground-up rebuild. Unity 6000.x.
///
/// States
/// ──────
/// Idle          No WASD; velocity decays to zero along a decel curve.
/// Running       WASD; body auto-turns toward wish direction; asymptotic accel to runSpeed.
/// Backpedaling  S only (not crouching); body locked to camera forward; velocity goes backward.
/// Sprinting     Shift + WASD (not crouch); higher accel and top speed; drains stamina.
/// Crouching     Ctrl held; body locked to camera forward; no sprint; lower speed caps:
///                 W   = forward          (crouchRunSpeed)
///                 S   = backpedal        (crouchBackpedalSpeed)
///                 A/D = lateral strafe   (crouchStrafeSpeed)
/// Dead stop     Alt; instant zero velocity.
/// Dodge         Double-tap WASD:
///                 Upright  – same-dir adds impulse (capped at runSpeed);
///                            change-dir overrides velocity (capped at runSpeed).
///                 Crouching – quick lunge + hard decel (crouchLunge*).
///
/// Velocity shape : a = aBase × (1 – v_projected / vMax)  — asymptotic approach
/// Turn speed     : decreases linearly from maxTurnSpeed (still) → minTurnSpeed (runSpeed)
///                  and further to sprintTurnSpeed at full sprint.
/// Stamina regen  : multiplied by idleRegenMultiplier after idleRegenDelay seconds of standing still.
///
/// DEFERRED (todo):
///   - Airborne movement (jump + air-directional control)
///   - Stamina calibration (currently maxStamina = 9999 for testing)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    // ── Running ───────────────────────────────────────────────────────────────
    [Header("Running")]
    public float runSpeed = 5.5f;
    public float runAccel = 20f;

    // ── Backpedaling ──────────────────────────────────────────────────────────
    [Header("Backpedaling (upright)")]
    public float backpedalSpeed = 3.2f;
    public float backpedalAccel = 16f;

    // ── Sprinting ─────────────────────────────────────────────────────────────
    [Header("Sprinting")]
    public float sprintSpeed     = 9f;
    public float sprintAccel     = 32f;
    [Tooltip("Rate (m/s²) at which speed decays back to runSpeed once sprint is released.")]
    public float sprintBleedRate = 8f;

    // ── Idle deceleration ─────────────────────────────────────────────────────
    [Header("Idle deceleration")]
    public float idleDecel = 14f;

    // ── Crouch stance ─────────────────────────────────────────────────────────
    [Header("Crouch stance")]
    public float crouchRunSpeed       = 3.5f;
    public float crouchBackpedalSpeed = 2.0f;
    public float crouchStrafeSpeed    = 3.0f;
    public float crouchAccel          = 24f;

    // ── Body rotation ─────────────────────────────────────────────────────────
    [Header("Body rotation")]
    [Tooltip("Max turn speed (deg/s) when nearly still.")]
    public float maxTurnSpeed    = 540f;
    [Tooltip("Min turn speed (deg/s) at full runSpeed.")]
    public float minTurnSpeed    = 100f;
    [Tooltip("Turn speed (deg/s) at full sprintSpeed.")]
    public float sprintTurnSpeed = 50f;

    // ── Dodge – upright ───────────────────────────────────────────────────────
    [Header("Dodge — upright")]
    [Tooltip("Velocity added when dodging in the same direction as current travel.")]
    public float dodgeAddSpeed     = 2.5f;
    [Tooltip("Velocity magnitude when dodging in a new direction (override).")]
    public float dodgeOverrideSpeed = 5.0f;
    [Tooltip("How long the IsDodging flag stays true for the animator (upright).")]
    public float dodgeAnimDuration  = 0.18f;

    // ── Dodge – crouch lunge ──────────────────────────────────────────────────
    [Header("Dodge — crouch lunge")]
    public float crouchLungeSpeed    = 3.8f;
    public float crouchLungeDuration = 0.18f;
    public float crouchLungeDecel    = 45f;

    // ── Dodge – shared ────────────────────────────────────────────────────────
    [Header("Dodge — shared")]
    public float dodgeCooldown    = 0.75f;
    public float doubleTapWindow  = 0.25f;
    public float dodgeStaminaCost = 30f;

    // ── Stamina ───────────────────────────────────────────────────────────────
    [Header("Stamina")]
    [Tooltip("Very large for testing. Calibration is deferred.")]
    public float maxStamina          = 9999f;
    public float sprintStaminaDrain  = 25f;
    public float staminaRegen        = 30f;
    [Tooltip("Regen multiplier after standing still for idleRegenDelay seconds.")]
    public float idleRegenMultiplier = 3f;
    public float idleRegenDelay      = 0.5f;

    // ── Ground detection ──────────────────────────────────────────────────────
    [Header("Ground detection")]
    public Transform groundCheck;
    public float     groundCheckRadius = 0.25f;
    public LayerMask groundMask;

    // ── Gravity ───────────────────────────────────────────────────────────────
    [Header("Gravity")]
    public float gravity = -20f;

    // ── Key bindings ──────────────────────────────────────────────────────────
    [Header("Key bindings")]
    public KeyCode sprintKey = KeyCode.LeftShift;
    public KeyCode crouchKey = KeyCode.LeftControl;
    public KeyCode brakeKey  = KeyCode.LeftAlt;

    // ── References ────────────────────────────────────────────────────────────
    [Header("References")]
    public Transform cameraTransform;
    public Animator  animator;

    // ── Public state ──────────────────────────────────────────────────────────
    public bool  IsGrounded     { get; private set; }
    public bool  IsSprinting    { get; private set; }
    public bool  IsCrouching    { get; private set; }
    public bool  IsBackpedaling { get; private set; }
    public bool  IsBraking      { get; private set; }
    public bool  IsDodging      { get; private set; }
    public float Stamina        { get; private set; }
    public float StaminaRatio   => Stamina / maxStamina;
    public float MoveSpeed      { get; private set; }
    /// <summary>Current body yaw in world degrees. Read by FirstPersonCamera.</summary>
    public float BodyYaw        { get; private set; }

    public event System.Action<Vector3> OnDodge;

    // ── Animator hashes ───────────────────────────────────────────────────────
    private static readonly int _hashSpeed        = Animator.StringToHash("Speed");
    private static readonly int _hashGrounded     = Animator.StringToHash("IsGrounded");
    private static readonly int _hashSprinting    = Animator.StringToHash("IsSprinting");
    private static readonly int _hashCrouching    = Animator.StringToHash("IsCrouching");
    private static readonly int _hashBackpedaling = Animator.StringToHash("IsBackpedaling");
    private static readonly int _hashDodging      = Animator.StringToHash("IsDodging");

    // ── Private fields ────────────────────────────────────────────────────────
    private CharacterController _controller;
    private Vector3 _horizontalVelocity;
    private float   _verticalVelocity;
    private float   _bodyYaw;
    private bool    _inputEnabled = true;

    // Dodge state
    private float _dodgeCooldownTimer;
    private float _dodgeAnimTimer;   // drives IsDodging for upright dodge
    private bool  _isLunging;        // true during crouch lunge decel phase
    private float _lungeTimer;

    // Double-tap detection
    private float _lastTapW, _lastTapA, _lastTapS, _lastTapD;
    private bool  _prevW, _prevA, _prevS, _prevD;

    // Stamina idle tracking
    private float _idleTimer;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (groundCheck == null)
        {
            var go = new UnityEngine.GameObject("GroundCheck");
            go.transform.SetParent(transform);
            // Must account for CharacterController.center so the check lands just
            // below the capsule bottom, not 1 m below the root pivot.
            go.transform.localPosition = new Vector3(
                0f,
                _controller.center.y - _controller.height * 0.5f - 0.05f,
                0f);
            groundCheck = go.transform;
        }

        Stamina  = maxStamina;
        _bodyYaw = transform.eulerAngles.y;
        BodyYaw  = _bodyYaw;
    }

    private void Update()
    {
        TickTimers();
        CheckGround();

        if (_inputEnabled)
        {
            DetectDoubleTap();
            HandleHorizontalMovement();
        }
        else
        {
            Decelerate(idleDecel);
        }

        ApplyGravity();
        CommitVelocity();
        UpdateStamina();
        UpdateAnimator();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets body yaw and immediately applies it to transform.rotation.
    /// Called by FirstPersonCamera when the camera exceeds head-freedom limit.
    /// </summary>
    public void SetBodyYaw(float yaw)
    {
        _bodyYaw = yaw;
        BodyYaw  = yaw;
        transform.rotation = Quaternion.Euler(0f, _bodyYaw, 0f);
    }

    public void SetInputEnabled(bool enabled) => _inputEnabled = enabled;

    // ── Timers ────────────────────────────────────────────────────────────────

    private void TickTimers()
    {
        if (_dodgeCooldownTimer > 0f) _dodgeCooldownTimer -= Time.deltaTime;
        if (_dodgeAnimTimer     > 0f) _dodgeAnimTimer     -= Time.deltaTime;

        if (_isLunging)
        {
            _lungeTimer -= Time.deltaTime;
            if (_lungeTimer <= 0f) _isLunging = false;
        }

        IsDodging = _dodgeAnimTimer > 0f || _isLunging;
    }

    // ── Ground check ──────────────────────────────────────────────────────────

    private void CheckGround()
    {
        IsGrounded = Physics.CheckSphere(
            groundCheck.position, groundCheckRadius,
            groundMask, QueryTriggerInteraction.Ignore);
    }

    // ── Double-tap dodge detection ────────────────────────────────────────────

    private void DetectDoubleTap()
    {
        if (_dodgeCooldownTimer > 0f) return;

        bool w = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        bool a = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
        bool s = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
        bool d = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);

        float t = Time.time;
        if (w && !_prevW) { if (t - _lastTapW < doubleTapWindow) TryDodge( GetBodyForward()); _lastTapW = t; }
        if (a && !_prevA) { if (t - _lastTapA < doubleTapWindow) TryDodge(-GetBodyRight());   _lastTapA = t; }
        if (s && !_prevS) { if (t - _lastTapS < doubleTapWindow) TryDodge(-GetBodyForward()); _lastTapS = t; }
        if (d && !_prevD) { if (t - _lastTapD < doubleTapWindow) TryDodge( GetBodyRight());   _lastTapD = t; }

        _prevW = w; _prevA = a; _prevS = s; _prevD = d;
    }

    private void TryDodge(Vector3 dir)
    {
        if (Stamina < dodgeStaminaCost) return;

        Stamina            -= dodgeStaminaCost;
        _dodgeCooldownTimer = dodgeCooldown;
        _dodgeAnimTimer     = dodgeAnimDuration;

        if (IsCrouching)
        {
            // Lunge: set velocity in direction, then lunge decel brings it back down
            _horizontalVelocity = dir * crouchLungeSpeed;
            _isLunging          = true;
            _lungeTimer         = crouchLungeDuration;
        }
        else
        {
            float dot = _horizontalVelocity.sqrMagnitude > 0.01f
                ? Vector3.Dot(_horizontalVelocity.normalized, dir)
                : 0f;

            if (dot > 0.5f)
            {
                // Same direction: additive impulse, hard cap at runSpeed
                _horizontalVelocity += dir * dodgeAddSpeed;
                if (_horizontalVelocity.magnitude > runSpeed)
                    _horizontalVelocity = _horizontalVelocity.normalized * runSpeed;
            }
            else
            {
                // Direction change: override velocity, hard cap at runSpeed
                _horizontalVelocity = dir * Mathf.Min(dodgeOverrideSpeed, runSpeed);
            }
        }

        OnDodge?.Invoke(dir);
    }

    // ── Main horizontal movement ──────────────────────────────────────────────

    private void HandleHorizontalMovement()
    {
        // During a crouch lunge the player only experiences decel — no steering.
        if (_isLunging)
        {
            Decelerate(crouchLungeDecel);
            WriteBodyTransform();
            MoveSpeed = _horizontalVelocity.magnitude;
            return;
        }

        float h       = Input.GetAxisRaw("Horizontal");
        float v       = Input.GetAxisRaw("Vertical");
        bool hasInput = Mathf.Abs(h) + Mathf.Abs(v) > 0.1f;

        IsBraking   = Input.GetKey(brakeKey);
        IsCrouching = Input.GetKey(crouchKey);

        // ── Dead stop ─────────────────────────────────────────────────────────
        if (IsBraking)
        {
            _horizontalVelocity = Vector3.zero;
            IsSprinting         = false;
            IsBackpedaling      = false;
            MoveSpeed           = 0f;
            WriteBodyTransform();
            return;
        }

        // ── Crouch stance ─────────────────────────────────────────────────────
        if (IsCrouching)
        {
            HandleCrouchMovement(h, v, hasInput);
            WriteBodyTransform();
            MoveSpeed = _horizontalVelocity.magnitude;
            return;
        }

        // ── Upright — no input ────────────────────────────────────────────────
        IsSprinting    = false;
        IsBackpedaling = false;

        if (!hasInput)
        {
            Decelerate(idleDecel);
            WriteBodyTransform();
            MoveSpeed = _horizontalVelocity.magnitude;
            return;
        }

        // ── Upright — backpedaling (pure S, no crouch) ────────────────────────
        if (v < -0.1f && Mathf.Abs(h) < 0.1f)
        {
            HandleBackpedal();
            WriteBodyTransform();
            MoveSpeed = _horizontalVelocity.magnitude;
            return;
        }

        // ── Upright — running / sprinting ─────────────────────────────────────
        HandleRunSprint(h, v);
        WriteBodyTransform();
        MoveSpeed = _horizontalVelocity.magnitude;
    }

    // ── Crouch movement ───────────────────────────────────────────────────────

    private void HandleCrouchMovement(float h, float v, bool hasInput)
    {
        IsSprinting    = false;
        IsBackpedaling = v < -0.1f;

        // Lock body to camera forward continuously while crouching
        _bodyYaw = GetCameraYaw();

        if (!hasInput)
        {
            Decelerate(idleDecel);
            return;
        }

        // Speed cap: determined by the dominant input axis
        float vMax;
        if (Mathf.Abs(v) >= Mathf.Abs(h))
            vMax = v > 0f ? crouchRunSpeed : crouchBackpedalSpeed;
        else
            vMax = crouchStrafeSpeed;

        Vector3 wishDir = (GetCameraForward() * v + GetCameraRight() * h).normalized;
        AccelerateToward(wishDir, vMax, crouchAccel);
    }

    // ── Backpedaling ──────────────────────────────────────────────────────────

    private void HandleBackpedal()
    {
        IsBackpedaling = true;
        IsSprinting    = false;

        // Continuously lock body to camera forward so the player faces their attacker
        _bodyYaw = GetCameraYaw();

        AccelerateToward(-GetCameraForward(), backpedalSpeed, backpedalAccel);
    }

    // ── Running / sprinting ───────────────────────────────────────────────────

    private void HandleRunSprint(float h, float v)
    {
        bool wantSprint = Input.GetKey(sprintKey) && Stamina > 0f;
        IsSprinting = wantSprint;

        // Body-relative axes: W = body forward, A/D = body sides.
        // Using camera-relative axes here created a feedback loop where
        // pressing W rotated the body toward the camera, which changed the
        // camera direction, which changed wishDir — causing a permanent spin.
        Vector3 wishDir = (GetBodyForward() * v + GetBodyRight() * h).normalized;

        if (IsSprinting)
        {
            AccelerateToward(wishDir, sprintSpeed, sprintAccel);
        }
        else
        {
            AccelerateToward(wishDir, runSpeed, runAccel);

            // Sprint bleed: if we are still faster than runSpeed (from a prior sprint),
            // gently decay toward runSpeed while input is held.
            if (_horizontalVelocity.magnitude > runSpeed)
            {
                float newMag = Mathf.Max(
                    _horizontalVelocity.magnitude - sprintBleedRate * Time.deltaTime,
                    runSpeed);
                _horizontalVelocity = _horizontalVelocity.normalized * newMag;
            }
        }

        // Auto-turn body toward wish direction at a speed that narrows with velocity
        float turnSpeed = ComputeTurnSpeed();
        float targetYaw = Mathf.Atan2(wishDir.x, wishDir.z) * Mathf.Rad2Deg;
        _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, targetYaw, turnSpeed * Time.deltaTime);
    }

    // ── Velocity helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Asymptotic acceleration: a = accelBase * (1 – projected/vMax).
    /// Uses the projection of current velocity onto wishDir so the curve is
    /// direction-aware — pivots get a full acceleration kick.
    /// A minimum floor (5 % of accelBase) guarantees we always start from rest.
    /// </summary>
    private void AccelerateToward(Vector3 dir, float vMax, float accelBase)
    {
        float projected      = Vector3.Dot(_horizontalVelocity, dir);
        float factor         = 1f - Mathf.Clamp01(projected / vMax);
        float effectiveAccel = Mathf.Max(accelBase * factor, accelBase * 0.05f);

        _horizontalVelocity = Vector3.MoveTowards(
            _horizontalVelocity, dir * vMax, effectiveAccel * Time.deltaTime);
    }

    private void Decelerate(float rate)
    {
        _horizontalVelocity = Vector3.MoveTowards(
            _horizontalVelocity, Vector3.zero, rate * Time.deltaTime);
    }

    private void CommitVelocity()
    {
        if (_horizontalVelocity.sqrMagnitude > 0.0001f)
            _controller.Move(_horizontalVelocity * Time.deltaTime);
    }

    /// <summary>Writes _bodyYaw into the public BodyYaw property and transform.rotation.</summary>
    private void WriteBodyTransform()
    {
        BodyYaw            = _bodyYaw;
        transform.rotation = Quaternion.Euler(0f, _bodyYaw, 0f);
    }

    // ── Turn speed ────────────────────────────────────────────────────────────

    private float ComputeTurnSpeed()
    {
        float vNorm   = Mathf.Clamp01(_horizontalVelocity.magnitude / runSpeed);
        float baseTurn = Mathf.Lerp(maxTurnSpeed, minTurnSpeed, vNorm);

        if (IsSprinting && sprintSpeed > runSpeed)
        {
            float sprintNorm = Mathf.Clamp01(
                (_horizontalVelocity.magnitude - runSpeed) / (sprintSpeed - runSpeed));
            baseTurn = Mathf.Lerp(baseTurn, sprintTurnSpeed, sprintNorm);
        }

        return baseTurn;
    }

    // ── Gravity ───────────────────────────────────────────────────────────────

    private void ApplyGravity()
    {
        if (IsGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
        _verticalVelocity += gravity * Time.deltaTime;
        _controller.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
    }

    // ── Stamina ───────────────────────────────────────────────────────────────

    private void UpdateStamina()
    {
        if (IsSprinting)
        {
            Stamina    = Mathf.Max(Stamina - sprintStaminaDrain * Time.deltaTime, 0f);
            _idleTimer = 0f;
            return;
        }

        // Track idle time for boosted regen (threshold: ~0.2 m/s)
        bool nearlyStill = _horizontalVelocity.sqrMagnitude < 0.04f;
        _idleTimer = nearlyStill ? _idleTimer + Time.deltaTime : 0f;

        float rate = (_idleTimer >= idleRegenDelay)
            ? staminaRegen * idleRegenMultiplier
            : staminaRegen;

        Stamina = Mathf.Min(Stamina + rate * Time.deltaTime, maxStamina);
    }

    // ── Animator ──────────────────────────────────────────────────────────────

    private void UpdateAnimator()
    {
        if (animator == null) return;
        animator.SetFloat(_hashSpeed,        MoveSpeed);
        animator.SetBool (_hashGrounded,     IsGrounded);
        animator.SetBool (_hashSprinting,    IsSprinting);
        animator.SetBool (_hashCrouching,    IsCrouching);
        animator.SetBool (_hashBackpedaling, IsBackpedaling);
        animator.SetBool (_hashDodging,      IsDodging);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Body forward on the horizontal plane (derived from _bodyYaw).</summary>
    private Vector3 GetBodyForward()
    {
        return Quaternion.Euler(0f, _bodyYaw, 0f) * Vector3.forward;
    }

    /// <summary>Body right on the horizontal plane (derived from _bodyYaw).</summary>
    private Vector3 GetBodyRight()
    {
        return Quaternion.Euler(0f, _bodyYaw, 0f) * Vector3.right;
    }

    // Camera helpers — still used by HandleCrouchMovement and HandleBackpedal
    // (body is locked to camera in those modes, so results are equivalent).
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

    private float GetCameraYaw()
    {
        if (cameraTransform != null)
        {
            Vector3 f = cameraTransform.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 0.001f)
                return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }
        return _bodyYaw;
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}
