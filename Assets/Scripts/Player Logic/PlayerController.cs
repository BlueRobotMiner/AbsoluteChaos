using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    public GameObject  leftLeg;
    public GameObject  rightLeg;
    public Rigidbody2D rb;
    public float       speed = 1.5f;

    [Header("Step Feel")]
    public float stepWait      = 0.25f;
    public float stepLiftForce = 1.5f;
    public float bodyBounce    = 0.3f;
    public float stoppingDrag  = 12f;

    [Header("Lean")]
    public BalanceController torsoBalance;
    public float leanFactor    = 5f;
    public float maxLean       = 20f;
    public float leanSmoothing = 8f;

    [Header("Jump")]
    [SerializeField] float _jumpForce = 15f;  // launch velocity — what you set is what you get

    [Header("Ground Check")]
    public Transform playerPos;
    public Transform leftFootPos;
    public Transform rightFootPos;
    public float     positionRadius;
    public LayerMask ground;

    [Header("Gun")]
    [SerializeField] Transform _gunHandAttach;
    public Transform GunHandAttach => _gunHandAttach;

    [Header("Animation")]
    public Animator anim;

    [Header("Player Colors")]
    [SerializeField] Color[] _slotColors =
    {
        Color.white,
        new(1f, 0.3f, 0.3f, 1f),
        new(0.2f, 1f, 0.4f, 1f),
    };

    // ── Synced state (server writes, everyone reads) ───────────────────────
    NetworkVariable<Vector2> _syncedPosition = new NetworkVariable<Vector2>(
        Vector2.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    NetworkVariable<int> _syncedAnimDir = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Server writes airborne state — clients play jump anim when true
    NetworkVariable<bool> _syncedAirborne = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Server-side buffered input ─────────────────────────────────────────
    float _pendingH;
    float _pendingAimAngle;
    bool  _pendingJump;

    // ── Input RPC throttle (client → server) ───────────────────────────────
    float _lastSentH;
    float _lastSentAim;
    float _inputSendTimer;
    const float InputSendInterval = 0.05f; // fallback heartbeat: 20/sec max

    // ── Private state ──────────────────────────────────────────────────────
    Rigidbody2D _leftLegRb;
    Rigidbody2D _rightLegRb;
    bool  _inputEnabled = true;
    bool  _leftStep     = true;
    float _walkTimer;

    // Jump buffer — keeps the jump request alive for several physics frames
    // so a momentary failed ground check doesn't silently eat the Space press
    int _jumpBufferFrames;
    const int JumpBufferMax = 6;

    // Knockback suppression — prevents HandleMove from overwriting external impulses
    bool _suppressHorizontal;

    // Draft mode
    bool           _draftMode;
    CardSlot[]     _draftCards;
    CardDraftingUI _draftManager;
    CardSlot       _hoveredDraftCard;

    // ── Unity ──────────────────────────────────────────────────────────────

    void Start()
    {
        _leftLegRb  = leftLeg.GetComponent<Rigidbody2D>();
        _rightLegRb = rightLeg.GetComponent<Rigidbody2D>();
        _leftLegRb.sleepMode  = RigidbodySleepMode2D.NeverSleep;
        _rightLegRb.sleepMode = RigidbodySleepMode2D.NeverSleep;
    }

    // ── NGO ────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        // Only the local owner sees through their camera
        Camera cam = GetComponentInChildren<Camera>();
        if (cam != null) cam.enabled = IsOwner;

        // Color by connection slot
        int   slot = GetPlayerSlot();
        Color col  = slot < _slotColors.Length ? _slotColors[slot] : Color.white;
        foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
            sr.color = col;

        if (IsServer)
        {
            // Server owns all physics — hold root kinematic for 2 frames so spawn position settles
            if (rb != null) rb.isKinematic = true;
            StartCoroutine(UnlockPhysicsAfterSync());
        }
        else
        {
            // Clients are pure renderers — disable ALL rigidbodies completely
            foreach (var r in GetComponentsInChildren<Rigidbody2D>())
            {
                r.isKinematic = true;
                r.simulated   = false;
            }
        }

        if (IsOwner)
        {
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible   = true;
        }

        // Clear stale input carried over from the previous scene
        _pendingH         = 0f;
        _pendingJump      = false;
        _lastSentH        = 0f;
        _suppressHorizontal = false;
    }

    System.Collections.IEnumerator UnlockPhysicsAfterSync()
    {
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        if (rb != null) rb.isKinematic = false;
    }

    [ClientRpc]
    public void InitializePositionClientRpc(Vector2 spawnPos)
    {
        if (IsServer)
        {
            // Shift every ragdoll body by the same delta so joints don't fight each other
            if (rb != null)
            {
                Vector2 delta = spawnPos - rb.position;
                foreach (var r in GetComponentsInChildren<Rigidbody2D>())
                {
                    r.position       += delta;
                    r.velocity        = Vector2.zero;
                    r.angularVelocity = 0f;
                }
            }

            _syncedPosition.Value = spawnPos;

            // Force-broadcast new positions so clients snap rather than lerp from old spot
            GetComponent<RagdollSync>()?.ServerTeleport(spawnPos);
        }
        else
        {
            // Root snap — RagdollSync.SnapStateClientRpc (sent by server above) handles the limbs
            transform.position = new Vector3(spawnPos.x, spawnPos.y, 0f);
        }
    }

    public override void OnGainedOwnership()
    {
        Camera cam = GetComponentInChildren<Camera>();
        if (cam != null) cam.enabled = true;
        _inputEnabled = true;
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible   = true;
    }

    public override void OnLostOwnership()
    {
        Camera cam = GetComponentInChildren<Camera>();
        if (cam != null) cam.enabled = false;
        _inputEnabled = false;
    }

    // ── Update — input capture only, no physics ────────────────────────────

    bool _wasFocused = true;

    void Update()
    {
        if (!IsSpawned) return;

        // All non-server clients: render synced animation
        if (!IsServer)
            ApplySyncedAnimation(_syncedAnimDir.Value);

        // When the window loses focus, zero out buffered input so the player stops moving
        bool focused = Application.isFocused;
        if (IsOwner && _wasFocused && !focused)
        {
            if (IsServer)
                _pendingH = 0f;
            else
                SendInputServerRpc(0f, false, _lastSentAim);

            _lastSentH = 0f;
        }
        _wasFocused = focused;

        // Only the owner captures input
        if (!IsOwner || !_inputEnabled || !focused) return;
        if (_draftMode) { HandleDraftInput(); return; }

        float h    = Input.GetAxisRaw("Horizontal");
        bool  jump = Input.GetKeyDown(KeyCode.Space);
        float aim  = GetAimAngle();

        if (IsServer)
        {
            // Host: buffer input directly — no RPC needed
            _pendingH         = h;
            _pendingAimAngle  = aim;
            if (jump) _pendingJump = true;
        }
        else
        {
            // Client: only send when input changes meaningfully, or as a heartbeat
            bool hChanged   = Mathf.Abs(h   - _lastSentH)   > 0.05f;
            bool aimChanged = Mathf.Abs(Mathf.DeltaAngle(aim, _lastSentAim)) > 1f;
            _inputSendTimer += Time.deltaTime;
            bool timerFired = _inputSendTimer >= InputSendInterval;

            if (hChanged || aimChanged || jump || timerFired)
            {
                _lastSentH        = h;
                _lastSentAim      = aim;
                _inputSendTimer   = 0f;
                SendInputServerRpc(h, jump, aim);
            }
        }
    }

    // ── FixedUpdate — server runs physics, clients lerp ───────────────────

    void FixedUpdate()
    {
        if (!IsSpawned) return;

        if (!IsServer)
        {
            // Client: lerp root transform directly — rb.simulated is false so MovePosition does nothing
            Vector2 smoothed = Vector2.Lerp(
                (Vector2)transform.position, _syncedPosition.Value, 20f * Time.fixedDeltaTime);
            transform.position = new Vector3(smoothed.x, smoothed.y, 0f);
            return;
        }

        // ── Server: apply buffered input and run physics ───────────────────
        if (!_inputEnabled) return;

        HandleMove(_pendingH);

        if (_pendingJump)
        {
            _pendingJump = false;
            _jumpBufferFrames = JumpBufferMax;
        }

        if (_jumpBufferFrames > 0)
        {
            _jumpBufferFrames--;
            TryJump();   // keeps trying until grounded or buffer expires
        }

        // Feed aim angle to ArmAimController so it points correctly on server
        var aim = GetComponent<ArmAimController>();
        if (aim != null) aim.SetServerAimAngle(_pendingAimAngle);

        // Broadcast position (only when it moved enough to matter)
        if (rb != null)
        {
            Vector2 cur = rb.position;
            if (Vector2.Distance(cur, _syncedPosition.Value) > 0.02f)
                _syncedPosition.Value = cur;
        }

        // Broadcast animation direction
        int dir = _pendingH > 0 ? 1 : _pendingH < 0 ? -1 : 0;
        if (dir != _syncedAnimDir.Value) _syncedAnimDir.Value = dir;

        // Broadcast airborne state — use all ground check points so any foot contact counts
        bool grounded = (leftFootPos   != null && Physics2D.OverlapCircle(leftFootPos.position,   positionRadius, ground))
                     || (rightFootPos  != null && Physics2D.OverlapCircle(rightFootPos.position,  positionRadius, ground))
                     || (playerPos     != null && Physics2D.OverlapCircle(playerPos.position,     positionRadius, ground));
        bool airborne = !grounded;
        if (airborne != _syncedAirborne.Value) _syncedAirborne.Value = airborne;


        ApplySyncedAnimation(dir);
    }

    // ── ServerRpc — client sends raw input each frame ──────────────────────

    [ServerRpc]
    void SendInputServerRpc(float h, bool jump, float aimAngle)
    {
        _pendingH        = h;
        _pendingAimAngle = aimAngle;
        if (jump) _pendingJump = true;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    float GetAimAngle()
    {
        if (Camera.main == null || GetComponent<ArmAimController>() == null) return 0f;
        var shoulder = GetComponent<ArmAimController>().Shoulder;
        if (shoulder == null) return 0f;
        Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 toMouse    = mouseWorld - (Vector2)shoulder.position;
        return Mathf.Atan2(toMouse.y, toMouse.x) * Mathf.Rad2Deg;
    }

    // ── Animation ──────────────────────────────────────────────────────────

    void ApplySyncedAnimation(int dir)
    {
        if (anim == null) return;
        if      (dir > 0) anim.Play("WalkRight");
        else if (dir < 0) anim.Play("WalkLeft");
        else              anim.Play("Idle");
    }

    // ── Draft mode ─────────────────────────────────────────────────────────

    public void SetDraftMode(bool active, CardSlot[] cards, CardDraftingUI manager)
    {
        _draftMode    = active;
        _draftCards   = cards;
        _draftManager = manager;

        // Disable / re-enable shooting when entering / leaving draft
        var combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.SetShootingEnabled(!active);

        if (!active && _hoveredDraftCard != null)
        {
            _hoveredDraftCard.IsHovered = false;
            _hoveredDraftCard = null;
        }
    }

    void HandleDraftInput()
    {
        if (_draftCards == null || Camera.main == null) return;

        // ── Find which card the mouse is directly over (screen-space rect check) ──
        CardSlot nearest    = null;
        int      nearestIdx = -1;

        Camera uiCam = Camera.main;

        for (int i = 0; i < _draftCards.Length; i++)
        {
            var rect = _draftCards[i].GetComponent<RectTransform>();
            if (rect == null) continue;

            if (RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, uiCam))
            {
                nearest    = _draftCards[i];
                nearestIdx = i;
                break;   // mouse can only be over one card at a time
            }
        }

        // ── Sync hover to all clients when it changes ───────────────────
        if (nearest != _hoveredDraftCard)
        {
            _hoveredDraftCard = nearest;
            if (_draftManager != null)
                _draftManager.UpdateHoverServerRpc(nearestIdx);
        }

        // ── Calculate h and send movement to server ─────────────────────
        float h = 0f;
        if (nearest != null && rb != null)
        {
            float targetX = nearest.WorldTarget != null ? nearest.WorldTarget.position.x : rb.position.x;
            float dx      = targetX - rb.position.x;
            h             = Mathf.Abs(dx) > 0.3f ? Mathf.Sign(dx) : 0f;
        }

        _inputSendTimer += Time.deltaTime;
        bool hChanged = Mathf.Abs(h - _lastSentH) > 0.05f;
        if (hChanged || _inputSendTimer >= InputSendInterval)
        {
            _lastSentH      = h;
            _inputSendTimer = 0f;
            if (IsServer) _pendingH = h;
            else          SendInputServerRpc(h, false, 0f);
        }

        // ── Right click to select hovered card ──────────────────────────
        if (Input.GetMouseButtonDown(0) && _hoveredDraftCard != null && _draftManager != null)
        {
            var picked   = _hoveredDraftCard;
            var manager  = _draftManager;             // save before SetDraftMode nulls it
            SetDraftMode(false, null, null);          // re-enables shooting, clears refs
            manager.UpdateHoverServerRpc(-1);
            manager.SubmitPickServerRpc(GetPlayerSlotPublic(), (int)picked.CardId);
        }
    }

    // ── Movement (server only) ─────────────────────────────────────────────

    void HandleMove(float h)
    {
        if (rb == null) return;

        if (h != 0 && !_suppressHorizontal)
            // Set horizontal velocity only — preserve rb.velocity.y so gravity and jump are unaffected
            rb.velocity = new Vector2(h * speed, rb.velocity.y);
        else
            // Stopping drag — bleeds off horizontal momentum, vertical untouched
            rb.velocity = new Vector2(
                rb.velocity.x * (1f - stoppingDrag * Time.fixedDeltaTime),
                rb.velocity.y);

        UpdateLean();
        HandleStep(h);
    }

    void UpdateLean()
    {
        if (torsoBalance == null || rb == null) return;
        float target = Mathf.Clamp(-rb.velocity.x * leanFactor, -maxLean, maxLean);
        torsoBalance.targetRotation = Mathf.LerpAngle(
            torsoBalance.targetRotation, target, leanSmoothing * Time.fixedDeltaTime);
    }

    void HandleStep(float h)
    {
        bool leftGrounded  = leftFootPos  != null && Physics2D.OverlapCircle(leftFootPos.position,  positionRadius, ground);
        bool rightGrounded = rightFootPos != null && Physics2D.OverlapCircle(rightFootPos.position, positionRadius, ground);

        if (Mathf.Abs(h) < 0.1f || (!leftGrounded && !rightGrounded))
        {
            _walkTimer = 0f;
            return;
        }

        _walkTimer += Time.fixedDeltaTime;
        if (_walkTimer < stepWait) return;

        _walkTimer = 0f;
        bool wantLeft  = _leftStep  && leftGrounded;
        bool wantRight = !_leftStep && rightGrounded;

        Rigidbody2D stepping = null;
        if      (wantLeft)  stepping = _leftLegRb;
        else if (wantRight) stepping = _rightLegRb;

        if (stepping != null)
        {
            stepping.AddForce(Vector2.up * stepLiftForce, ForceMode2D.Impulse);
            rb.AddForce(Vector2.up * bodyBounce, ForceMode2D.Impulse);
        }

        _leftStep = !_leftStep;
    }

    void TryJump()
    {
        if (rb == null) return;

        // Check feet first, then fall back to a broad torso-level cast so
        // ragdoll foot wobble doesn't prevent jumping when clearly on the ground
        bool leftGrounded   = leftFootPos  != null && Physics2D.OverlapCircle(leftFootPos.position,  positionRadius,        ground);
        bool rightGrounded  = rightFootPos != null && Physics2D.OverlapCircle(rightFootPos.position, positionRadius,        ground);
        bool centerGrounded = playerPos    != null && Physics2D.OverlapCircle(playerPos.position,    positionRadius * 2.5f, ground);

        if (!leftGrounded && !rightGrounded && !centerGrounded) return;

        // Grounded — consume buffer and launch
        _jumpBufferFrames = 0;
        rb.velocity = new Vector2(rb.velocity.x, _jumpForce);

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayJumpSFX();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public int GetPlayerSlotPublic() => GetPlayerSlot();

    int GetPlayerSlot()
    {
        // OwnerClientId is set at spawn time and available on all clients.
        // NGO assigns IDs incrementally (host = 0, first client = 1, etc.) and
        // ConnectedClientsIds preserves insertion order, so the index is stable.
        int i = 0;
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (id == OwnerClientId) return i;
            i++;
        }
        return 0;
    }

    public void SetInputEnabled(bool enabled)
    {
        _inputEnabled = enabled;
        if (!enabled && rb != null) rb.velocity = Vector2.zero;
    }

    /// <summary>
    /// Broadcasts input enable/disable to the owning client so their input gate stays in sync.
    /// Call this from the server instead of SetInputEnabled when you need it to reach the client.
    /// </summary>
    [ClientRpc]
    public void SetInputEnabledClientRpc(bool enabled)
    {
        if (!IsOwner) return;
        SetInputEnabled(enabled);
    }

    /// <summary>
    /// Called by KillBox (server only). Applies an impulse and suppresses horizontal
    /// input for a short window so HandleMove can't immediately cancel the knockback.
    /// </summary>
    public void ApplyKnockback(Vector2 force, float suppressDuration = 0.35f)
    {
        if (rb == null) return;
        rb.velocity = new Vector2(0f, rb.velocity.y);   // clear horizontal so impulse isn't fighting it
        rb.AddForce(force, ForceMode2D.Impulse);
        StartCoroutine(KnockbackSuppressCoroutine(suppressDuration));
    }

    System.Collections.IEnumerator KnockbackSuppressCoroutine(float duration)
    {
        _suppressHorizontal = true;
        yield return new WaitForSeconds(duration);
        _suppressHorizontal = false;
    }
}
