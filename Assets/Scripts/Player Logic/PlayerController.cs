using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    public GameObject  leftLeg;
    public GameObject  rightLeg;
    public Rigidbody2D rb;           // Body Rigidbody2D — assign in Inspector
    public float       speed = 1.5f;

    [Header("Step Feel")]
    public float stepWait      = 0.25f;  // seconds between alternate foot lifts
    public float stepLiftForce = 1.5f;   // upward impulse on the stepping foot
    public float bodyBounce    = 0.3f;   // micro body bob each step (set 0 to disable)
    public float stoppingDrag  = 12f;    // how fast body kills horizontal slide

    [Header("Lean")]
    public BalanceController torsoBalance;  // drag Torso's BalanceController here
    public float leanFactor    = 5f;        // velocity multiplier for lean angle
    public float maxLean       = 20f;       // max degrees of tilt
    public float leanSmoothing = 8f;        // lean interpolation speed

    [Header("Jump")]
    [SerializeField] float _jumpForce = 10f;

    [Header("Ground Check")]
    public Transform playerPos;       // centre — used for jump check
    public Transform leftFootPos;     // empty GO at left foot
    public Transform rightFootPos;    // empty GO at right foot
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

    // Owner writes position — everyone else reads it (mirrors Pong paddle sync pattern)
    NetworkVariable<Vector2> _syncedPosition = new NetworkVariable<Vector2>(
        Vector2.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    Rigidbody2D _leftLegRb;
    Rigidbody2D _rightLegRb;
    bool  _inputEnabled = true;
    bool  _leftStep     = true;
    float _walkTimer;

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
        Camera cam = GetComponentInChildren<Camera>();
        if (cam != null) cam.enabled = IsOwner;

        // Color by slot
        int slot = GetPlayerSlot();
        Color col = slot < _slotColors.Length ? _slotColors[slot] : Color.white;
        foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
            sr.color = col;

        if (!IsOwner)
        {
            // Non-owners: root RB stays kinematic forever — position driven by _syncedPosition
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.simulated   = false;
            }
        }
        else
        {
            // Owner: wait 2 physics frames so _syncedPosition has a valid value before releasing physics
            if (rb != null) rb.isKinematic = true;
            StartCoroutine(UnlockPhysicsAfterSync());
        }
    }

    System.Collections.IEnumerator UnlockPhysicsAfterSync()
    {
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        if (rb != null) rb.isKinematic = false;
    }

    public override void OnGainedOwnership()
    {
        Camera cam = GetComponentInChildren<Camera>();
        if (cam != null) cam.enabled = true;
        _inputEnabled = true;
    }

    public override void OnLostOwnership()
    {
        Camera cam = GetComponentInChildren<Camera>();
        if (cam != null) cam.enabled = false;
        _inputEnabled = false;
    }

    // ── Input guard ────────────────────────────────────────────────────────

    bool CanInput => _inputEnabled && (IsOwner || !IsSpawned);

    // ── Update ─────────────────────────────────────────────────────────────

    void Update()
    {
        if (!CanInput) return;
        if (_draftMode) { HandleDraftInput(); return; }
        HandleJump();
        HandleAnimation();
    }

    void FixedUpdate()
    {
        if (!IsSpawned) return;

        if (!IsOwner)
        {
            // Non-owner: read the owner's broadcast position and follow it
            if (rb != null)
                rb.MovePosition(_syncedPosition.Value);
            return;
        }

        if (!CanInput) return;
        HandleMove();

        // Owner: broadcast current position so all other clients can follow
        if (rb != null)
            _syncedPosition.Value = rb.position;
    }

    // ── Animation ──────────────────────────────────────────────────────────

    void HandleAnimation()
    {
        float h = Input.GetAxisRaw("Horizontal");
        if      (h > 0) anim.Play("WalkRight");
        else if (h < 0) anim.Play("WalkLeft");
        else            anim.Play("Idle");
    }

    // ── Draft mode ─────────────────────────────────────────────────────────

    public void SetDraftMode(bool active, CardSlot[] cards, CardDraftingUI manager)
    {
        _draftMode    = active;
        _draftCards   = cards;
        _draftManager = manager;
        if (!active && _hoveredDraftCard != null)
        {
            _hoveredDraftCard.IsHovered = false;
            _hoveredDraftCard = null;
        }
    }

    void HandleDraftInput()
    {
        if (_draftCards == null || Camera.main == null) return;

        // Find the card closest to the mouse in screen space
        Vector2 mouseScreen = Input.mousePosition;
        CardSlot nearest    = null;
        float   minDist     = float.MaxValue;

        foreach (var card in _draftCards)
        {
            // Convert each card's RectTransform screen position for comparison
            Vector2 cardScreen = RectTransformUtility.WorldToScreenPoint(null, card.transform.position);
            float d = Vector2.Distance(mouseScreen, cardScreen);
            if (d < minDist) { minDist = d; nearest = card; }
        }

        // Update hover highlight
        foreach (var card in _draftCards)
            card.IsHovered = (card == nearest);
        _hoveredDraftCard = nearest;

        // Play walk animation toward hovered card's world target
        if (nearest != null && rb != null)
        {
            float targetX = nearest.WorldTarget != null
                ? nearest.WorldTarget.position.x
                : rb.position.x;
            float dx = targetX - rb.position.x;
            float h  = Mathf.Abs(dx) > 0.3f ? Mathf.Sign(dx) : 0f;
            if      (h > 0) anim.Play("WalkRight");
            else if (h < 0) anim.Play("WalkLeft");
            else            anim.Play("Idle");
        }

        // Space confirms the pick
        if (Input.GetKeyDown(KeyCode.Space) && _hoveredDraftCard != null && _draftManager != null)
        {
            _draftMode = false;
            foreach (var card in _draftCards) card.IsHovered = false;
            _draftManager.SubmitPickServerRpc(GetPlayerSlotPublic(), (int)_hoveredDraftCard.CardId);
        }
    }

    // ── Movement ───────────────────────────────────────────────────────────

    void HandleMove()
    {
        if (_draftMode && rb != null)
        {
            // Walk character toward the hovered card's X position
            float draftH = 0f;
            if (_hoveredDraftCard != null)
            {
                float targetX = _hoveredDraftCard.WorldTarget != null
                    ? _hoveredDraftCard.WorldTarget.position.x
                    : rb.position.x;
                float dx = targetX - rb.position.x;
                draftH = Mathf.Abs(dx) > 0.3f ? Mathf.Sign(dx) : 0f;
            }
            if (draftH != 0)
                rb.MovePosition(rb.position + Vector2.right * (draftH * speed * Time.fixedDeltaTime));
            else
                rb.velocity = new Vector2(rb.velocity.x * (1f - stoppingDrag * Time.fixedDeltaTime), rb.velocity.y);
            UpdateLean();
            HandleStep(draftH);
            return;
        }

        float h = Input.GetAxisRaw("Horizontal");

        // Smooth body translation — everything else follows via joints
        if (h != 0 && rb != null)
            rb.MovePosition(rb.position + Vector2.right * (h * speed * Time.fixedDeltaTime));

        // Kill horizontal slide when input releases
        if (Mathf.Abs(h) < 0.1f && rb != null)
            rb.velocity = new Vector2(
                rb.velocity.x * (1f - stoppingDrag * Time.fixedDeltaTime),
                rb.velocity.y);

        UpdateLean();
        HandleStep(h);
    }

    // Lean follows actual velocity — smooth and physics-accurate
    void UpdateLean()
    {
        if (torsoBalance == null || rb == null) return;
        float target = Mathf.Clamp(-rb.velocity.x * leanFactor, -maxLean, maxLean);
        torsoBalance.targetRotation = Mathf.LerpAngle(
            torsoBalance.targetRotation, target, leanSmoothing * Time.fixedDeltaTime);
    }

    // Per-foot ground detection — only lifts a foot that is actually touching ground
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

        if (_walkTimer >= stepWait)
        {
            _walkTimer = 0f;

            // Lift whichever foot is grounded this step; skip if that foot is already mid-air
            bool wantLeft  = _leftStep  && leftGrounded;
            bool wantRight = !_leftStep && rightGrounded;

            Rigidbody2D stepping = null;
            if      (wantLeft)  stepping = _leftLegRb;
            else if (wantRight) stepping = _rightLegRb;

            if (stepping != null)
            {
                stepping.AddForce(Vector2.up * stepLiftForce, ForceMode2D.Impulse);
                if (rb != null)
                    rb.AddForce(Vector2.up * bodyBounce, ForceMode2D.Impulse);
            }

            _leftStep = !_leftStep;
        }
    }

    // ── Jump ───────────────────────────────────────────────────────────────

    void HandleJump()
    {
        bool isOnGround = Physics2D.OverlapCircle(playerPos.position, positionRadius, ground);

        if (isOnGround && Input.GetKeyDown(KeyCode.Space))
        {
            rb.AddForce(Vector2.up * _jumpForce);

            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayJumpSFX();
        }
    }

    // ── Gun pickup ─────────────────────────────────────────────────────────

    // ── Helpers ────────────────────────────────────────────────────────────

    public int GetPlayerSlotPublic() => GetPlayerSlot();

    int GetPlayerSlot()
    {
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
}
