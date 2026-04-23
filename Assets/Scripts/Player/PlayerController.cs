using UnityEngine;

/// <summary>
/// First-person player movement. Unity 6000.x.
/// CharacterController-based. Body rotation is owned by FirstPersonCamera;
/// this script only moves the character — no rotation-toward-movement.
/// W/S = camera-forward/-backward, A/D = strafe, Shift = sprint, Space = jump.
/// Assign cameraTransform to the FirstPersonCamera's Transform.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed   = 5f;
    public float sprintSpeed = 9f;
    public float jumpHeight  = 1.5f;
    public float gravity     = -20f;

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
        bool  hasInput = (Mathf.Abs(h) + Mathf.Abs(v)) > 0.1f;

        IsSprinting = Input.GetKey(KeyCode.LeftShift) && hasInput;
        float speed = IsSprinting ? sprintSpeed : walkSpeed;

        if (hasInput && cameraTransform != null)
        {
            // FPS strafe: W/S along camera-forward, A/D along camera-right.
            // Body rotation is owned by FirstPersonCamera — no rotation here.
            Vector3 forward = cameraTransform.forward; forward.y = 0f; forward.Normalize();
            Vector3 right   = cameraTransform.right;   right.y   = 0f; right.Normalize();
            Vector3 moveDir = (forward * v + right * h).normalized;
            _controller.Move(moveDir * speed * Time.deltaTime);
            MoveSpeed = speed;
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
