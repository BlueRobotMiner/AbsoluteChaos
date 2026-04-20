using Unity.Collections;
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
    [SerializeField] float _jumpHeight = 3f;  // target apex height in world units

    // ── Base stat cache (populated in Start; card effects multiply against these) ───
    [HideInInspector] public float baseSpeed;
    int _maxJumps      = 1;   // 1 = single jump; 2 = double jump (DoubleJump card)
    int _jumpsRemaining;

    [Header("Ground Check")]
    public Transform playerPos;
    public Transform leftFootPos;
    public Transform rightFootPos;
    public float     positionRadius;
    public LayerMask ground;

    [Header("Gun")]
    [SerializeField] Transform _gunHandAttach;
    [SerializeField] Transform _leftGunHandAttach;
    public Transform GunHandAttach     => _gunHandAttach;
    public Transform LeftGunHandAttach => _leftGunHandAttach;

    [Header("Spawn Anchor")]
    [SerializeField] Transform _spawnAnchor;   // drag "Spawn Player" GO here

    [Header("Animation")]
    public Animator anim;

    [Header("Player Colors")]
    [SerializeField] Color[] _slotColors =
    {
        Color.white,
        new(1f, 0.3f, 0.3f, 1f),
        new(0.2f, 1f, 0.4f, 1f),
    };

    [Header("Head Types")]
    [SerializeField] GameObject[] _headTypeObjects;   // drag each head variant GO here (0=circle, 1=diamond, 2=polygon)

    // ── Customization NetworkVariables (owner writes, everyone reads) ─────────
    // Owner pushes their saved settings on spawn; all clients apply them visually.
    NetworkVariable<FixedString64Bytes> _netPlayerName = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    NetworkVariable<int> _netHeadType = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    NetworkVariable<Color32> _netColor = new(
        new Color32(255, 255, 255, 255), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // ── Synced state (server writes, everyone reads) ───────────────────────
    NetworkVariable<Vector2> _syncedPosition = new NetworkVariable<Vector2>(
        Vector2.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    NetworkVariable<int> _syncedAnimDir = new NetworkVariable<int>(
        0,
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
        baseSpeed = speed;
    }

    // ── NGO ────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        // Only the local owner sees through their camera
        Camera cam = GetComponentInChildren<Camera>();
        if (cam != null) cam.enabled = IsOwner;

        // Subscribe to customization changes — fires on all clients whenever owner updates
        _netColor.OnValueChanged    += OnNetColorChanged;
        _netHeadType.OnValueChanged += OnNetHeadTypeChanged;

        if (IsOwner)
        {
            // Push saved customization settings to all clients on spawn
            var d = SaveLoadManager.Instance?.Data ?? new PlayerSaveData();
            _netPlayerName.Value = new FixedString64Bytes(d.playerName);
            _netHeadType.Value   = d.headTypeIndex;
            _netColor.Value      = (Color32)d.ToColor();
        }

        // Apply current values immediately (handles host and late-joiners)
        ApplyColor(_netColor.Value);
        ApplyHeadType(_netHeadType.Value);

        if (IsServer)
        {
            // Server owns all physics — hold root kinematic for 2 frames so spawn position settles
            if (rb != null) rb.isKinematic = true;
            StartCoroutine(UnlockPhysicsAfterSync());
        }
        else
        {
            // Clients are pure renderers — disable ALL rigidbodies completely
            // true = include inactive GOs (head variant GOs are inactive until ApplyHeadType runs)
            foreach (var r in GetComponentsInChildren<Rigidbody2D>(true))
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
        _pendingH           = 0f;
        _pendingJump        = false;
        _jumpBufferFrames   = 0;
        _lastSentH          = 0f;
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
        // _spawnAnchor ("Spawn Player" GO) is a static child of the root — its localPosition
        // is a fixed offset from the root rb. We shift the root so the anchor lands on spawnPos.
        Vector2 anchorOffset = _spawnAnchor != null ? (Vector2)_spawnAnchor.localPosition : Vector2.zero;
        Vector2 rootTarget   = spawnPos - anchorOffset;

        if (IsServer)
        {
            // Shift every ragdoll body by the same delta so joints don't fight each other.
            // includeInactive:true so inactive head variant bodies also get repositioned.
            if (rb != null)
            {
                Vector2 delta = rootTarget - rb.position;
                foreach (var r in GetComponentsInChildren<Rigidbody2D>())
                {
                    r.position       += delta;
                    r.velocity        = Vector2.zero;
                    r.angularVelocity = 0f;
                }
            }

            _syncedPosition.Value = rootTarget;

            // Force-broadcast new positions so clients snap rather than lerp from old spot
            GetComponent<RagdollSync>()?.BroadcastCurrentPositions();
        }
        else
        {
            // Root snap — RagdollSync.SnapStateClientRpc (sent by server above) handles the limbs
            transform.position = new Vector3(rootTarget.x, rootTarget.y, 0f);
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
            if (nearest != null)
                AudioManager.Instance?.PlayCardPickSFX();
        }

        // ── Calculate h and send movement to server ─────────────────────
        // Use transform.position — rb.position is stale on non-server clients
        // because their Rigidbody2D has simulated=false and never gets physics updates.
        float h = 0f;
        if (nearest != null)
        {
            float myX     = transform.position.x;
            float targetX = nearest.WorldTarget != null ? nearest.WorldTarget.position.x : myX;
            float dx      = targetX - myX;
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

        bool leftGrounded   = leftFootPos  != null && Physics2D.OverlapCircle(leftFootPos.position,  positionRadius,        ground);
        bool rightGrounded  = rightFootPos != null && Physics2D.OverlapCircle(rightFootPos.position, positionRadius,        ground);
        bool centerGrounded = playerPos    != null && Physics2D.OverlapCircle(playerPos.position,    positionRadius * 2.5f, ground);
        bool isGrounded     = leftGrounded || rightGrounded || centerGrounded;

        // Refill jumps on landing
        if (isGrounded) _jumpsRemaining = _maxJumps;

        if (_jumpsRemaining <= 0) return;

        // Consume the jump (and the input buffer)
        _jumpBufferFrames = 0;
        _jumpsRemaining--;

        // v = sqrt(2 * |g_effective| * h) — reaches exactly _jumpHeight apex
        float effectiveGravity = Mathf.Abs(Physics2D.gravity.y) * Mathf.Max(rb.gravityScale, 0.01f);
        float jumpVelocity     = Mathf.Sqrt(2f * effectiveGravity * _jumpHeight);
        rb.velocity = new Vector2(rb.velocity.x, jumpVelocity);

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayJumpSFX();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public int GetPlayerSlotPublic() => GetPlayerSlot();

    /// <summary>Returns the player's custom display name, or "Player {slot+1}" if they left the default.</summary>
    public string PlayerDisplayName
    {
        get
        {
            string n = _netPlayerName.Value.ToString().Trim();
            return (string.IsNullOrEmpty(n) || n == "Player")
                ? $"Player {GetPlayerSlot() + 1}"
                : n;
        }
    }

    /// <summary>Returns the player's chosen character color.</summary>
    public Color PlayerColor => (Color)(Color32)_netColor.Value;

    /// <summary>Finds the PlayerController that owns the given slot index (0/1/2).</summary>
    public static PlayerController GetBySlot(int slot)
    {
        foreach (var pc in FindObjectsOfType<PlayerController>(true))
            if (pc.GetPlayerSlotPublic() == slot) return pc;
        return null;
    }

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

    public bool IsInputEnabled => _inputEnabled;

    public void SetInputEnabled(bool enabled)
    {
        _inputEnabled = enabled;
        if (!enabled && rb != null)
        {
            rb.velocity       = Vector2.zero;
            _pendingJump      = false;
            _jumpBufferFrames = 0;
            _jumpsRemaining   = 0;
        }
    }

    /// <summary>
    /// Broadcasts input enable/disable to the owning client so their input gate stays in sync.
    /// Call this from the server instead of SetInputEnabled when you need it to reach the client.
    /// </summary>
    [ClientRpc]
    public void SetInputEnabledClientRpc(bool enabled)
    {
        // Owner: gates client-side input capture.
        // Server: gates server-side physics so buffered input isn't processed during countdown.
        if (!IsOwner && !IsServer) return;
        SetInputEnabled(enabled);
    }

    /// <summary>
    /// Called by PauseManager on the owning client. Freezes/unfreezes this player's
    /// physics on the server without affecting any other player.
    /// </summary>
    [ServerRpc]
    public void SetInputEnabledServerRpc(bool enabled)
    {
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

    // ── Card effect API (server only) ─────────────────────────────────────

    public void SetMaxJumps(int n)  => _maxJumps = n;
    public int  GetMaxJumps()       => _maxJumps;
    public void ResetBaseStats()
    {
        speed     = baseSpeed;
        _maxJumps = 1;
        _jumpsRemaining = 0;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _netColor.OnValueChanged    -= OnNetColorChanged;
        _netHeadType.OnValueChanged -= OnNetHeadTypeChanged;
    }

    // ── Customization helpers ──────────────────────────────────────────────

    void OnNetColorChanged(Color32 prev, Color32 current)    => ApplyColor(current);
    void OnNetHeadTypeChanged(int prev, int current)         => ApplyHeadType(current);

    void ApplyColor(Color32 col)
    {
        foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
            sr.color = col;
    }

    void ApplyHeadType(int index)
    {
        if (_headTypeObjects == null) return;
        for (int i = 0; i < _headTypeObjects.Length; i++)
        {
            if (_headTypeObjects[i] == null) continue;
            _headTypeObjects[i].SetActive(i == index);
        }
    }

}
