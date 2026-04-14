using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// GunRegistry removed — gun is now a NetworkObject synced by the server

public class Gun : NetworkBehaviour
{
    [SerializeField] Transform  _handAttachPoint;
    [SerializeField] Transform  _firePoint;
    [SerializeField] float      _throwForce  = 10f;
    [SerializeField] LayerMask  _groundLayer;   // set this to your Ground layer in the Inspector

    // Server-authoritative state — all clients read these
    NetworkVariable<bool>  _networkHeld     = new(false,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    NetworkVariable<ulong> _holderNetObjId  = new(0,      NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    Rigidbody2D      _rb;
    NetworkTransform _netTransform;
    Collider2D[]     _allColliders;   // cached — avoids GetComponent in hot paths
    bool             _landed;

    void Awake()
    {
        _rb           = GetComponent<Rigidbody2D>();
        _netTransform = GetComponent<NetworkTransform>();
        _allColliders = GetComponents<Collider2D>();
    }

    public override void OnNetworkSpawn()
    {
        _networkHeld.OnValueChanged    += OnHeldChanged;
        _holderNetObjId.OnValueChanged += OnHolderChanged;

        // If already held when we joined, attach immediately
        if (_networkHeld.Value)
            AttachToHolder(_holderNetObjId.Value);
    }


    // ── Physics landing — solid collider hits ground ───────────────────

    void OnCollisionEnter2D(Collision2D col)
    {
        if (!IsServer || _landed || _networkHeld.Value) return;
        if ((_groundLayer.value & (1 << col.gameObject.layer)) == 0) return;

        _landed             = true;
        _rb.velocity        = Vector2.zero;
        _rb.angularVelocity = 0f;
        _rb.isKinematic     = true;
    }

    // ── Pickup — trigger collider overlaps player ──────────────────────

    void OnTriggerEnter2D(Collider2D col)
    {
        if (!IsServer || _networkHeld.Value) return;
        PlayerController pc = col.GetComponentInParent<PlayerController>();
        if (pc == null) return;
        DoPickup(pc);
    }

    void DoPickup(PlayerController pc)
    {
        // Block pickup if this player already holds any other gun —
        // they must throw first before picking up a new one
        foreach (var other in FindObjectsOfType<Gun>())
        {
            if (other != this && other.IsHeldBy(pc.NetworkObjectId))
                return;
        }

        // Server sets state — NetworkVariables push to all clients automatically
        _networkHeld.Value    = true;
        _holderNetObjId.Value = pc.NetworkObjectId;

        _rb.isKinematic = true;
        _rb.velocity    = Vector2.zero;

        // Disable NetworkTransform — gun will just follow the hand each frame
        if (_netTransform != null) _netTransform.enabled = false;

        foreach (var col in _allColliders) col.enabled = false;

        // Tell owner's ArmAimController about the gun sprite
        NotifyOwnerPickupClientRpc(pc.OwnerClientId);
    }

    [ClientRpc]
    void NotifyOwnerPickupClientRpc(ulong ownerClientId)
    {
        if (NetworkManager.Singleton.LocalClientId != ownerClientId) return;

        // Find the local PlayerController owned by this client
        var all = FindObjectsOfType<PlayerController>();
        foreach (var pc in all)
        {
            if (pc.OwnerClientId == ownerClientId)
            {
                if (pc.TryGetComponent(out ArmAimController aim))
                    aim.SetGunRenderer(GetComponentInChildren<SpriteRenderer>());

                if (pc.TryGetComponent(out PlayerCombat combat))
                {
                    combat.SetHeldGun(this);
                    Debug.Log($"[Gun] SetHeldGun called on client {ownerClientId} — firePoint assigned: {_firePoint != null}");
                }
                else
                {
                    Debug.LogWarning("[Gun] PlayerCombat component not found on player — is it on the root prefab GO?");
                }

                break;
            }
        }
    }

    // ── Follow hand — each client follows the hand of the holder on their own machine ──

    void Update()
    {
        if (!IsSpawned || !_networkHeld.Value) return;

        // Every client has a local copy of every player — each reads the holder's local hand transform
        // This means the gun snaps correctly on every screen without extra RPCs
        Transform hand = GetHolderHand(_holderNetObjId.Value);
        if (hand == null) return;

        transform.position = hand.position;
        transform.rotation = hand.rotation;

        // Compute sprite flip locally from our own rotation — no extra sync needed
        // Gun crossed to the other side of the body when |angle| > 90°
        float angle = transform.eulerAngles.z;
        if (angle > 180f) angle -= 360f;
        bool crossedBody = Mathf.Abs(angle) > 90f;

        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.flipY = crossedBody;
    }

    void OnHeldChanged(bool prev, bool current)
    {
        if (!current) return;
        AttachToHolder(_holderNetObjId.Value);
    }

    void OnHolderChanged(ulong prev, ulong current)
    {
        if (!_networkHeld.Value) return;
        AttachToHolder(current);
    }

    void AttachToHolder(ulong holderNetObjId)
    {
        // Disable physics locally — Update() drives position from here
        if (_rb != null)
        {
            _rb.isKinematic = true;
            _rb.velocity    = Vector2.zero;
        }
        if (_netTransform != null) _netTransform.enabled = false;

        foreach (var col in _allColliders) col.enabled = false;
    }

    Transform GetHolderHand(ulong holderNetObjId)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects
            .TryGetValue(holderNetObjId, out var netObj)) return null;

        var pc = netObj.GetComponent<PlayerController>();
        return pc != null ? pc.GunHandAttach : null;
    }

    // ── Throw ──────────────────────────────────────────────────────────

    /// <summary>Server only — releases the gun and flings it in throwDir.</summary>
    public void Throw(Vector2 throwDir)
    {
        if (!IsServer) return;

        // Save holder before clearing — needed so the ClientRpc knows who to clear
        ulong prevHolder      = _holderNetObjId.Value;
        _networkHeld.Value    = false;
        _holderNetObjId.Value = 0;
        _landed               = false;

        // Re-enable physics with throw velocity and a random spin for feel
        _rb.isKinematic     = false;
        _rb.velocity        = throwDir * _throwForce;
        _rb.angularVelocity = UnityEngine.Random.Range(-200f, 200f);

        // Re-enable all colliders so the gun can land and be picked up again
        foreach (var col in _allColliders) col.enabled = true;

        // Re-enable NetworkTransform so thrown arc syncs to all clients
        if (_netTransform != null) _netTransform.enabled = true;

        // Tell clients to clear gun references — only from the player who held it
        ClearHolderClientRpc(prevHolder);
    }

    [ClientRpc]
    void ClearHolderClientRpc(ulong holderNetObjId)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects
                .TryGetValue(holderNetObjId, out var netObj)) return;

        if (netObj.TryGetComponent(out PlayerCombat combat)) combat.SetHeldGun(null);
        if (netObj.TryGetComponent(out ArmAimController aim)) aim.SetGunRenderer(null);
    }

    /// <summary>Returns true if this gun is currently held by the given NetworkObject.</summary>
    public bool IsHeldBy(ulong networkObjectId) =>
        _networkHeld.Value && _holderNetObjId.Value == networkObjectId;

    // ── Out-of-world removal (called by GunSpawner) ────────────────────

    /// <summary>
    /// Server only. Clears held references on all clients then despawns the NetworkObject.
    /// </summary>
    public void DespawnSelf()
    {
        if (!IsServer) return;
        ulong prevHolder      = _holderNetObjId.Value;
        _networkHeld.Value    = false;
        _holderNetObjId.Value = 0;
        ClearHolderClientRpc(prevHolder);
        NetworkObject.Despawn(true);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _networkHeld.OnValueChanged    -= OnHeldChanged;
        _holderNetObjId.OnValueChanged -= OnHolderChanged;

        // Only clear the player who was holding this gun (0 means nobody)
        ulong holderId = _holderNetObjId.Value;
        if (holderId != 0 &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.SpawnManager.SpawnedObjects
                .TryGetValue(holderId, out var netObj))
        {
            if (netObj.TryGetComponent(out PlayerCombat combat)) combat.SetHeldGun(null);
            if (netObj.TryGetComponent(out ArmAimController aim)) aim.SetGunRenderer(null);
        }
    }

    // ── Public helpers ─────────────────────────────────────────────────

    public Transform GetAttachPoint() => _handAttachPoint;
    public Transform GetFirePoint()   => _firePoint;
}
