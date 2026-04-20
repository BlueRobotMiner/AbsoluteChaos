using TMPro;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class Gun : NetworkBehaviour
{
    [SerializeField] Transform  _handAttachPoint;
    [SerializeField] Transform  _leftHandGrip;      // left-hand grip — empty child GO on prefab
    [SerializeField] Transform  _firePoint;
    [SerializeField] float      _throwForce        = 10f;
    [SerializeField] float      _throwGravityScale = 2f;
    [SerializeField] LayerMask  _groundLayer;

    [Header("Bullet Settings")]
    [SerializeField] float _bulletGravityScale   = 0f;   // 0 = flat hitscan, positive = bullet drop
    [SerializeField] float _bulletSpeedOverride  = 0f;   // 0 = use Projectile's default speed
    [SerializeField] int   _bulletDamageOverride = 0;    // 0 = use Projectile's default damage
    [SerializeField] float _fireRateOverride     = 0f;   // 0 = use PlayerCombat's default fire rate

    public float BulletGravityScale    => _bulletGravityScale;
    public float BulletSpeedOverride   => _bulletSpeedOverride;
    public int   BulletDamageOverride  => _bulletDamageOverride;
    public float FireRateOverride      => _fireRateOverride;

    [Header("Ammo")]
    [SerializeField] int    _maxAmmo    = 0;    // 0 = infinite
    [SerializeField] TMP_Text _ammoDisplay;     // drag a world-space TMP Text child here
    [SerializeField] float    _ammoDisplayYOffset = 0.3f;  // world-space units above gun center

    NetworkVariable<int> _ammoRemaining = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // Synced so all clients display the correct max regardless of prefab variant
    NetworkVariable<int> _netMaxAmmo = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Server-authoritative state
    NetworkVariable<bool>  _networkHeld    = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    NetworkVariable<ulong> _holderNetObjId = new(0,     NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    Rigidbody2D      _rb;
    NetworkTransform _netTransform;   // disabled on spawn — we use manual sync instead
    Collider2D[]     _allColliders;
    bool             _landed;

    SpriteRenderer _sr;
    Transform      _srTransform;
    float          _srBaseScaleX;


    // ── Manual sync — same pattern as RagdollSync ──────────────────────────

    [Header("Sync")]
    [SerializeField] int   _sendEveryNFrames = 3;    // ~16/sec at 50Hz physics
    [SerializeField] float _interpSpeed      = 25f;  // client lerp speed toward received state
    [SerializeField] float _posThreshold     = 0.02f;
    [SerializeField] float _rotThreshold     = 1f;

    int     _frameCounter;
    Vector2 _lastSentPos;
    float   _lastSentRot;

    // Client: interpolation targets set by ServerRpcs
    Vector2 _targetPos;
    float   _targetRot;
    bool    _hasTarget;

    // ── Other runtime state ────────────────────────────────────────────────

    [Header("Spin while falling")]
    [SerializeField] float _spinSpeed = 360f;

    float _spinAngle;
    float _pickupCooldownEnd;
    bool  _clearingHolder;

    // ── Unity ──────────────────────────────────────────────────────────────

    void Awake()
    {
        _rb           = GetComponent<Rigidbody2D>();
        _netTransform = GetComponent<NetworkTransform>();
        _allColliders = GetComponents<Collider2D>();

        _sr           = GetComponentInChildren<SpriteRenderer>();
        _srTransform  = _sr != null ? _sr.transform : null;
        float rawScaleX = _srTransform != null ? _srTransform.localScale.x : 1f;
        _srBaseScaleX = Mathf.Abs(rawScaleX) > 0.001f ? rawScaleX : 1f;

    }

    public override void OnNetworkSpawn()
    {
        if (_netTransform != null) _netTransform.enabled = false;

        _networkHeld.OnValueChanged    += OnHeldChanged;
        _holderNetObjId.OnValueChanged += OnHolderChanged;
        _ammoRemaining.OnValueChanged  += OnAmmoChanged;
        _netMaxAmmo.OnValueChanged     += (_, v) => UpdateAmmoDisplay(_ammoRemaining.Value);

        if (IsServer && _maxAmmo > 0)
        {
            _ammoRemaining.Value = _maxAmmo;
            _netMaxAmmo.Value    = _maxAmmo;
        }

        // Hide ammo display until picked up
        if (_ammoDisplay != null)
            _ammoDisplay.gameObject.SetActive(false);

        UpdateAmmoDisplay(_ammoRemaining.Value);
        IgnorePlayerCollisions();

        if (_networkHeld.Value)
            AttachToHolder(_holderNetObjId.Value);
    }

    // ── FixedUpdate: server broadcasts, clients lerp ───────────────────────

    void FixedUpdate()
    {
        if (!IsSpawned) return;

        if (IsServer)
        {
            // Broadcast while held OR airborne so P2 always has a server position to interpolate
            if (!_landed)
                ServerBroadcast();
        }
        else
        {
            // Non-holder clients: interpolate toward server broadcast for both held and airborne
            bool holderIsLocal = IsHeldByLocalPlayer();
            if (!holderIsLocal && _hasTarget)
            {
                float t = _interpSpeed * Time.fixedDeltaTime;
                transform.position = Vector2.Lerp(
                    (Vector2)transform.position, _targetPos, t);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.Euler(0f, 0f, _targetRot),
                    t);
            }
        }
    }

    void ServerBroadcast()
    {
        _frameCounter++;
        if (_frameCounter % _sendEveryNFrames != 0) return;

        Vector2 pos = _rb.position;
        float   rot = _rb.rotation;

        if (Vector2.Distance(pos, _lastSentPos) > _posThreshold ||
            Mathf.Abs(Mathf.DeltaAngle(rot, _lastSentRot)) > _rotThreshold)
        {
            _lastSentPos = pos;
            _lastSentRot = rot;
            SyncGunStateClientRpc(pos, rot);
        }
    }

    [ClientRpc]
    void SyncGunStateClientRpc(Vector2 pos, float rot)
    {
        if (IsServer) return;
        _targetPos = pos;
        _targetRot = rot;
        _hasTarget = true;
    }

    // ── Physics landing ────────────────────────────────────────────────────

    void OnCollisionEnter2D(Collision2D col)
    {
        if (!IsServer || _landed || _networkHeld.Value) return;
        if ((_groundLayer.value & (1 << col.gameObject.layer)) == 0) return;

        _landed             = true;
        _rb.velocity        = Vector2.zero;
        _rb.angularVelocity = 0f;
        _rb.gravityScale    = 1f;
        _rb.isKinematic     = true;

        // Send final resting position so clients snap cleanly to the landed spot
        SnapGunClientRpc(_rb.position, _rb.rotation);
    }

    // ── Pickup ─────────────────────────────────────────────────────────────

    void OnTriggerEnter2D(Collider2D col)
    {
        if (!IsServer || _networkHeld.Value) return;
        if (Time.time < _pickupCooldownEnd) return;
        if (IsEmpty()) return;   // empty guns can't be picked up
        PlayerController pc = col.GetComponentInParent<PlayerController>();
        if (pc == null) return;
        DoPickup(pc);
    }

    void DoPickup(PlayerController pc)
    {
        foreach (var other in FindObjectsOfType<Gun>())
            if (other != this && other.IsHeldBy(pc.NetworkObjectId)) return;

        _networkHeld.Value    = true;
        _holderNetObjId.Value = pc.NetworkObjectId;
        _landed               = false;   // allow ServerBroadcast to run while gun is held

        // Free the spawn point so a replacement gun can appear
        ItemSpawner.Instance?.NotifyItemPickedUp(NetworkObject);

        _rb.isKinematic = true;
        _rb.velocity    = Vector2.zero;

        foreach (var col in _allColliders) col.enabled = false;

        // Server runs all arm physics — wire up the arm controller here so FixedUpdate sees the grip
        var serverAim = pc.GetComponentInChildren<ArmAimController>();
        if (serverAim != null)
        {
            serverAim.SetGunRenderer(GetComponentInChildren<SpriteRenderer>());
            serverAim.SetLeftGrip(_leftHandGrip);
        }

        // Wire up PlayerCombat on the server so FireServerRpc can read gun properties
        var combat = pc.GetComponent<PlayerCombat>();
        if (combat != null)
        {
            combat.SetHeldGun(this);
            if (_fireRateOverride > 0f) combat.SetFireRate(1f / _fireRateOverride);
            if (_maxAmmo > 0 && combat.ammoMultiplier > 1f)
            {
                int multiplied = Mathf.RoundToInt(_maxAmmo * combat.ammoMultiplier);
                _ammoRemaining.Value = multiplied;
                _netMaxAmmo.Value    = multiplied;   // sync display max so P2 sees correct mag size
            }
        }

        NotifyOwnerPickupClientRpc(pc.OwnerClientId);
    }

    [ClientRpc]
    void NotifyOwnerPickupClientRpc(ulong ownerClientId)
    {
        if (NetworkManager.Singleton.LocalClientId != ownerClientId) return;

        foreach (var pc in FindObjectsOfType<PlayerController>())
        {
            if (pc.OwnerClientId != ownerClientId) continue;

            var aim = pc.GetComponentInChildren<ArmAimController>();
            if (aim != null)
            {
                aim.SetGunRenderer(GetComponentInChildren<SpriteRenderer>());
                aim.SetLeftGrip(_leftHandGrip);
            }

            if (pc.TryGetComponent(out PlayerCombat combat))
            {
                combat.SetHeldGun(this);
                // Mirror the same fire rate the server applied so the client-side
                // _nextFireTime gate matches — owner sends FireServerRpc at the right cadence
                if (_fireRateOverride > 0f) combat.SetFireRate(1f / _fireRateOverride);
                else                        combat.SetFireRate(combat.baseFireRate);
            }

            break;
        }
    }

    // ── Update: spin animation + hand follow ───────────────────────────────

    void Update()
    {
        // Coin-flip spin only once the gun has landed — not while airborne
        if (IsSpawned && !_networkHeld.Value && _landed)
        {
            _spinAngle += _spinSpeed * Time.deltaTime;
            if (_srTransform != null)
            {
                float sine   = Mathf.Sin(_spinAngle * Mathf.Deg2Rad);
                float scaleX = Mathf.Abs(sine) * _srBaseScaleX;
                _srTransform.localScale = new Vector3(scaleX, _srTransform.localScale.y, _srTransform.localScale.z);
                if (_sr != null) _sr.flipX = sine < 0f;
            }
        }
        else if (_networkHeld.Value)
        {
            _spinAngle = 0f;
            if (_srTransform != null)
                _srTransform.localScale = new Vector3(_srBaseScaleX, _srTransform.localScale.y, _srTransform.localScale.z);
            if (_sr != null) _sr.flipX = false;
        }

        if (!IsSpawned || !_networkHeld.Value || _clearingHolder) return;

        // Keep ammo text world-upright for ALL clients — position follows whatever transform.position
        // is on this client (authoritative on server, interpolated on P2 via FixedUpdate)
        if (_ammoDisplay != null && _ammoDisplay.gameObject.activeSelf)
        {
            _ammoDisplay.transform.position = transform.position + Vector3.up * _ammoDisplayYOffset;
            _ammoDisplay.transform.rotation = Quaternion.identity;
        }

        // Server always follows the hand — this drives the authoritative position that gets broadcast.
        // Non-server clients only follow hand if they are the holder; otherwise FixedUpdate
        // interpolation toward the server broadcast handles it (P2 watching P1 hold).
        // Flip based on current rotation — must run on ALL clients so P2 sees the correct orientation
        if (_networkHeld.Value)
        {
            float angle = transform.eulerAngles.z;
            if (angle > 180f) angle -= 360f;
            SetFlipY(Mathf.Abs(angle) > 90f);
        }

        if (!IsServer && !IsHeldByLocalPlayer()) return;

        Transform hand = GetHolderHand(_holderNetObjId.Value);
        if (hand == null) return;

        transform.position = hand.position;
        transform.rotation = hand.rotation;
    }

    // ── NetworkVariable callbacks ──────────────────────────────────────────

    void OnHeldChanged(bool prev, bool current)
    {
        _clearingHolder = false;
        int maxAmmo = _netMaxAmmo.Value > 0 ? _netMaxAmmo.Value : _maxAmmo;
        if (_ammoDisplay != null)
            _ammoDisplay.gameObject.SetActive(current && maxAmmo > 0);
        if (current)
        {
            _landed = false;   // stop spin when picked up on clients
            UpdateAmmoDisplay(_ammoRemaining.Value);
        }
        if (!current)
        {
            SetFlipY(false);
            return;
        }
        AttachToHolder(_holderNetObjId.Value);
    }

    void OnHolderChanged(ulong prev, ulong current)
    {
        if (!_networkHeld.Value) return;
        AttachToHolder(current);
    }

    void AttachToHolder(ulong holderNetObjId)
    {
        if (_rb != null)
        {
            _rb.isKinematic = true;
            _rb.velocity    = Vector2.zero;
        }
        foreach (var col in _allColliders) col.enabled = false;
    }

    Transform GetHolderHand(ulong holderNetObjId)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects
            .TryGetValue(holderNetObjId, out var netObj)) return null;
        var pc = netObj.GetComponent<PlayerController>();
        return pc != null ? pc.GunHandAttach : null;
    }

    // ── Throw ──────────────────────────────────────────────────────────────

    public void Throw(Vector2 throwDir, Vector2 clientGunPos)
    {
        if (!IsServer) return;

        transform.position = new Vector3(clientGunPos.x, clientGunPos.y, transform.position.z);

        ulong prevHolder      = _holderNetObjId.Value;
        _networkHeld.Value    = false;
        _holderNetObjId.Value = 0;
        _landed               = false;

        _rb.isKinematic     = false;
        _rb.gravityScale    = _throwGravityScale;
        _rb.velocity        = throwDir * _throwForce;
        _rb.angularVelocity = 0f;
        _rb.rotation        = 0f;   // reset to flat — don't inherit arm aim angle

        _pickupCooldownEnd = Time.time + 0.5f;

        foreach (var col in _allColliders) col.enabled = true;

        // Clear server-side gun + arm state immediately
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects
                .TryGetValue(prevHolder, out var holderObj))
        {
            if (holderObj.TryGetComponent(out PlayerCombat serverCombat))
            {
                serverCombat.SetHeldGun(null);
                serverCombat.SetFireRate(serverCombat.baseFireRate);
            }
            var serverAim = holderObj.GetComponentInChildren<ArmAimController>();
            if (serverAim != null) { serverAim.SetGunRenderer(null); serverAim.SetLeftGrip(null); }
        }

        ClearHolderClientRpc(prevHolder, transform.position, Quaternion.identity);
    }

    [ClientRpc]
    void ClearHolderClientRpc(ulong holderNetObjId, Vector3 throwPos, Quaternion throwRot)
    {
        _clearingHolder = true;

        // Snap to the throw origin immediately
        transform.position = throwPos;
        transform.rotation = throwRot;

        // Seed interpolation targets at the throw origin so lerp starts from here,
        // not from wherever the client last saw the gun (avoids the zip entirely)
        _targetPos = throwPos;
        _targetRot = throwRot.eulerAngles.z;
        _hasTarget = true;

        if (_sr != null) _sr.flipY = false;

        foreach (var col in _allColliders) col.enabled = true;
        _landed = false;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects
                .TryGetValue(holderNetObjId, out var netObj)) return;

        if (netObj.TryGetComponent(out PlayerCombat combat))
        {
            combat.SetHeldGun(null);
            combat.SetFireRate(combat.baseFireRate);   // restore base rate on the owner's client
        }
        var aim = netObj.GetComponentInChildren<ArmAimController>();
        if (aim != null) { aim.SetGunRenderer(null); aim.SetLeftGrip(null); }
    }

    [ClientRpc]
    void SnapGunClientRpc(Vector2 pos, float rot)
    {
        if (IsServer) return;
        // Immediate snap — used when gun lands so clients jump to exact rest position
        _targetPos         = pos;
        _targetRot         = rot;
        _hasTarget         = true;
        _landed            = true;   // enable spin animation on clients
        transform.position = new Vector3(pos.x, pos.y, transform.position.z);
        transform.rotation = Quaternion.Euler(0f, 0f, rot);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    void IgnorePlayerCollisions()
    {
        Collider2D solidCol = null;
        foreach (var col in _allColliders)
            if (!col.isTrigger) { solidCol = col; break; }
        if (solidCol == null) return;

        foreach (var pc in FindObjectsOfType<PlayerController>(true))
            foreach (var col in pc.GetComponentsInChildren<Collider2D>(true))
                Physics2D.IgnoreCollision(solidCol, col, true);
    }

    public bool IsEmpty() => _maxAmmo > 0 && _ammoRemaining.Value <= 0;

    public bool IsHeldBy(ulong networkObjectId) =>
        _networkHeld.Value && _holderNetObjId.Value == networkObjectId;

    bool IsHeldByLocalPlayer()
    {
        if (!_networkHeld.Value) return false;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects
                .TryGetValue(_holderNetObjId.Value, out var holderNet)) return false;
        return holderNet.OwnerClientId == NetworkManager.Singleton.LocalClientId;
    }

    public void ScheduleDespawn(float delay)
    {
        if (!IsServer) return;
        StartCoroutine(DespawnAfterDelay(delay));
    }

    System.Collections.IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (IsSpawned && !_networkHeld.Value)
            DespawnSelf();
    }

    public void DespawnSelf()
    {
        if (!IsServer) return;
        ulong prevHolder      = _holderNetObjId.Value;
        _networkHeld.Value    = false;
        _holderNetObjId.Value = 0;

        if (prevHolder != 0 &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects
                .TryGetValue(prevHolder, out var holderObj))
        {
            if (holderObj.TryGetComponent(out PlayerCombat serverCombat)) serverCombat.SetHeldGun(null);
        }

        ClearHolderClientRpc(prevHolder, transform.position, transform.rotation);
        NetworkObject.Despawn(true);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _networkHeld.OnValueChanged    -= OnHeldChanged;
        _holderNetObjId.OnValueChanged -= OnHolderChanged;
        _ammoRemaining.OnValueChanged  -= OnAmmoChanged;
        _netMaxAmmo.OnValueChanged     -= (_, v) => UpdateAmmoDisplay(_ammoRemaining.Value);

        ulong holderId = _holderNetObjId.Value;
        if (holderId != 0 &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects
                .TryGetValue(holderId, out var netObj))
        {
            if (netObj.TryGetComponent(out PlayerCombat combat)) combat.SetHeldGun(null);
            var aim = netObj.GetComponentInChildren<ArmAimController>();
            if (aim != null) { aim.SetGunRenderer(null); aim.SetLeftGrip(null); }
        }
    }

    // ── Ammo API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Server only. Attempts to consume one bullet.
    /// Returns true (can fire) or false (empty — caller should auto-throw).
    /// _maxAmmo == 0 means infinite.
    /// </summary>
    public bool UseAmmo()
    {
        if (!IsServer || _maxAmmo <= 0) return true;
        if (_ammoRemaining.Value <= 0) return false;
        _ammoRemaining.Value--;
        return true;
    }

    /// <summary>Server only. Adds ammo up to _maxAmmo (e.g. from AmmoPickup card).</summary>
    public void RefillAmmo(int amount)
    {
        if (!IsServer || _maxAmmo <= 0) return;
        _ammoRemaining.Value = Mathf.Min(_maxAmmo, _ammoRemaining.Value + amount);
    }

    void OnAmmoChanged(int prev, int current) => UpdateAmmoDisplay(current);

    void UpdateAmmoDisplay(int current)
    {
        if (_ammoDisplay == null) return;
        int maxAmmo = _netMaxAmmo.Value > 0 ? _netMaxAmmo.Value : _maxAmmo;
        _ammoDisplay.text = maxAmmo > 0 ? $"{current}/{maxAmmo}" : string.Empty;
    }

    public Transform GetAttachPoint() => _handAttachPoint;
    public Transform GetFirePoint()   => _firePoint;

    // ── Flip helper — keeps child Transforms aligned with the flipped sprite ──

    void SetFlipY(bool flip)
    {
        if (_sr != null) _sr.flipY = flip;

        // Mirror fire point Y so bullets exit the correct barrel side when the gun is flipped
        if (_firePoint != null)
        {
            Vector3 fp = _firePoint.localPosition;
            _firePoint.localPosition = new Vector3(fp.x, flip ? -Mathf.Abs(fp.y) : Mathf.Abs(fp.y), fp.z);
        }
    }
}
