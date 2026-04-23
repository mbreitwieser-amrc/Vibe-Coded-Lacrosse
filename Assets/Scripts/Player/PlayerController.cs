using UnityEngine;

/// <summary>
/// Lacrosse-authentic movement system. Unity 6000.x.
///
/// Body rotation is owned by FirstPersonCamera. This script handles only
/// horizontal momentum, vertical movement, stamina, and dodge.
///
/// -- Controls --
///   WASD          - move (relative to camera facing, always)
///   Left Shift    - sprint  (rebindable: sprintKey)
///   Left Ctrl     - strafe / defensive stance  (rebindable: strafeKey)
///                   Locks body facing; enables side-dodge; defensive shuffle
///   Left Alt      - hard brake / plant  (rebindable: brakeKey)
///   Double-tap W/A/S/D - dodge burst in that direction (stamina cost)
///   Space         - jump
///
/// -- Momentum model --
///   _horizontalVelocity persists across frames (true momentum).
///   Acceleration / deceleration are applied via MoveTowards so direction
///   changes bleed through the velocity vector - bouncy, lacrosse-like.
///   Plant detection: when input opposes current velocity by >90 degrees, deceleration
///   is multiplied by plantBoost to simulate an outside-foot plant.
///
/// -- Stamina --
///   Sprint and strafe drain stamina. Walking and standing regen it.
///   Dodge costs a flat chunk. Sprint immediately drops to walk at 0 stamina.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Speeds")]
    [Tooltip("Normal walking speed (m/s).")]
    public float walkSpeed   = 5f;
    [Tooltip("Sprint speed. Requires stamina.")]
    public float sprintSpeed = 9f;
    [Tooltip("Defensive shuffle / strafe speed (Ctrl held). Slower but highly responsive.")]
    public float strafeSpeed = 3.8f;

    [Header("Acceleration")]
    [Tooltip("Horizontal acceleration during normal movement.")]
    public float acceleration  = 18f;
    [Tooltip("Horizontal acceleration while sprinting (snappier feel).")]
    public float sprintAccel   = 26f;
    [Tooltip("Horizontal acceleration while strafing (highly responsive lateral shuffle).")]
    public float strafeAccel   = 28f;
    [Tooltip("Natural deceleration when no key is held.")]
    public float deceleration  = 14f;
    [Tooltip("Deceleration rate while Alt-brake is held.")]
    public float brakeDecel    = 50f;
    [Tooltip("Multiplier applied to deceleration when input opposes current velocity >90 degrees.\nSimulates planting the outside foot before a direction change.")]
    public float plantBoost    = 2.6f;

    [Header("Dodge")]
    [Tooltip("Burst speed at the start of a dodge (m/s).")]
    public float dodgeSpeed      = 13f;
    [Tooltip("How long the dodge burst velocity is held before normal decel resumes (s).")]
    public float dodgeDuration   = 0.22f;
    [Tooltip("Cooldown between dodges (s).")]
    public float dodgeCooldown   = 0.85f;
    [Tooltip("Double-tap window: max time between two presses of the same key (s).")]
    public float doubleTapWindow = 0.26f;

    [Header("Stamina")]
    public float maxStamina       = 100f;
    [Tooltip("Stamina drained per second while sprinting.")]
    public float sprintDrain      = 22f;
    [Tooltip("Stamina drained per second while strafing (light drain - defensive footwork).")]
    public float strafeDrain      = 7f;
    [Tooltip("Stamina regenerated per second while walking or stationary.")]
    public float staminaRegen     = 14f;
    [Tooltip("Stamina cost of one dodge.")]
    public float dodgeStaminaCost = 22f;
    [Tooltip("Minimum stamina required to initiate a sprint (prevents rapid start/stop gaming).")]
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
    public KeyCode strafeKey = KeyCode.LeftControl;
    public KeyCode brakeKey  = KeyCode.LeftAlt;

    [Header("References")]
    public Transform cameraTransform;
    public Animator  animator;

    public bool  IsGrounded   { get; private set; }
    public bool  IsSprinting  { get; private set; }
    public bool  IsStrafing   { get; private set; }
    public bool  IsBraking    { get; private set; }
    public bool  IsDodging    { get; private set; }
    public float Stamina      { get; private set; }
    public float StaminaRatio => Stamina / maxStamina;
    public float MoveSpeed    { get; private set; }

    public event System.Action<Vector3> OnDodge;

    private static readonly int _hashSpeed     = Animator.StringToHash("Speed");
    private static readonly int _hashGrounded  = Animator.StringToHash("IsGrounded");
    private static readonly int _hashSprinting = Animator.StringToHash("IsSprinting");
    private static readonly int _hashStrafing  = Animator.StringToHash("IsStrafing");
    private static readonly int _hashDodging   = Animator.StringToHash("IsDodging");
    private static readonly int _hashJump      = Animator.StringToHash("Jump");

    private CharacterController _controller;
    private Vector3 _horizontalVelocity;
    private float   _verticalVelocity;
    private float _dodgeTimer;
    private float _dodgeCooldownTimer;
    private float _lastTapW, _lastTapA, _lastTapS, _lastTapD;
    private bool  _prevW, _prevA, _prevS, _prevD;
    private bool _inputEnabled = true;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (groundCheck == null)
        {
            var go = new UnityEngine.GameObject("GroundCheck");
            go.transform.SetParent(transform);
            go.transform.localPosition =
                new Vector3(0f, -(_controller.height * 0.5f + 0.05f), 0f);
            groundCheck = go.transform;
        }

        Stamina = maxStamina;
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

    private void CheckGround()
    {
        IsGrounded = Physics.CheckSphere(
            groundCheck.position, groundCheckRadius,
            groundMask, QueryTriggerInteraction.Ignore);
    }

    private void TickTimers()
    {
        if (_dodgeCooldownTimer > 0f) _dodgeCooldownTimer -= Time.deltaTime;
        if (_dodgeTimer > 0f)
        {
            _dodgeTimer -= Time.deltaTime;
            if (_dodgeTimer <= 0f) IsDodging = false;
        }
    }

    private void DetectDoubleTap()
    {
        if (!IsGrounded || _dodgeCooldownTimer > 0f) return;

        bool w = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        bool a = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
        bool s = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
        bool d = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);

        bool wDown = w && !_prevW;
        bool aDown = a && !_prevA;
        bool sDown = s && !_prevS;
        bool dDown = d && !_prevD;

        float t = Time.time;
        if (wDown) { if (t - _lastTapW < doubleTapWindow) TryDodge(0);  _lastTapW = t; }
        if (aDown) { if (t - _lastTapA < doubleTapWindow) TryDodge(2);  _lastTapA = t; }
        if (sDown) { if (t - _lastTapS < doubleTapWindow) TryDodge(1);  _lastTapS = t; }
        if (dDown) { if (t - _lastTapD < doubleTapWindow) TryDodge(3);  _lastTapD = t; }

        _prevW = w; _prevA = a; _prevS = s; _prevD = d;
    }

    private void TryDodge(int dir)
    {
        if (Stamina < dodgeStaminaCost) return;

        Vector3 camF = GetCameraForward();
        Vector3 camR = GetCameraRight();

        Vector3 dodgeDir = dir switch
        {
            0 =>  camF,
            1 => -camF,
            2 => -camR,
            3 =>  camR,
            _ =>  camF
        };

        _horizontalVelocity = dodgeDir * dodgeSpeed;
        _dodgeTimer         = dodgeDuration;
        _dodgeCooldownTimer = dodgeCooldown;
        IsDodging           = true;

        Stamina = Mathf.Max(Stamina - dodgeStaminaCost, 0f);
        OnDodge?.Invoke(dodgeDir);
    }

    private void HandleHorizontalMovement()
    {
        if (IsDodging) return;

        float h       = Input.GetAxisRaw("Horizontal");
        float v       = Input.GetAxisRaw("Vertical");
        bool  hasInput = (Mathf.Abs(h) + Mathf.Abs(v)) > 0.1f;

        IsBraking  = Input.GetKey(brakeKey);
        IsStrafing = Input.GetKey(strafeKey);

        bool wantSprint = Input.GetKey(sprintKey) && hasInput && !IsStrafing && !IsBraking;
        if (wantSprint && Stamina <= 0f)          wantSprint = false;
        if (wantSprint && !IsSprinting
                       && Stamina < sprintMinStamina) wantSprint = false;
        IsSprinting = wantSprint;

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
            float targetSpeed = IsSprinting ? sprintSpeed
                              : IsStrafing  ? strafeSpeed
                              :               walkSpeed;

            float baseAccel   = IsSprinting ? sprintAccel
                              : IsStrafing  ? strafeAccel
                              :               acceleration;

            Vector3 wishDir = (GetCameraForward() * v + GetCameraRight() * h).normalized;
            Vector3 wishVel = wishDir * targetSpeed;

            float dot = _horizontalVelocity.sqrMagnitude > 0.01f
                      ? Vector3.Dot(_horizontalVelocity.normalized, wishDir)
                      : 1f;
            float effectiveAccel = baseAccel * (dot < -0.3f ? plantBoost : 1f);

            _horizontalVelocity = Vector3.MoveTowards(
                _horizontalVelocity, wishVel,
                effectiveAccel * Time.deltaTime);
        }

        MoveSpeed = _horizontalVelocity.magnitude;
    }

    private void ApplyFriction(float rate)
    {
        _horizontalVelocity = Vector3.MoveTowards(
            _horizontalVelocity, Vector3.zero,
            rate * Time.deltaTime);
    }

    private void CommitHorizontalVelocity()
    {
        if (_horizontalVelocity.sqrMagnitude > 0.0001f)
            _controller.Move(_horizontalVelocity * Time.deltaTime);
    }

    private void HandleJump()
    {
        if (IsGrounded && Input.GetButtonDown("Jump"))
        {
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            animator?.SetTrigger(_hashJump);
        }
    }

    private void ApplyGravity()
    {
        if (IsGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;

        _verticalVelocity += gravity * Time.deltaTime;
        _controller.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
    }

    private void UpdateStamina()
    {
        if (IsSprinting)
            Stamina -= sprintDrain * Time.deltaTime;
        else if (IsStrafing)
            Stamina -= strafeDrain * Time.deltaTime;
        else
            Stamina += staminaRegen * Time.deltaTime;

        Stamina = Mathf.Clamp(Stamina, 0f, maxStamina);
    }

    private void UpdateAnimator()
    {
        if (animator == null) return;
        animator.SetFloat(_hashSpeed,     MoveSpeed);
        animator.SetBool (_hashGrounded,  IsGrounded);
        animator.SetBool (_hashSprinting, IsSprinting);
        animator.SetBool (_hashStrafing,  IsStrafing);
        animator.SetBool (_hashDodging,   IsDodging);
    }

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
