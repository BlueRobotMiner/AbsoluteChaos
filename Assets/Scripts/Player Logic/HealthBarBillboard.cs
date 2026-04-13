using UnityEngine;

/// <summary>
/// Keeps the health bar Canvas positioned above the torso each frame,
/// regardless of ragdoll physics pulling the root transform away.
/// Attach to the same GameObject as the World Space Canvas.
/// </summary>
public class HealthBarBillboard : MonoBehaviour
{
    [SerializeField] Transform _target;       // drag the Torso transform here
    [SerializeField] Vector3   _offset = new Vector3(0f, 0.8f, 0f);  // above the torso

    void LateUpdate()
    {
        if (_target == null) return;

        // Follow torso world position + offset
        transform.position = _target.position + _offset;

        // Stay upright — don't inherit any ragdoll rotation
        transform.rotation = Quaternion.identity;
    }
}
