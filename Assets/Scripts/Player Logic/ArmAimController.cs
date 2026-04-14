using Unity.Netcode;
using UnityEngine;

public class ArmAimController : MonoBehaviour
{
    [Header("Right Arm Joints (Gun Hand)")]
    [SerializeField] Rigidbody2D _armRb;
    [SerializeField] Rigidbody2D _wristRb;
    [SerializeField] Rigidbody2D _handRb;

    [Header("Left Arm Joints (Support Hand)")]
    [SerializeField] Rigidbody2D _armLRb;
    [SerializeField] Rigidbody2D _wristLRb;
    [SerializeField] Rigidbody2D _handLRb;

    [Header("Transforms")]
    [SerializeField] Transform _shoulder;
    public Transform Shoulder => _shoulder;   // exposed so PlayerController can compute aim angle

    [Header("Settings")]
    [SerializeField] float _handSpeed = 15f;
    [SerializeField] float _leftSpeed = 3f;

    [Header("Rendering")]
    [SerializeField] SpriteRenderer[] _armRenderers;
    [SerializeField] int _frontOrder = 5;
    [SerializeField] int _backOrder  = -1;

    bool  _holdingGun;
    float _serverAimAngle;   // set by PlayerController each FixedUpdate on server

    // ── Awake — disable arm colliders so ground contact doesn't jam MoveRotation ──

    void Awake()
    {
        // Arms, wrists and hands serve no collision purpose — pickup is the gun's
        // trigger, hit detection is on torso/legs. Disabling prevents ground contact
        // from locking the arm in place and making P2's aim look broken.
        DisableColliders(_armRb);
        DisableColliders(_wristRb);
        DisableColliders(_handRb);
        DisableColliders(_armLRb);
        DisableColliders(_wristLRb);
        DisableColliders(_handLRb);
    }

    static void DisableColliders(Rigidbody2D rb)
    {
        if (rb == null) return;
        foreach (var col in rb.GetComponents<Collider2D>())
            col.enabled = false;
    }

    public void SetGunRenderer(SpriteRenderer sr)
    {
        _holdingGun = sr != null;
    }

    /// <summary>PlayerController calls this on the server every FixedUpdate.</summary>
    public void SetServerAimAngle(float angle)
    {
        _serverAimAngle = angle;
    }

    void FixedUpdate()
    {
        // Only the server applies physics to the arms —
        // RagdollSync streams the resulting rotations to all clients
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer) return;

        if (_shoulder == null || _handRb == null) return;

        float aimAngle   = _serverAimAngle;
        bool  crossedBody = Mathf.Abs(aimAngle) > 90f;

        // Right arm, wrist, and hand all track aim at all times.
        // When holding a gun snap precisely; otherwise lerp smoothly.
        if (_holdingGun)
        {
            if (_armRb   != null) _armRb.MoveRotation(aimAngle);
            if (_wristRb != null) _wristRb.MoveRotation(aimAngle);
            _handRb.MoveRotation(aimAngle);
        }
        else
        {
            if (_armRb   != null) _armRb.MoveRotation(Mathf.LerpAngle(_armRb.rotation,   aimAngle, _handSpeed * Time.fixedDeltaTime));
            if (_wristRb != null) _wristRb.MoveRotation(Mathf.LerpAngle(_wristRb.rotation, aimAngle, _handSpeed * Time.fixedDeltaTime));
            _handRb.MoveRotation(Mathf.LerpAngle(_handRb.rotation, aimAngle, _handSpeed * Time.fixedDeltaTime));
        }

        // Left arm spawns in T-pose pointing left (180° offset from right arm),
        // so invert the angle so it tracks the same mouse direction correctly
        float leftAimAngle = aimAngle + 180f;

        if (_armLRb   != null) _armLRb.MoveRotation(Mathf.LerpAngle(_armLRb.rotation,     leftAimAngle, _leftSpeed * Time.fixedDeltaTime));
        if (_wristLRb != null) _wristLRb.MoveRotation(Mathf.LerpAngle(_wristLRb.rotation, leftAimAngle, _leftSpeed * Time.fixedDeltaTime));
        if (_handLRb  != null) _handLRb.MoveRotation(Mathf.LerpAngle(_handLRb.rotation,   leftAimAngle, _leftSpeed * Time.fixedDeltaTime));

        // Arm sort order
        int order = crossedBody ? _frontOrder : _backOrder;
        foreach (var sr in _armRenderers)
            if (sr != null) sr.sortingOrder = order;

        // Gun sprite flip is computed locally in Gun.Update() on every client
    }
}
