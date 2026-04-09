using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles local player movement and aiming.
/// Only the owning client drives input; NetworkTransform syncs position to everyone else.
/// Required on prefab: Rigidbody2D, NetworkObject, NetworkTransform (Interpolate = on).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] float _moveSpeed  = 8f;
    [SerializeField] float _jumpForce  = 14f;
    [SerializeField] int   _maxJumps   = 2;

    [Header("Ground Check")]
    [SerializeField] Transform  _groundCheck;
    [SerializeField] LayerMask  _groundLayer;
    [SerializeField] float      _groundCheckRadius = 0.2f;

    [Header("Aim")]
    [SerializeField] Transform _aimPivot;   // child transform that rotates toward mouse

    Rigidbody2D _rb;
    int _jumpsRemaining;
    bool _inputEnabled = true;

    // ── Unity ─────────────────────────────────────────────────────────────

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
    }

    // ── NGO ───────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        // Disable the camera on non-owner players so only our own camera follows us
        if (!IsOwner)
        {
            // If there is a Camera child, disable it
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) cam.enabled = false;
        }
    }

    // ── Update ────────────────────────────────────────────────────────────

    void Update()
    {
        if (!IsOwner || !_inputEnabled) return;

        HandleAim();
        HandleJump();
    }

    void FixedUpdate()
    {
        if (!IsOwner || !_inputEnabled) return;

        HandleMove();
    }

    // ── Movement ──────────────────────────────────────────────────────────

    void HandleMove()
    {
        float h = Input.GetAxisRaw("Horizontal");
        _rb.velocity = new Vector2(h * _moveSpeed, _rb.velocity.y);

        // Flip sprite to face movement direction
        if (h != 0)
            transform.localScale = new Vector3(Mathf.Sign(h), 1f, 1f);
    }

    void HandleJump()
    {
        if (IsGrounded())
            _jumpsRemaining = _maxJumps;

        if (Input.GetKeyDown(KeyCode.Space) && _jumpsRemaining > 0)
        {
            _rb.velocity = new Vector2(_rb.velocity.x, _jumpForce);
            _jumpsRemaining--;

            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayJumpSFX();
        }
    }

    void HandleAim()
    {
        if (_aimPivot == null || Camera.main == null) return;

        Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dir = mouseWorld - (Vector2)_aimPivot.position;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        _aimPivot.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    bool IsGrounded()
    {
        return Physics2D.OverlapCircle(_groundCheck.position, _groundCheckRadius, _groundLayer);
    }

    /// <summary>Disables player input — called by PlayerHealth on elimination.</summary>
    public void SetInputEnabled(bool enabled)
    {
        _inputEnabled = enabled;
        if (!enabled)
            _rb.velocity = Vector2.zero;
    }
}
