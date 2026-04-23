using UnityEngine;

/// <summary>
/// Third-person follow camera. Attach to the Camera GameObject (not the player).
/// Assign target to the player Transform. Unity 6000.x.
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Offset")]
    public Vector3 offset = new Vector3(0f, 2.5f, -5f);

    [Header("Follow")]
    public float followSmoothTime = 0.12f;
    public float rotateSmoothTime = 0.08f;

    [Header("Mouse Look")]
    public float mouseSensitivity = 3f;
    public float pitchMin = -20f;
    public float pitchMax =  60f;
    public bool  invertY  = false;

    [Header("Collision")]
    public float     collisionRadius = 0.3f;
    public LayerMask collisionMask;

    private float _yaw;
    private float _pitch = 15f;

    private Vector3 _posVelocity;
    private float   _rotVelocity;

    private void LateUpdate()
    {
        if (target == null) return;
        HandleMouseInput();
        ApplyCameraPosition();
    }

    private void HandleMouseInput()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);

        _yaw   += mouseX;
        _pitch  = Mathf.Clamp(_pitch + mouseY, pitchMin, pitchMax);
    }

    private void ApplyCameraPosition()
    {
        Quaternion targetRotation = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3    desiredPos     = target.position + targetRotation * offset;

        if (Physics.SphereCast(target.position, collisionRadius,
                               (desiredPos - target.position).normalized,
                               out RaycastHit hit,
                               offset.magnitude,
                               collisionMask,
                               QueryTriggerInteraction.Ignore))
        {
            desiredPos = target.position + (desiredPos - target.position).normalized * hit.distance;
        }

        transform.position = Vector3.SmoothDamp(transform.position, desiredPos,
                                                ref _posVelocity, followSmoothTime);
        transform.LookAt(target.position + Vector3.up * 1f);
    }

    public void SnapToTarget()
    {
        if (target == null) return;
        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        transform.position = target.position + rot * offset;
        transform.LookAt(target.position + Vector3.up * 1f);
    }

    private void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(target.position, transform.position);
    }
}
