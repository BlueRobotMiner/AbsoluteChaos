using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// GunRegistry removed — gun is now a NetworkObject synced by the server

public class Gun : NetworkBehaviour
{
    [SerializeField] Transform _handAttachPoint;

    // Server-authoritative state — all clients read these
    NetworkVariable<bool>  _networkHeld     = new(false,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    NetworkVariable<ulong> _holderNetObjId  = new(0,      NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    Rigidbody2D     _rb;
    NetworkTransform _netTransform;
    bool            _landed;

    void Awake()
    {
        _rb           = GetComponent<Rigidbody2D>();
        _netTransform = GetComponent<NetworkTransform>();
    }

    public override void OnNetworkSpawn()
    {
        _networkHeld.OnValueChanged    += OnHeldChanged;
        _holderNetObjId.OnValueChanged += OnHolderChanged;

        // If already held when we joined, attach immediately
        if (_networkHeld.Value)
            AttachToHolder(_holderNetObjId.Value);
    }

    public override void OnNetworkDespawn()
    {
        _networkHeld.OnValueChanged    -= OnHeldChanged;
        _holderNetObjId.OnValueChanged -= OnHolderChanged;
    }

    // ── Physics landing (server only) ──────────────────────────────────

    void OnTriggerEnter2D(Collider2D col)
    {
        if (!IsServer) return;

        // Land on ground
        if (!_landed && col.gameObject.layer == LayerMask.NameToLayer("Ground"))
        {
            _landed = true;
            _rb.velocity        = Vector2.zero;
            _rb.angularVelocity = 0f;
            _rb.isKinematic     = true;
            return;
        }

        // Pickup attempt — player walks into trigger
        if (_networkHeld.Value) return;
        PlayerController pc = col.GetComponentInParent<PlayerController>();
        if (pc == null) return;
        DoPickup(pc);
    }

    void DoPickup(PlayerController pc)
    {
        // Server sets state — NetworkVariables push to all clients automatically
        _networkHeld.Value    = true;
        _holderNetObjId.Value = pc.NetworkObjectId;

        _rb.isKinematic = true;
        _rb.velocity    = Vector2.zero;

        // Disable NetworkTransform — gun will just follow the hand each frame
        if (_netTransform != null) _netTransform.enabled = false;

        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

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
                var aim = pc.GetComponent<ArmAimController>();
                if (aim != null)
                    aim.SetGunRenderer(GetComponentInChildren<SpriteRenderer>());
                break;
            }
        }
    }

    // ── Follow hand on all clients ──────────────────────────────────────

    void Update()
    {
        if (!_networkHeld.Value) return;

        Transform hand = GetHolderHand(_holderNetObjId.Value);
        if (hand == null) return;

        transform.position = hand.position;
        transform.rotation = hand.rotation;
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

        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
    }

    Transform GetHolderHand(ulong holderNetObjId)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects
            .TryGetValue(holderNetObjId, out var netObj)) return null;

        var pc = netObj.GetComponent<PlayerController>();
        return pc != null ? pc.GunHandAttach : null;
    }

    // ── Public helpers ─────────────────────────────────────────────────

    public Transform GetAttachPoint() => _handAttachPoint;
}
