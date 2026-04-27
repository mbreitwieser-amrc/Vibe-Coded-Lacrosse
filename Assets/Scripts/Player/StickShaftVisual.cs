using UnityEngine;

/// <summary>
/// Positions and orients the stick shaft cylinder in world space so it always
/// stretches from the grip anchor to the stick head socket.
///
/// The shaft cylinder's local Y axis is its long axis (Unity default).
/// LookRotation gives Z-forward, so we apply a −90° X rotation to align Y
/// with the shaft direction.
///
/// Detaches from any parent in Awake so the parent's transform does not
/// fight the world-space writes done here each LateUpdate.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class StickShaftVisual : MonoBehaviour
{
    [Tooltip("The StickInputController on the Player.")]
    public StickInputController stickInput;

    private void Awake()
    {
        // Own the world transform entirely — parent motion must not interfere.
        transform.SetParent(null, true);
    }

    private void LateUpdate()
    {
        if (stickInput == null || stickInput.stickHeadSocket == null) return;

        Vector3 grip = stickInput.GripAnchor;
        Vector3 head = stickInput.stickHeadSocket.position;
        Vector3 dir  = head - grip;
        float   len  = dir.magnitude;

        if (len < 0.001f) return;

        // Centre the shaft between grip and socket
        transform.position = (grip + head) * 0.5f;

        // Orient: Quaternion.LookRotation points Z toward dir.
        // Rotating −90° around X swings Y to point toward dir (shaft long axis).
        transform.rotation = Quaternion.LookRotation(dir.normalized)
                           * Quaternion.Euler(90f, 0f, 0f);

        // Scale Y to half the shaft length (cylinder height = 2× local Y scale)
        transform.localScale = new Vector3(0.025f, len * 0.5f, 0.025f);
    }
}
