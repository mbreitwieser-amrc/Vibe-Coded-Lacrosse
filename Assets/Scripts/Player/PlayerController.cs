using UnityEngine;

/// <summary>
/// Third-person player movement for Unity 6000.x.
/// Requires a CharacterController. Assign cameraTransform in the Inspector.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed    = 5f;
    public float sprintSpeed  = 9f;
    public float jumpHeight   = 1.5f;
    public float gravity      = -20f;
    public float turnSmoothTime = 0.08f;

    [Header("Ground Detection")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.25f;
    public LayerMask groundMask;

    [Header("References")]
    public Transform cameraTransform;
    public Animator  animator;          // Optional – assign if using an Animator

    // ── Public state ─────────────────────────────────────────────────────────
    public bool  IsGrounded  { get; private set; }
    public bool  IsSprinting { get; private set; }
    public float MoveSpeed   { get; private set; }

    // ── Animator parameter hashes (perf: avoid string lookup per frame) ──────
    private static readonly int _hashSpeed      = Animator.StringToHash("Speed");
    private static readonly int _hashGrounded   = Animator.StringToHash("IsGrounded");
    private static readonly int _hashSprinting  = Animator.StringToHash("IsSprinting");
    private static readonly int _hashJump       = Animator.StringToHash("Jump");

    // ── Private fields ───────────────────────────────────────────────────────
    private CharacterController _controller;
    private Vector3  _velocity;
    private float    _turnSmoothVelocity;
    private bool     _inputEnabled = true;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();

        // Auto-find Animator if not assigned
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // Auto-create ground check if not assigned
        if (groundCheck == null)
        {
            var go = new GameObject("GroundCheck");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(0f, -(_controller.height * 0.5f + 0.05f), 0f);
            groundCheck = go.transform;
        }
    }

    private void Update()
    {
        CheckGround();
        if (_inputEnabled)
        {
            HandleMovement();
            HandleJump();
        }
        ApplyGravity();
        UpdateAnimator();
    }

    // ── Ground check ─────────────────────────────────────────────────────────
    private void CheckGround()
    {
        IsGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundMask,
                                         QueryTriggerInteraction.Ignore);
    }

    // ── Movement ─────────────────────────────────────────────────────────────
    private void HandleMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 input = new Vector3(h, 0f, v).normalized;

        IsSprinting = Input.GetKey(KeyCode.LeftShift) && input.magnitude > 0.1f;
        float speed = IsSprinting ? sprintSpeed : walkSpeed;
        MoveSpeed   = input.magnitude * speed;

        if (input.magnitude >= 0.1f)
        {
            float targetAngle = Mathf.Atan2(input.x, input.z) * Mathf.Rad2Deg
                                + cameraTransform.eulerAngles.y;
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle,
                                                 ref _turnSmoothVelocity, turnSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);

            Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            _controller.Move(moveDir * speed * Time.deltaTime);
        }
        else
        {
            MoveSpeed = 0f;
        }
    }

    // ── Jump ─────────────────────────────────────────────────────────────────
    private void HandleJump()
    {
        if (IsGrounded && Input.GetButtonDown("Jump"))
        {
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            animator?.SetTrigger(_hashJump);
        }
    }

    // ── Gravity ──────────────────────────────────────────────────────────────
    private void ApplyGravity()
    {
        if (IsGrounded && _velocity.y < 0f)
            _velocity.y = -2f;  // Small constant keeps player pressed to ground

        _velocity.y += gravity * Time.deltaTime;
        _controller.Move(_velocity * Time.deltaTime);
    }

    // ── Animator ─────────────────────────────────────────────────────────────
    private void UpdateAnimator()
    {
        if (animator == null) return;
        animator.SetFloat(_hashSpeed,     MoveSpeed);
        animator.SetBool (_hashGrounded,  IsGrounded);
        animator.SetBool (_hashSprinting, IsSprinting);
    }

    // ── Public API ───────────────────────────────────────────────────────────
    /// <summary>Disable player input (e.g. during cutscene or pause).</summary>
    public void SetInputEnabled(bool enabled) => _inputEnabled = enabled;

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}
